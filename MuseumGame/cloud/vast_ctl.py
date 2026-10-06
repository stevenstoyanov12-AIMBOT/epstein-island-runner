#!/usr/bin/env python3
"""Rent, check and destroy the cloud streaming cards from the command line (run on your PC).

  set VAST_API_KEY=...          your Vast API key
  set CLOUD_KEY=...             the same string as the Worker's CLOUD_SECRET (passed to the cards as an env var)

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
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

from vast_find import EU, US, country, search

# keys can live in cloud/.env next to this script (KEY=value lines), so they stay with the project only
_envfile = os.path.join(os.path.dirname(os.path.abspath(__file__)), ".env")
if os.path.exists(_envfile):
    for _line in open(_envfile, encoding="utf-8-sig"):   # -sig: Notepad adds a BOM
        _k, _, _v = _line.strip().partition("=")
        if _k and not _k.startswith("#") and _v:
            os.environ[_k.strip()] = _v.strip().strip('"')    # .env wins over stale shell variables

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
        src = "cloud/.env" if os.path.exists(_envfile) and "VAST_API_KEY" in open(_envfile, encoding="utf-8-sig").read() else "Windows environment"
        raise SystemExit(f"Vast API {method} {path} -> HTTP {e.code}: {detail}\n"
                         f"(key from {src}: {len(key)} chars, ends ...{key[-4:]})")


def rent(offer_id, region, instances):
    env = {
        "CLOUD_KEY": os.environ["CLOUD_KEY"], "CLOUD_REGION": region, "INSTANCES": str(instances),
        "-p 3478:3478/udp": "1", "-p 3478:3478/tcp": "1",
    }
    r = call("PUT", f"/asks/{offer_id}/", {
        "client_id": "me", "image": IMAGE, "env": env, "disk": 20,
        "runtype": "args", "label": f"{LABEL}-{region}",
    })
    print(f"rented offer {offer_id} ({region}):", r.get("new_contract") or r)


def ours():
    rows = call("GET", "/instances/?owner=me").get("instances", [])
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
    r = call("PUT", f"/instances/request_logs/{a.id}/", {"tail": "200"})
    url = r.get("result_url")
    if not url:
        print(r)
        return
    for _ in range(15):          # the log file appears after a few seconds
        try:
            print(urllib.request.urlopen(url, timeout=20).read().decode())
            return
        except Exception:
            time.sleep(2)
    print("log not ready yet, try again")


def cmd_down(a):
    ids = [a.id] if a.id else [i["id"] for i in ours()]
    for i in ids:
        call("DELETE", f"/instances/{i}/")
        print("destroyed", i)
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
    for k in ("VAST_API_KEY",) + (("CLOUD_KEY",) if a.cmd == "up" else ()):
        if not os.environ.get(k):
            sys.exit(f"set {k} first")
    {"up": cmd_up, "list": cmd_list, "logs": cmd_logs, "down": cmd_down}[a.cmd](a)


if __name__ == "__main__":
    main()
