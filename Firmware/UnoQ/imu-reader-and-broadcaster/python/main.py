import json
import socket
import time

from arduino.app_utils import App, Bridge

PC_ADDR = ("YOUR_PC_ADDRESS_HERE", 5067)   # your laptop's IPv4 address

sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
forwarded = 0


def on_quat(w, x, y, z):
    global forwarded
    msg = {"t": time.time(), "q": [w, x, y, z]}
    sock.sendto(json.dumps(msg).encode("utf-8"), PC_ADDR)
    forwarded += 1


Bridge.provide("imu_quat", on_quat)


def loop():
    global forwarded
    # Heartbeat: lets the sketch show on the LED matrix that this side is running
    Bridge.notify("py_alive")
    time.sleep(1)
    print(f"{forwarded} quaternions/s forwarded")
    forwarded = 0


App.run(user_loop=loop)