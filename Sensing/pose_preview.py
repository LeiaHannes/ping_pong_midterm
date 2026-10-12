import time
import cv2
import mediapipe as mp
from mediapipe.tasks import python as mp_python
from mediapipe.tasks.python import vision

MODEL_PATH = "models/pose_landmarker_full.task"
CAMERA_INDEX = 0

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


def draw_skeleton(frame, landmarks):
    h, w = frame.shape[:2]

    def px(i):
        lm = landmarks[i]
        return int(lm.x * w), int(lm.y * h)

    for a, b in CONNECTIONS:
        if landmarks[a].visibility > 0.5 and landmarks[b].visibility > 0.5:
            cv2.line(frame, px(a), px(b), (255, 255, 255), 2)

    for i in (L_WRIST, R_WRIST):
        if landmarks[i].visibility > 0.5:
            cv2.circle(frame, px(i), 10, (0, 200, 255), -1)


def main():
    options = vision.PoseLandmarkerOptions(
        base_options=mp_python.BaseOptions(model_asset_path=MODEL_PATH),
        running_mode=vision.RunningMode.VIDEO,
        num_poses=1,
    )

    cap = cv2.VideoCapture(CAMERA_INDEX, cv2.CAP_DSHOW)
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, 1280)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 720)
    if not cap.isOpened():
        raise SystemExit(f"Could not open camera {CAMERA_INDEX}")

    start = time.perf_counter()
    fps, frame_count, fps_timer = 0.0, 0, start
    last_print = 0.0

    with vision.PoseLandmarker.create_from_options(options) as landmarker:
        while True:
            ok, frame = cap.read()
            if not ok:
                break

            rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
            timestamp_ms = int((time.perf_counter() - start) * 1000)
            result = landmarker.detect_for_video(mp_image, timestamp_ms)

            now = time.perf_counter()
            if result.pose_landmarks:
                draw_skeleton(frame, result.pose_landmarks[0])

                # World landmarks: meters, origin between the hips
                wrist = result.pose_world_landmarks[0][R_WRIST]
                if now - last_print > 0.5:
                    print(f"right wrist (m): x={wrist.x:+.2f}  y={wrist.y:+.2f}  z={wrist.z:+.2f}")
                    last_print = now

            # Frame rate
            frame_count += 1
            if now - fps_timer >= 1.0:
                fps = frame_count / (now - fps_timer)
                frame_count, fps_timer = 0, now

            display = cv2.flip(frame, 1)   # mirror only the display, like a mirror
            cv2.putText(display, f"{fps:.0f} fps", (10, 35),
                        cv2.FONT_HERSHEY_SIMPLEX, 1.0, (0, 255, 0), 2)
            cv2.imshow("Pose preview (Q to quit)", display)
            if cv2.waitKey(1) & 0xFF == ord("q"):
                break

    cap.release()
    cv2.destroyAllWindows()


if __name__ == "__main__":
    main()