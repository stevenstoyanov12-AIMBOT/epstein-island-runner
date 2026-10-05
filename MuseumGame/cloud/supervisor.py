#!/usr/bin/env python3
"""Card supervisor: starts coturn, Xvfb and N game instances, restarts them, reports to the Worker.

Env (set in the Vast template, never baked into the image):
  CLOUD_KEY      shared secret, same value as the Worker's CLOUD_SECRET
  CLOUD_REGION   eu | us
  CLOUD_SERVER   https://museum-server.steven-stoyanov12.workers.dev   (default)
  INSTANCES      game instances on this card, default 6, never more than 8 (GeForce NVENC session limit)
  FPS            30..60, default 60
Vast provides PUBLIC_IPADDR and VAST_UDP_PORT_3478 / VAST_TCP_PORT_3478 for the direct port mapping.
"""
import json
import os
import secrets
import signal
import socket
import subprocess
import sys
import time
import urllib.request

SERVER = os.environ.get("CLOUD_SERVER", "https://museum-server.steven-stoyanov12.workers.dev").rstrip("/")
KEY = os.environ.get("CLOUD_KEY", "")
REGION = os.environ.get("CLOUD_REGION", "eu")
N = max(1, min(8, int(os.environ.get("INSTANCES", "6"))))
FPS = os.environ.get("FPS", "60")
CARD = os.environ.get("CARD_ID") or os.environ.get("VAST_CONTAINERLABEL") or socket.gethostname()
GAME = "/app/game/Museum.x86_64"
LOGS = "/app/logs"
TURN_PORT = 3478


def log(*a):
    print(time.strftime("%H:%M:%S"), *a, flush=True)


def local_ip():
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        s.connect(("8.8.8.8", 80))
        return s.getsockname()[0]
    finally:
        s.close()


def public_ip():
    ip = os.environ.get("PUBLIC_IPADDR")
    if ip:
        return ip
    return urllib.request.urlopen("https://api.ipify.org", timeout=10).read().decode().strip()


def gpu():
    try:
        out = subprocess.check_output(
            ["nvidia-smi", "--query-gpu=name,utilization.gpu,utilization.encoder,memory.used,memory.total",
             "--format=csv,noheader,nounits"], timeout=5).decode().strip().split(", ")
        return {"name": out[0], "util": int(out[1]), "enc": int(out[2]), "memUsed": int(out[3]), "memTotal": int(out[4])}
    except Exception:
        return None


def start_turn(lip, secret):
    conf = f"""listening-port={TURN_PORT}
listening-ip=0.0.0.0
relay-ip={lip}
min-port=49160
max-port=49999
use-auth-secret
static-auth-secret={secret}
realm=museum
fingerprint
no-tls
no-dtls
no-cli
no-multicast-peers
allowed-peer-ip={lip}
total-quota=64
log-file=stdout
simple-log
"""
    with open("/tmp/turnserver.conf", "w") as fh:
        fh.write(conf)
    return subprocess.Popen(["turnserver", "-c", "/tmp/turnserver.conf"],
                            stdout=open(f"{LOGS}/turn.log", "a"), stderr=subprocess.STDOUT)


def start_game(slot):
    env = dict(os.environ, DISPLAY=":99", CLOUD_SIGNAL=SERVER.replace("https://", "wss://") + "/cloud/signal")
    args = [GAME, "-force-vulkan", "-screen-width", "1280", "-screen-height", "720", "-screen-fullscreen", "0",
            "-slot", str(slot), "-card", CARD, "-fps", FPS, "-logFile", f"{LOGS}/slot{slot}.log"]
    return subprocess.Popen(args, env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def report(ip, udp, tcp, secret, procs):
    body = json.dumps({
        "card": CARD, "region": REGION, "ip": ip, "udp": udp, "tcp": tcp, "turnSecret": secret,
        "slots": [{"slot": i, "alive": p is not None and p.poll() is None} for i, p in enumerate(procs)],
        "gpu": gpu(),
    }).encode()
    req = urllib.request.Request(f"{SERVER}/cloud/report", data=body, method="POST",
                                 headers={"content-type": "application/json", "x-cloud-key": KEY})
    try:
        urllib.request.urlopen(req, timeout=8).read()
    except Exception as e:
        log("report failed:", e)


def main():
    if not KEY:
        sys.exit("CLOUD_KEY is not set (Vast env var)")
    os.makedirs(LOGS, exist_ok=True)
    lip, ip = local_ip(), public_ip()
    udp = int(os.environ.get(f"VAST_UDP_PORT_{TURN_PORT}", TURN_PORT))
    tcp = int(os.environ.get(f"VAST_TCP_PORT_{TURN_PORT}", 0)) or None
    secret = secrets.token_hex(24)          # TURN secret lives only on this card and in the Worker's memory
    log(f"card {CARD} region {REGION} public {ip} udp {udp} tcp {tcp} local {lip}, {N} instances")

    turn = start_turn(lip, secret)
    xvfb = subprocess.Popen(["Xvfb", ":99", "-screen", "0", "1280x720x24", "-nolisten", "tcp"])
    time.sleep(2)
    procs = [None] * N
    restarts = [0] * N
    stop = []
    signal.signal(signal.SIGTERM, lambda *_: stop.append(1))

    next_report = 0
    while not stop:
        if turn.poll() is not None:
            log("coturn died, restarting")
            turn = start_turn(lip, secret)
        if xvfb.poll() is not None:
            log("Xvfb died, restarting")
            xvfb = subprocess.Popen(["Xvfb", ":99", "-screen", "0", "1280x720x24", "-nolisten", "tcp"])
            time.sleep(2)
        for i in range(N):
            p = procs[i]
            if p is None or p.poll() is not None:
                if p is not None:
                    restarts[i] += 1
                    log(f"slot {i} exited ({p.returncode}), restart #{restarts[i]}")
                    time.sleep(1)
                procs[i] = start_game(i)
        if time.time() >= next_report:
            report(ip, udp, tcp, secret, procs)
            next_report = time.time() + 10
        time.sleep(1)

    for p in procs:
        if p:
            p.terminate()
    turn.terminate()
    xvfb.terminate()


if __name__ == "__main__":
    main()
