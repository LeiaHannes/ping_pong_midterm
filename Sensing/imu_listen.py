import json
import socket
import time

sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
sock.bind(("0.0.0.0", 5067))
print("Listening on port 5066...")

count, timer = 0, time.perf_counter()
while True:
    data, _ = sock.recvfrom(1024)
    msg = json.loads(data)
    count += 1
    now = time.perf_counter()
    if now - timer >= 1.0:
        w, x, y, z = msg["q"]
        print(f"{count / (now - timer):.0f} Hz   q = ({w:+.3f}, {x:+.3f}, {y:+.3f}, {z:+.3f})")
        count, timer = 0, now