import json
import socket
import threading
import time

import cv2
import numpy as np
import mediapipe as mp
from mediapipe.tasks import python as mp_python
from mediapipe.tasks.python import vision
from pupil_apriltags import Detector

MODEL_PATH = "models/pose_landmarker_full.task"
CAMERA_INDEX = 0
UNITY_ADDR = ("127.0.0.1", 5065)

IMU_LISTEN_PORT = 5067                  # the UNO Q sends here
IMU_UNITY_ADDR = ("127.0.0.1", 5066)    # relayed to Unity

# Player identification
PLAYER_TAG_FAMILY = "tag16h5"
PLAYER_TAG_ID = 1
PADDLE_TAG_ID = 2          # same family as the chest tag, on the paddle's forehand face
TAG_SIZE_M = 0.0525          # black square edge length, in meters
CAMERA_HFOV_DEG = 75.0     # approximate horizontal field of view of the webcam
MIN_DECISION_MARGIN = 40    # how clearly the tag must be read; raise if you get false detections
MAX_AMBIGUITY = 0.6        # best orientation's error must be below this fraction of the alternative's
MAX_POSES = 3           # how many people MediaPipe looks for
REQUIRE_TAG = True      # False = use the biggest person until the tag is seen
MAX_JUMP = 0.15         # how far (fraction of frame width) the player's torso may move between frames
LOST_AFTER = 0.5        # seconds without finding the player before their position is forgotten

# MediaPipe landmark indices (the person's own left/right)
L_SHOULDER, R_SHOULDER = 11, 12
L_ELBOW, R_ELBOW = 13, 14
L_WRIST, R_WRIST = 15, 16
L_HIP, R_HIP = 23, 24

CONNECTIONS = [
    (L_SHOULDER, R_SHOULDER), (L_HIP, R_HIP),
    (L_SHOULDER, L_HIP), (R_SHOULDER, R_HIP),
    (L_SHOULDER, L_ELBOW), (L_ELBOW, L_WRIST),
    (R_SHOULDER, R_ELBOW), (R_ELBOW, R_WRIST),
]


def draw_skeleton(frame, landmarks, color):
    h, w = frame.shape[:2]

    def px(i):
        lm = landmarks[i]
        return int(lm.x * w), int(lm.y * h)

    for a, b in CONNECTIONS:
        if landmarks[a].visibility > 0.5 and landmarks[b].visibility > 0.5:
            cv2.line(frame, px(a), px(b), color, 2)

    for i in (L_WRIST, R_WRIST):
        if landmarks[i].visibility > 0.5:
            cv2.circle(frame, px(i), 10, (0, 200, 255), -1)


def to_message(capture_time, world, image, tag_seen, paddle_n):
    """MediaPipe output -> Unity axes: x = player's right, y = up, z = toward the table."""
    lm = []
    for p in world:
        lm += [round(-p.x, 4), round(-p.y, 4), round(-p.z, 4)]

    vis = [round(p.visibility, 3) for p in image]

    hip_x = (image[L_HIP].x + image[R_HIP].x) / 2
    hip_y = (image[L_HIP].y + image[R_HIP].y) / 2
    root = [round(0.5 - hip_x, 4), round(0.5 - hip_y, 4)]

    msg = {"t": capture_time, "lm": lm, "vis": vis, "root": root, "tag": tag_seen}
    if paddle_n is not None:
        msg["pn"] = paddle_n
    return msg


def imu_relay(stop):
    """Receive IMU packets from the network and pass each one to Unity immediately."""
    rx = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    rx.bind(("0.0.0.0", IMU_LISTEN_PORT))
    rx.settimeout(0.5)                   # wake up regularly to check whether to stop
    tx = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

    while not stop.is_set():
        try:
            data, _ = rx.recvfrom(1024)
        except socket.timeout:
            continue

        # Restamp with this computer's clock so Unity's timing readouts are meaningful
        msg = json.loads(data)
        msg["t"] = time.time()
        tx.sendto(json.dumps(msg).encode("utf-8"), IMU_UNITY_ADDR)

    rx.close()
    tx.close()


def torso_quad(landmarks, w, h):
    """Shoulders and hips as a 4-point outline in pixels."""
    pts = [[landmarks[i].x * w, landmarks[i].y * h] for i in (L_SHOULDER, R_SHOULDER, R_HIP, L_HIP)]
    return np.array(pts, dtype=np.float32).reshape(-1, 1, 2)


def find_tags(detector, gray):
    """All confidently read tags, by ID."""
    found = {}
    for tag in detector.detect(gray):
        if tag.hamming == 0 and tag.decision_margin >= MIN_DECISION_MARGIN:
            found[tag.tag_id] = tag
    return found


def paddle_normal(tag, camera_matrix):
    """Direction the tagged paddle face points, in Unity axes, or None if the reading is ambiguous."""
    # Tag corners in the tag's own frame, in the order the detector reports them
    s = TAG_SIZE_M / 2
    object_pts = np.array([[-s, s, 0], [s, s, 0], [s, -s, 0], [-s, -s, 0]], dtype=np.float64)
    image_pts = tag.corners.astype(np.float64)

    ok, rvecs, tvecs, errors = cv2.solvePnPGeneric(
        object_pts, image_pts, camera_matrix, np.zeros(4),
        flags=cv2.SOLVEPNP_IPPE_SQUARE)
    if not ok or len(rvecs) == 0:
        return None

    # Two candidate orientations: keep the best only if it clearly beats the other
    errors = np.asarray(errors).ravel()
    order = np.argsort(errors)
    if len(order) > 1 and errors[order[0]] > MAX_AMBIGUITY * errors[order[1]]:
        return None

    rotation, _ = cv2.Rodrigues(rvecs[order[0]])
    # The tag's z axis points into the tag; converting camera axes to Unity's negates
    # all three, which cancels that sign, so the face direction is the third column
    n = rotation[:, 2]
    return [round(float(v), 4) for v in n]

