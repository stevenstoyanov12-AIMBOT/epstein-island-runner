#!/usr/bin/env python3
"""List Vast.ai offers that fit the cloud streaming plan (run on your PC, not on a card).

  set VAST_API_KEY=...            (Windows)   /  export VAST_API_KEY=...
  python cloud/vast_find.py              strict: datacenter (secure cloud), verified, reliability >= 0.99,
                                         direct ports, static IP, RTX 4090 / 5090 / PRO 5000
  python cloud/vast_find.py --relaxed    same without the datacenter requirement

Prints EU and US offers, cheapest first. If a region has none even relaxed, that region needs TURN after all.
"""
import json
import os
import sys
import urllib.parse
import urllib.request

GPUS = ["RTX 4090", "RTX 5090", "RTX PRO 5000", "RTX PRO 5000 Blackwell"]
EU = {"AT", "BE", "BG", "CH", "CZ", "DE", "DK", "EE", "ES", "FI", "FR", "GB", "UK", "GR", "HR", "HU", "IE",
      "IS", "IT", "LT", "LU", "LV", "NL", "NO", "PL", "PT", "RO", "RS", "SE", "SI", "SK", "UA"}
US = {"US", "CA"}


def search(relaxed):
    q = {
        "verified": {"eq": True}, "rentable": {"eq": True}, "rented": {"eq": False},
        "reliability2": {"gte": 0.99}, "direct_port_count": {"gt": 2}, "static_ip": {"eq": True},
        "gpu_name": {"in": GPUS}, "num_gpus": {"eq": 1}, "type": "on-demand", "limit": 500,
        "order": [["dph_total", "asc"]],
    }
    if not relaxed:
        q["hosting_type"] = {"eq": 1}        # 1 = datacenter (Secure Cloud)
    url = "https://console.vast.ai/api/v0/bundles/?q=" + urllib.parse.quote(json.dumps(q))
    req = urllib.request.Request(url, headers={"Authorization": "Bearer " + os.environ.get("VAST_API_KEY", "")})
    return json.load(urllib.request.urlopen(req, timeout=30)).get("offers", [])


def country(o):
    geo = (o.get("geolocation") or "").split(",")[-1].strip().upper()
    return geo


def main():
    relaxed = "--relaxed" in sys.argv
    offers = search(relaxed)
    for name, codes in (("EU", EU), ("US", US)):
        rows = [o for o in offers if country(o) in codes]
        print(f"\n{name}: {len(rows)} offers ({'relaxed' if relaxed else 'datacenter'})")
        for o in rows[:12]:
            print(f"  id {o['id']:>9}  {o['gpu_name']:<14} ${o['dph_total']:.3f}/h  rel {o['reliability2']:.3f}"
                  f"  ports {o.get('direct_port_count')}  {o.get('geolocation')}  up {o.get('inet_up', 0):.0f} Mbps"
                  f"  {'DC' if o.get('hosting_type') == 1 else 'host'}")
        if not rows:
            print("  none" + ("" if relaxed else " - try --relaxed") + (" - this region needs TURN" if relaxed else ""))


if __name__ == "__main__":
    main()
