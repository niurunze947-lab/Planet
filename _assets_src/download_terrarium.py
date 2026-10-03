"""Download AWS Open Data global terrain tiles (terrarium encoding) at zoom 5.

Source: https://elevation-tiles-prod.s3.amazonaws.com/terrarium/{z}/{x}/{y}.png
These tiles aggregate public-domain elevation data (SRTM, GMTED, NED, ETOPO1...).
Terrarium encoding: elevation_metres = (R * 256 + G + B / 256) - 32768
"""
import os, sys, time
from concurrent.futures import ThreadPoolExecutor, as_completed
from urllib.request import urlopen, Request

Z = 5
N = 2 ** Z  # 32 -> 1024 tiles, mosaic 8192x8192 (Web Mercator)
OUT = os.path.join(r"D:\UnityProjects\Planet\_assets_src\Earth", "terrarium", str(Z))
os.makedirs(OUT, exist_ok=True)

BASE = "https://elevation-tiles-prod.s3.amazonaws.com/terrarium"

def fetch(xy):
    x, y = xy
    dst = os.path.join(OUT, f"{x}_{y}.png")
    if os.path.exists(dst) and os.path.getsize(dst) > 0:
        return (x, y, "cached", os.path.getsize(dst))
    url = f"{BASE}/{Z}/{x}/{y}.png"
    for attempt in range(3):
        try:
            req = Request(url, headers={"User-Agent": "Mozilla/5.0"})
            with urlopen(req, timeout=30) as r:
                data = r.read()
            if len(data) < 100:
                raise ValueError(f"suspiciously small: {len(data)}")
            with open(dst, "wb") as f:
                f.write(data)
            return (x, y, "ok", len(data))
        except Exception as e:
            if attempt == 2:
                return (x, y, f"FAIL {e}", 0)
            time.sleep(1 + attempt)
    return (x, y, "FAIL", 0)

targets = [(x, y) for y in range(N) for x in range(N)]
print(f"zoom {Z}: {len(targets)} tiles -> mosaic {N*256}x{N*256}", flush=True)

done = ok = fail = cached = 0
total_bytes = 0
t0 = time.time()
with ThreadPoolExecutor(max_workers=16) as ex:
    futs = [ex.submit(fetch, t) for t in targets]
    for f in as_completed(futs):
        x, y, st, sz = f.result()
        done += 1
        total_bytes += sz
        if st == "ok": ok += 1
        elif st == "cached": cached += 1
        else: fail += 1
        if done % 128 == 0 or done == len(targets):
            el = time.time() - t0
            print(f"  {done}/{len(targets)}  ok={ok} cached={cached} fail={fail}  "
                  f"{total_bytes/1e6:.1f} MB  {el:.0f}s", flush=True)

print(f"DONE ok={ok} cached={cached} fail={fail} bytes={total_bytes}", flush=True)
missing = [f"{x}_{y}" for x, y in targets if not os.path.exists(os.path.join(OUT, f"{x}_{y}.png"))]
if missing:
    print("MISSING:", missing[:20], "...total", len(missing), flush=True)