def choose_player(poses, w, h, tag, last_center):
    """Index of the pose that belongs to the player, or None."""
    quads = [torso_quad(p, w, h) for p in poses]

    # 1. Whoever has the player's tag on their chest
    if tag is not None:
        point = (float(tag.center[0]), float(tag.center[1]))
        for i, quad in enumerate(quads):
            if cv2.pointPolygonTest(quad, point, False) >= 0:
                return i

    # 2. Tag hidden (turned sideways, arm in front): stay with whoever is where the player just was
    if last_center is not None:
        dists = [np.linalg.norm(q.reshape(-1, 2).mean(axis=0) - last_center) for q in quads]
        best = int(np.argmin(dists))
        if dists[best] < MAX_JUMP * w:
            return best

    # 3. Nobody identified yet
    if not REQUIRE_TAG:
        return int(np.argmax([cv2.contourArea(q) for q in quads]))
    return None


def main():
    stop = threading.Event()
    relay = threading.Thread(target=imu_relay, args=(stop,), daemon=True)
    relay.start()

    options = vision.PoseLandmarkerOptions(
        base_options=mp_python.BaseOptions(model_asset_path=MODEL_PATH),
        running_mode=vision.RunningMode.VIDEO,
        num_poses=MAX_POSES,
    )
    detector = Detector(families=PLAYER_TAG_FAMILY, quad_decimate=2.0)

    cap = cv2.VideoCapture(CAMERA_INDEX, cv2.CAP_DSHOW)
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, 1280)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 720)
    if not cap.isOpened():
        raise SystemExit(f"Could not open camera {CAMERA_INDEX}")

    width = cap.get(cv2.CAP_PROP_FRAME_WIDTH)
    height = cap.get(cv2.CAP_PROP_FRAME_HEIGHT)
    focal = (width / 2) / np.tan(np.radians(CAMERA_HFOV_DEG / 2))
    camera_matrix = np.array([[focal, 0, width / 2],
                                [0, focal, height / 2],
                                [0, 0, 1]], dtype=np.float64)

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    start = time.perf_counter()
    fps, frame_count, fps_timer = 0.0, 0, start
    last_center = None
    last_found = 0.0

    with vision.PoseLandmarker.create_from_options(options) as landmarker:
        while True:
            ok, frame = cap.read()
            if not ok:
                break
            capture_time = time.time()
            h, w = frame.shape[:2]

            # Find the player's tag (on the unflipped image: tags can't be read mirrored)
            gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
            tags = find_tags(detector, gray)
            tag = tags.get(PLAYER_TAG_ID)
            paddle_tag = tags.get(PADDLE_TAG_ID)

            # Track everyone in view
            rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
            timestamp_ms = int((time.perf_counter() - start) * 1000)
            result = landmarker.detect_for_video(mp_image, timestamp_ms)

            now = time.perf_counter()
            player = None
            if result.pose_landmarks:
                player = choose_player(result.pose_landmarks, w, h, tag, last_center)
                for i, lms in enumerate(result.pose_landmarks):
                    color = (255, 255, 255) if i == player else (90, 90, 90)
                    draw_skeleton(frame, lms, color)

            if player is not None:
                image_lms = result.pose_landmarks[player]
                last_center = torso_quad(image_lms, w, h).reshape(-1, 2).mean(axis=0)
                last_found = now
                pn = paddle_normal(paddle_tag, camera_matrix) if paddle_tag is not None else None
                msg = to_message(capture_time, result.pose_world_landmarks[player],
                                image_lms, tag is not None, pn)
                sock.sendto(json.dumps(msg).encode("utf-8"), UNITY_ADDR)
            elif now - last_found > LOST_AFTER:
                last_center = None

            if tag is not None:
                corners = tag.corners.astype(np.int32).reshape(-1, 1, 2)
                cv2.polylines(frame, [corners], True, (0, 255, 0), 3)

            if paddle_tag is not None:
                corners = paddle_tag.corners.astype(np.int32).reshape(-1, 1, 2)
                cv2.polylines(frame, [corners], True, (255, 150, 0), 3)
                
            # Frame rate
            frame_count += 1
            if now - fps_timer >= 1.0:
                fps = frame_count / (now - fps_timer)
                frame_count, fps_timer = 0, now

            display = cv2.flip(frame, 1)   # mirror only the display, like a mirror
            status = "PLAYER LOCKED" if last_center is not None else "SHOW YOUR TAG"
            status_color = (0, 255, 0) if last_center is not None else (0, 0, 255)
            cv2.putText(display, f"{fps:.0f} fps   {status}", (10, 35),
                        cv2.FONT_HERSHEY_SIMPLEX, 1.0, status_color, 2)
            cv2.imshow("Pose server (Q to quit)", display)
            if cv2.waitKey(1) & 0xFF == ord("q"):
                break

    cap.release()
    cv2.destroyAllWindows()
    stop.set()
    relay.join(timeout=1.0)


if __name__ == "__main__":
    main()