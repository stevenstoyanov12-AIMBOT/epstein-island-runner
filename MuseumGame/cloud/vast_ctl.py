#!/usr/bin/env python3
"""Rent, check and destroy the cloud streaming cards from the command line (run on your PC).

  Instance actions go through Vast's own CLI (pip install vastai), which needs a 2FA session:
    vastai tfa login --method-type totp -c <6-digit code>
  cloud/.env holds VAST_API_KEY (offer search) and CLOUD_KEY.

  python cloud/vast_ctl.py up --eu 1 --us 0 --instances 2     rent the best-ranked cards (datacenter first)
  python cloud/vast_ctl.py up --offer 40114418 --region eu    rent one specific offer
  python cloud/vast_ctl.py list                                our cards: id, region, state, price, IP
  python cloud/vast_ctl.py logs 1234567                        last lines of a card's log
  python cloud/vast_ctl.py down                                destroy ALL our cards (stops billing)
  python cloud/vast_ctl.py down 1234567                        destroy one card

Only touches instances labelled "museum-cloud-*", so nothing else on the account is affected.
"""
import argparse
import json
import os
import shutil
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

from vast_find import EU, US, country, search

# keys can live in cloud/.env next to this script (KEY=value lines), so they stay with the project only
_envfile = os.path.join(os.path.dirname(os.path.abspath(__file__)), ".env")
if os.path.exists(_envfile):
    import re
    # KEY=value pairs, one per line or several on one line separated by spaces
    for _k, _v in re.findall(r'([A-Z_][A-Z0-9_]*)=("[^"]*"|\S+)', open(_envfile, encoding="utf-8-sig").read()):
        os.environ[_k] = _v.strip('"')    # .env wins over stale shell variables

API = "https://console.vast.ai/api/v0"
IMAGE = os.environ.get("CLOUD_IMAGE", "aimbot66/museum-cloud:latest")
LABEL = "museum-cloud"


def call(method, path, body=None):
    """Vast API call. Sends the key both as a Bearer header and as api_key=, like Vast's own CLI,
    and prints Vast's error text instead of a bare traceback."""
    key = os.environ["VAST_API_KEY"]
    sep = "&" if "?" in path else "?"
    url = f"{API}{path}{sep}api_key={urllib.parse.quote(key)}"
    req = urllib.request.Request(url, method=method,
                                 data=json.dumps(body).encode() if body is not None else None,
                                 headers={"Authorization": "Bearer " + key, "Content-Type": "application/json",
                                          "Accept": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=60) as r:
            txt = r.read().decode()
            return json.loads(txt) if txt else {}
    except urllib.error.HTTPError as e:
        detail = e.read().decode(errors="replace")[:400]
        src = os.environ.get("VAST_KEY_SOURCE") or ("cloud/.env" if os.path.exists(_envfile) else "Windows environment")
        hint = "\nRun: vastai tfa login --method-type totp -c <code>  (2FA session expired)" if e.code == 401 else ""
        raise SystemExit(f"Vast API {method} {path} -> HTTP {e.code}: {detail}{hint}\n"
                         f"(key from {src}: {len(key)} chars, ends ...{key[-4:]})")


def vastai(*args, raw=False, answer=None):
    """Run Vast's own CLI (it carries the 2FA session from `vastai tfa login`)."""
    exe = shutil.which("vastai") or os.path.join(sys.prefix, "Scripts", "vastai.exe")
    cmd = [exe, *map(str, args)] + (["--raw"] if raw else [])
    env = {k: v for k, v in os.environ.items() if k != "VAST_API_KEY"}   # else the CLI ignores its 2FA session
    r = subprocess.run(cmd, capture_output=True, text=True, input=answer, env=env)
    out = (r.stdout or "") + (r.stderr or "")
    if r.returncode != 0 or "2FA session" in out or "Two Factor" in out:
        raise SystemExit(out.strip() + "\n\nIf this is the 2FA error: vastai tfa login --method-type totp -c <code>  then retry.")
    if raw:
        try:
            return json.loads(r.stdout)
        except ValueError:
            raise SystemExit(out.strip())
    return out


def rent(offer_id, region, instances):
    env = (f"-p 3478:3478/udp -p 3478:3478/tcp -e CLOUD_KEY={os.environ['CLOUD_KEY']} "
           f"-e CLOUD_REGION={region} -e INSTANCES={instances}")
    out = vastai("create", "instance", offer_id, "--image", IMAGE, "--env", env, "--disk", 20,
                 "--label", f"{LABEL}-{region}")
    if "Failed" in out or "error" in out.lower():
        raise SystemExit(f"could not rent offer {offer_id} ({region}): {out.strip()}")
    print(f"rented offer {offer_id} ({region}): {out.strip()}")


def ours():
    rows = vastai("show", "instances", raw=True)
    rows = rows.get("instances", rows) if isinstance(rows, dict) else rows
    return [i for i in rows if (i.get("label") or "").startswith(LABEL)]


def cmd_up(a):
    if a.offer:
        rent(a.offer, a.region, a.instances)
        return
    offers = search(relaxed=False) + search(relaxed=True)
    seen = set()
    for region, codes, n in (("eu", EU, a.eu), ("us", US, a.us)):
        picked = 0
        for o in offers:
            if picked >= n:
                break
            if o["id"] in seen or country(o) not in codes or o.get("inet_up", 0) < 300:
                continue
            seen.add(o["id"])
            print(f"{region}: {o['gpu_name']} {o.get('geolocation')} ${o['dph_total']:.3f}/h")
            rent(o["id"], region, a.instances)
            picked += 1
        if picked < n:
            print(f"{region}: only found {picked} of {n} suitable offers")


def cmd_list(a):
    rows = ours()
    if not rows:
        print("no museum-cloud cards running")
    total = 0
    for i in rows:
        total += i.get("dph_total") or 0
        print(f"{i['id']:>9}  {i.get('label'):<18} {i.get('actual_status') or i.get('cur_state'):<10} "
              f"{i.get('gpu_name'):<14} ${(i.get('dph_total') or 0):.3f}/h  {i.get('public_ipaddr')}  {i.get('geolocation')}")
    if rows:
        print(f"total ${total:.2f}/h")


def cmd_logs(a):
    print(vastai("logs", a.id, "--tail", 200))


def cmd_down(a):
    ids = [a.id] if a.id else [i["id"] for i in ours()]
    for i in ids:
        print(vastai("destroy", "instance", i, answer="y\n").strip() or f"destroyed {i}")
    if not ids:
        print("nothing to destroy")


def main():
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    up = sub.add_parser("up")
    up.add_argument("--eu", type=int, default=3)
    up.add_argument("--us", type=int, default=3)
    up.add_argument("--instances", type=int, default=6)
    up.add_argument("--offer", type=int)
    up.add_argument("--region", default="eu", choices=["eu", "us"])
    sub.add_parser("list")
    lg = sub.add_parser("logs")
    lg.add_argument("id", type=int)
    dn = sub.add_parser("down")
    dn.add_argument("id", type=int, nargs="?")
    a = ap.parse_args()
    for k in (("VAST_API_KEY", "CLOUD_KEY") if a.cmd == "up" else ()):
        if not os.environ.get(k):
            sys.exit(f"set {k} first")
    {"up": cmd_up, "list": cmd_list, "logs": cmd_logs, "down": cmd_down}[a.cmd](a)


if __name__ == "__main__":
    main()
