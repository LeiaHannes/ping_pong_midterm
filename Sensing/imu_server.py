import json
import socket
import time

import legoeducation as le

CARD_COLOR = le.LEGO_COLOR_YELLOW
CARD_SERIAL = "0994"
UNITY_ADDR = ("127.0.0.1", 5066)
MIN_SEND_INTERVAL = 0.005   # never send faster than 200 Hz


def main():
    motor = le.DoubleMotor()
    print("Connecting... (make sure the Double Motor is on and broadcasting)")
    motor.connect(card_color=CARD_COLOR, card_serial=CARD_SERIAL)
    if not motor.connected:
        raise SystemExit("Could not connect to the Double Motor")
    print("Connected. Press Ctrl+C to stop.")

    motor.imu_reset_yaw_axis(0)
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

    last = None
    last_send = 0.0
    sent = 0
    rate_timer = time.perf_counter()

    try:
        while True:
            imu = motor.imu_device
            if imu is None:
                time.sleep(0.01)
                continue

            now = time.perf_counter()
            sample = (imu.yaw, imu.pitch, imu.roll)
            if sample != last and now - last_send >= MIN_SEND_INTERVAL:
                # Tenths of a degree -> degrees
                msg = {"t": time.time(), "ypr": [v / 10.0 for v in sample]}
                sock.sendto(json.dumps(msg).encode("utf-8"), UNITY_ADDR)
                last = sample
                last_send = now
                sent += 1

            if now - rate_timer >= 2.0:
                print(f"{sent / (now - rate_timer):.0f} updates/s")
                sent = 0
                rate_timer = now

            time.sleep(0.001)
    except KeyboardInterrupt:
        pass
    finally:
        motor.disconnect()


if __name__ == "__main__":
    main()