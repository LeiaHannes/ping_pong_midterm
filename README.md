# Motion-Tracked Table Tennis

A first-person table tennis game in Unity, played with your real body. A webcam tracks your arm and body position, an IMU on a real paddle tracks its orientation, and a custom physics engine simulates the ball with drag, spin, and realistic bounces. You rally against a scripted opponent with a body, or practice against a ball machine.

---

## Contents

- [Overview](#overview)
- [How it works](#how-it-works)
- [Repository layout](#repository-layout)
- [Requirements](#requirements)
- [Setup](#setup)
- [Running the game](#running-the-game)
- [Controls](#controls)
- [Calibration](#calibration)
- [Tuning](#tuning)
- [Building](#building)
- [Troubleshooting](#troubleshooting)
- [Known limitations](#known-limitations)
- [Future work](#future-work)
- [Third-party software and assets](#third-party-software-and-assets)

---

## Overview

**Features**

- Custom, deterministic ball physics: gravity, quadratic air drag, Magnus force, spin-aware bounces with friction, and swept collision detection that prevents tunneling at any speed
- Paddle position from webcam pose tracking (MediaPipe), with speed-adaptive smoothing and latency-compensating prediction
- Paddle orientation from an MPU-6050 IMU on an Arduino UNO Q, fused on the board at about 650 Hz and streamed at about 94 Hz
- AprilTag on the player's chest to identify the player when other people are in view, and an AprilTag on the paddle to calibrate its heading
- Practice mode with a ball machine, a referee that judges every return, an on-screen HUD, and 3D contact sounds
- A scripted opponent with a body that predicts the ball, swings to meet it, and returns it to a target
- First-person view with tracked floating hands; a furnished, lit room
- Mouse control as a fallback and for development
- Fullscreen Windows build

---

## How it works

### System architecture

```
┌──────────────────────────┐   Wi-Fi UDP :5067    ┌───────────────────────────────┐
│ Arduino UNO Q            │ ───────────────────▶ │ PC: Sensing/pose_server.py     │
│  MCU sketch:             │                      │  • webcam → MediaPipe pose     │
│   MPU-6050 → Mahony      │                      │  • AprilTags (chest, paddle)   │
│   fusion → quaternion    │                      │  • relays IMU packets          │
│  Linux Python (main.py): │                      └──────────────┬────────────────┘
│   forwards over Wi-Fi    │                                     │ localhost UDP
└──────────────────────────┘                     pose :5065      │      IMU :5066
                                                                 ▼
                                   ┌───────────────────────────────────────────────┐
                                   │ Unity game                                    │
                                   │  PoseReceiver / ImuReceiver (background       │
                                   │  threads) → SensorPaddleInput → PaddleController│
                                   │  → BallPhysics (contacts) → referee, sounds,  │
                                   │  HUD, opponent                                │
                                   └───────────────────────────────────────────────┘
```

| Port | From → to | Contents |
|---|---|---|
| 5065 | `pose_server.py` → Unity (localhost) | Player skeleton, hip position, chest-tag status, paddle-tag direction |
| 5066 | `pose_server.py` relay → Unity (localhost) | IMU quaternions, restamped with the PC clock |
| 5067 | UNO Q → `pose_server.py` (Wi-Fi) | IMU quaternions |

The IMU is relayed through `pose_server.py` rather than sent straight to Unity because Python already has firewall permission to receive from the network, and localhost traffic to Unity is never blocked.

### Ball physics

All ball physics is custom C# (Unity's built-in physics engine never touches the ball). `BallPhysics.Simulate` is a pure, deterministic function: it advances a `BallState` (position, velocity, spin, phase) without side effects. That lets the same code drive the live ball, the trajectory predictor, and the opponent's shot planning.

- **Integration:** semi-implicit Euler in 1 ms substeps, run 10 per physics tick (100 Hz).
- **Forces:** gravity; drag `-k|v|v` with `k` computed from real ball size, mass, and drag coefficient (terminal velocity ≈ 8.8 m/s); Magnus `K(ω × v)`.
- **Contacts:** one general model for every surface. The normal component reverses and shrinks by the coefficient of restitution; friction acts on the contact point's slip, limited both by the friction coefficient and by the point where the ball rolls cleanly (2/5 of the slip, for a hollow sphere). Friction changes both velocity and spin, which produces topspin kick and backspin check-up.
- **Swept collisions:** the table, net, and paddle are detected by checking whether the ball *crossed* the surface during a substep, with moving paddles interpolated across each tick.
- **Calibration:** table restitution is set so a 30 cm drop rebounds about 23 cm, matching the official rule.
- **Rendering:** the visible ball is interpolated between physics states, so motion stays smooth at any refresh rate.

Contacts are reported through static C# events (`BallSimulator.AnyContact`, `AnyDied`), so the referee, sounds, HUD, opponent, and logger each subscribe independently.

### Paddle control

Paddle inputs implement `IPaddleInput`; `PaddleController` uses whichever one is enabled, tracks previous and current poses, and computes velocity. Ball collisions use the paddle's motion across each tick, including rotation, so wrist movement adds speed and spin.

**Position (camera):** MediaPipe world landmarks (meters, hip-centered) are converted to Unity axes in Python (all three axes negated). Side-to-side stepping comes from the hips' position in the image. In Unity, the wrist is mapped onto the table with per-axis reach scales, smoothed with a One Euro filter, and predicted forward from its velocity only while the hand is genuinely moving (prediction is cut immediately when the hand slows or turns). A critically damped spring (`SmoothDamp`) then follows the prediction without overshoot, upsampling 30 Hz camera data to 100 Hz physics.

**Orientation (IMU):** the UNO Q's microcontroller reads the MPU-6050 over I2C, removes the gyro bias (measured at startup and re-learned whenever the sensor is still), and runs a Mahony filter that trusts the accelerometer only when it reads close to 1 g. Quaternions are converted from the sensor's right-handed, z-up frame to Unity's left-handed, y-up frame.

**Blade placement:** the blade center is placed a fixed distance up the handle from the tracked wrist, along the IMU's orientation, so wrist rotation moves the blade immediately.

### Player identification

MediaPipe tracks up to three people. The player is whoever has the chest AprilTag inside their torso outline (shoulders and hips). When the tag is hidden, for example while turning sideways mid-stroke, the player is whoever is closest to the player's last position. Nothing is sent until the tag has been seen.

### Gameplay

- **Ball machine:** fires balls with configurable speed, angle, spin, and randomness. Auto-fire waits until no ball is in play, so rallies aren't interrupted.
- **Practice referee:** follows each ball through *incoming → hittable → returned* and judges the outcome (good return, net, out, landed on your side, missed, volley, or machine error). Stats and the last hit's speed and spin are shown on the HUD.
- **Scripted opponent:** when your return lands on the far side, it predicts where and when the ball will reach its hitting point, swings its visible paddle to meet it at that moment, and redirects the ball. The return's launch angle is found by bisection over simulated shots so it lands on a target (by default, the center of your half) and always clears the net.
- **Avatars:** a shared rig drives a torso, head, and floating hands from a paddle. The player sees only their hands in first person; the opponent's full body is visible, and its head tracks the ball.

---

## Repository layout

```
├─ Assets/
│  ├─ Scenes/
│  ├─ Prefabs/            Ball, Avatar, Window, CeilingLight
│  ├─ Materials/  Textures/  Audio/  Models/
│  └─ Scripts/
│     ├─ Physics/         PingPongSpec, BallState, BallPhysics, Contact, BallSimulator
│     ├─ Paddle/          PaddlePose, PaddleMotion, IPaddleInput, MousePaddleInput, PaddleController
│     ├─ Sensing/         PoseReceiver, ImuReceiver, SensorPaddleInput, OneEuroFilter3, PoseStickFigure
│     ├─ Avatar/          AvatarPose, IAvatarSource, AvatarRig, PaddleAvatarDriver
│     ├─ Gameplay/        BallMachine, PracticeReferee, ContactAudio, ScriptedOpponent, OpponentPaddleMotion
│     ├─ UI/              PracticeHUD, DisplayControls
│     └─ Debug/           DebugControls, TrajectoryPredictor, BallTrail, ContactLogger
├─ Sensing/
│  ├─ pose_server.py      Camera tracking, AprilTags, IMU relay (run this to play)
│  ├─ pose_preview.py     Standalone pose tracking preview, for testing
│  ├─ imu_listen.py       Prints incoming IMU packets, for testing the network link
│  ├─ imu_probe.py        LEGO Double Motor test (early prototype)
│  ├─ imu_server.py       LEGO Double Motor streamer (early prototype)
│  └─ models/             MediaPipe model files (not committed; see Setup)
├─ Firmware/
│  └─ UnoQ/imu-reader-and-broadcaster/
│     ├─ sketch/sketch.ino   MCU: MPU-6050, Mahony fusion, bias learning, LED status
│     └─ python/main.py      Linux side: forwards quaternions over Wi-Fi
└─ README.md
```

---

## Requirements

### Hardware

- Windows PC with a webcam (tested on an ASUS ROG Zephyrus G14; 720p, 30 fps)
- Arduino UNO Q
- MPU-6050 breakout (GY-521)
- USB-C power bank for the UNO Q
- A table tennis paddle (or a paddle-shaped piece of cardboard)
- Two printed AprilTags (family **tag16h5**): ID 0 for the chest, ID 1 for the paddle

### Software (all free)

- Unity 6 (LTS), Personal license, Universal 3D (URP) template
- Unity packages: Input System, Universal Render Pipeline, Unity UI (with TextMeshPro)
- Python 3, at a version MediaPipe publishes Windows builds for (check the `cp3XX` files under "Download files" on MediaPipe's PyPI page)
- Python packages: `mediapipe`, `opencv-python`, `numpy`, `pupil-apriltags`
- Arduino App Lab (for the UNO Q)

---

## Setup

### 1. Unity

1. Clone the repository to a short path outside OneDrive-synced folders (for example `C:\Dev\`).
2. Open it with Unity 6 LTS through Unity Hub.
3. In **Windows Settings → System → Display → Graphics**, set the Unity editor (and later the built `.exe`) to **High performance** so it uses the dedicated GPU.

### 2. Python (camera tracking)

From the `Sensing` folder:

```
py -3.12 -m venv .venv
.venv\Scripts\activate
pip install mediapipe opencv-python numpy pupil-apriltags
```

Use a Python version MediaPipe supports in place of `3.12`. If PowerShell blocks the activate script, run `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned` once.

Download the **Pose Landmarker (full)** model from Google's MediaPipe Pose Landmarker documentation and save it as `Sensing/models/pose_landmarker_full.task`. The **lite** model also works and is faster.

### 3. AprilTags

Download tag images from the **AprilRobotics/apriltag-imgs** repository, folder `tag16h5`:

| Tag | File | Printed size (black square) | Where |
|---|---|---|---|
| ID 0 | `tag16_05_00000.png` | 10–12 cm | Flat on the chest, ideally on cardboard |
| ID 1 | `tag16_05_00001.png` | about 8 cm | Flat on the paddle's forehand face |

Scale the images up with **nearest-neighbor** (no smoothing), keep a white margin around each tag, and print on matte paper. Set `TAG_SIZE_M` in `pose_server.py` to the paddle tag's measured black-square size in meters.

### 4. UNO Q and MPU-6050

**Wiring** (to the UNO Q header):

| GY-521 | UNO Q |
|---|---|
| VCC | 3.3V |
| GND | GND |
| SCL | SCL |
| SDA | SDA |
| AD0 | GND (I2C address 0x68) |

Use 3.3V, not 5V. Keep the I2C cable short (under about 50 cm).

**App Lab:**

1. Create an app and copy in `Firmware/UnoQ/imu-reader-and-broadcaster/sketch/sketch.ino` and `python/main.py`.
2. In `main.py`, set `PC_ADDR` to your PC's IPv4 address (from `ipconfig`) and port **5067**. The UNO Q and PC must be on the same Wi-Fi network.
3. Run it once from App Lab to confirm it works.
4. Next to **Run**, open the dropdown and turn on **Run at startup**, so the app starts by itself on battery power.

To keep the PC's address from changing, reserve it in your router's settings (DHCP reservation).

**LED matrix status:**

| Display | Meaning |
|---|---|
| `--` | Starting |
| `CA` | Measuring gyro bias: keep the paddle still |
| `PY` | Sensor running; the board's Python side isn't (yet) |
| `OK` (blinking) | Everything on the board is running |
| `ER` | Sensor not responding: check wiring |

### 5. Network

Set your Wi-Fi network's profile to **Private** (Settings → Network & internet → Wi-Fi). Allow Python through Windows Firewall on private networks when prompted. Unity only receives from localhost, so it needs no firewall permission.

---

## Running the game

1. **Power on the UNO Q** with the paddle lying still. Wait for the blinking `OK`.
2. **Start the camera script:** in `Sensing` with the venv active, run `python pose_server.py`. Put on the chest tag and check that the preview shows **PLAYER LOCKED**.
3. **Start the game:** press Play in Unity, or run the built `.exe`.
4. **Switch to body control:** press **M**.
5. **Calibrate:** press **C**, then hold your ready stance (paddle upright, face toward the table, paddle tag visible to the camera) until the countdown ends.
6. **Play:** press **R** to start auto-fire, or **F** for single balls.

If the UNO Q is plugged into the PC instead of running on battery, start its app from App Lab before step 2.

---

## Controls

| Key | Action |
|---|---|
| **M** | Switch paddle input between mouse and body tracking |
| **C** | Calibrate position and heading (3-second countdown) |
| **B** | IMU mounting calibration (two poses; only needed once per mounting) |
| **F** | Fire one ball |
| **R** | Toggle ball-machine auto-fire |
| **1 / 2 / 3** | Normal speed / quarter speed / 1/20 speed |
| **P** | Pause / resume |
| **I** | Toggle ball interpolation (debugging) |
| **F11** | Toggle fullscreen (in builds) |
| **Alt+F4** | Quit |

**Mouse mode:** move the mouse to move the paddle; **left click** to swing; **scroll** to tilt the face; **Q / E** to aim; **middle click** to reset angles. Scroll and Q/E also set the paddle angle in body mode if no IMU data is arriving.

---

## Calibration

### IMU mounting (B): once per physical mounting

Teaches the game how the sensor is attached to the paddle, using gravity in two known poses. Press **B** and follow the Console prompts:

1. Hold the **ready pose**: face toward the table, handle down.
2. Lay the paddle flat on a surface **exactly as the second prompt specifies** (which face up matters; laying it the other way mirrors two of the rotations).

The result is saved with `PlayerPrefs`. The editor and builds store this separately, so run it once in each.

### Session calibration (C): every session

Press **C** and hold your ready stance. This:

- maps your current wrist position onto the paddle's ready position, and records your stance for side-to-side movement;
- sets the IMU's heading. If the paddle tag is visible during the last half-second of the countdown, the heading comes from the tag's measured direction (averaged over several frames, with ambiguous readings rejected). Otherwise, it assumes the paddle faces the table.

The MPU-6050 has no magnetometer, so heading can drift slowly. Press **C** again whenever it does.

---

## Tuning

Most behavior is tunable in the Inspector. Recommended order: reduce latency first, then geometry, then smoothing, then prediction, then IMU smoothing, then hit feel.

| Where | Setting | Effect |
|---|---|---|
| `pose_server.py` | `MAX_POSES`, model choice | Fewer people tracked and the lite model reduce camera latency |
| SensorPaddleInput | Wrist To Blade | Distance from wrist to blade center along the handle |
| SensorPaddleInput | Ready Position | Where calibration places the blade |
| SensorPaddleInput | Reach Scale, Lateral Scale | How far the paddle moves for a given arm or body movement |
| SensorPaddleInput | Min Cutoff, Beta | Steadiness at rest vs. responsiveness in fast moves |
| SensorPaddleInput | Follow Time | Final smoothing lag |
| SensorPaddleInput | Lead Time, Max Extrapolation | How far ahead to predict, to cover camera latency |
| SensorPaddleInput | Rest Speed, Full Speed | When prediction switches on |
| ImuReceiver | Follow Sharpness | Orientation smoothing; higher is less lag |
| `PingPongSpec.cs` | PaddleRestitution, PaddleFriction | Liveliness and spin of returns |
| ScriptedOpponent | Target, Return Speed, Topspin Rps, Hit Z | Where and how the opponent returns |
| BallMachine | Speed, Elevation, Side Angle, spin, jitter | The balls you receive |

Useful readouts: **PoseReceiver → Debug Age Ms / Debug Rate Hz**, **ImuReceiver → Debug Rate Hz**, **PaddleController → Debug Speed**, and the ball prefab's **Debug Speed / Spin / Phase**.

Inspector changes made during Play mode are lost when Play stops. To keep them, right-click a component → **Copy Component**, stop Play, then **Paste Component Values**.

---

## Building

1. **File → Build Profiles** (or Build Settings): select **Windows** and make sure the game scene is in the Scene List.
2. **Build** to a `Builds/` folder (ignored by Git). **Ctrl+B** rebuilds to the same location and runs it.
3. Player settings used: Fullscreen Window, native resolution, **Run In Background on** (so the game keeps running when the Python preview window has focus).

The Python script and UNO Q run alongside the build exactly as they do with the editor. A build's log is at `%USERPROFILE%\AppData\LocalLow\<Company>\<Product>\Player.log`.

---

## Troubleshooting

| Symptom | Likely cause and fix |
|---|---|
| Paddle doesn't follow you after pressing M | Preview says SHOW YOUR TAG (no data is sent until the chest tag is seen); not calibrated yet (press C); or SensorPaddleInput's Receiver/Imu slots are empty (both should be the Sensing object) |
| PoseReceiver Debug Rate Hz is 0 | `pose_server.py` isn't running or crashed (check its terminal), or another program (Unity in Play mode, a second build) is holding the port |
| No IMU data (ImuReceiver rate 0) | Check the LED matrix; the PC's IP may have changed (update `main.py`); Wi-Fi profile not Private; Python blocked by the firewall |
| Paddle slowly spins | Gyro bias was wrong at startup (paddle moved during `CA`). Lay it still for a few seconds so the bias re-learns, then press C |
| Two rotations are mirrored | Mounting calibration step 2 was done with the wrong face up. Redo B following the prompt exactly |
| Paddle lags behind sweeping motions | Reduce camera latency first (lighting, MAX_POSES, lite model), then raise Lead Time |
| Paddle wiggles at the end of motions | Lead Time or Max Extrapolation too high |
| Webcam fps drops | Dim lighting makes webcams lengthen exposure; light the player brightly |
| Opponent stands still | Check the Console for errors; OpponentAvatar's driver must have Paddle = OpponentPaddle and Sensor empty; ScriptedOpponent's Paddle Motion slot must be set |

---

## Known limitations

- Camera latency (roughly 50–80 ms) is only partly hidden by prediction; very fast strokes can blur in a 30 fps webcam image.
- Monocular depth is the weakest tracking axis, so forward and back movement is less precise than side to side.
- No magnetometer: IMU heading drifts slowly and needs occasional recalibration.
- The table is modeled as a thin surface, so balls hitting its side edge pass through. Net-cord deflections are not modeled.
- The opponent doesn't use physics for its hit: its paddle is visual, and the ball is redirected directly.
- The Python scripts and UNO Q app must be started separately from the game.

---

## Future work

- Match play: serves, scoring to 11, and a scoreboard
- Menus and a settings screen, including the calibration steps
- Bundling the Python script with the build (PyInstaller) and launching it automatically
- Varied opponent placement and pace, and an RL-trained opponent (Unity ML-Agents)
- Camera calibration and chest-tag pose estimation for true depth
- Continuous drift correction of the IMU heading from the paddle tag

---

## Third-party software and assets

- [Unity](https://unity.com/) 6, Personal license
- [MediaPipe](https://github.com/google-ai-edge/mediapipe) (Pose Landmarker) by Google
- [OpenCV](https://opencv.org/) and [NumPy](https://numpy.org/)
- [pupil-apriltags](https://github.com/pupil-labs/apriltags), Python bindings for the AprilTag 3 library
- [AprilTag](https://april.eecs.umich.edu/software/apriltag) and the [apriltag-imgs](https://github.com/AprilRobotics/apriltag-imgs) tag images, from the APRIL Robotics Laboratory, University of Michigan
- Arduino UNO Q, App Lab, and the Arduino RouterBridge and LED Matrix libraries

See each project for its license terms. Textures and sound effects are the author's own unless noted otherwise.
