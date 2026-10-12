import time
import legoeducation as le

CARD_COLOR = le.LEGO_COLOR_YELLOW
CARD_SERIAL = "0994"


def main():
    motor = le.DoubleMotor()
    print("Connecting... (make sure the Double Motor is on and broadcasting)")
    motor.connect(card_color=CARD_COLOR, card_serial=CARD_SERIAL)
    if not motor.connected:
        raise SystemExit("Could not connect to the Double Motor")
    print("Connected. Press Ctrl+C to stop.")

    from collections import Counter

    counts = Counter()

    def on_notify(data):
        for item in le.device_notification_parser(data):
            counts[type(item).__name__] += 1

    motor.set_notification_callback(on_notify)

    motor.imu_reset_yaw_axis(0)

    last = None
    updates = 0
    rate_timer = print_timer = time.perf_counter()

    try:
        while True:
            imu = motor.imu_device
            if imu is None:
                time.sleep(0.01)
                continue

            # Count how often the values actually change
            sample = (imu.yaw, imu.pitch, imu.roll)
            if sample != last:
                updates += 1
                last = sample

            now = time.perf_counter()
            if now - print_timer >= 0.25:
                print(f"yaw={imu.yaw:>7}  pitch={imu.pitch:>7}  roll={imu.roll:>7}   "
                      f"gyro=({imu.gyroscopeX}, {imu.gyroscopeY}, {imu.gyroscopeZ})")
                print_timer = now

            if now - rate_timer >= 2.0:
                print({name: round(n / (now - rate_timer)) for name, n in counts.items()})
                counts.clear()
                rate_timer = now    

            time.sleep(0.002)
    except KeyboardInterrupt:
        pass
    finally:
        motor.disconnect()


if __name__ == "__main__":
    main()