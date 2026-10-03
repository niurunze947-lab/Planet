import os, sys
from PIL import Image
import numpy as np

SRC = r"D:\UnityProjects\Planet\_assets_src"

targets = [
    ("Moon/lroc_color_poles_8k.tif",   "Moon colour 8k"),
    ("Moon/lroc_color_poles_2k.tif",   "Moon colour 2k"),
    ("Moon/ldem_16_uint.tif",          "Moon DEM 16ppd"),
    ("Moon/ldem_4_uint.tif",           "Moon DEM 4ppd"),
    ("Earth/land_ocean_ice_2048.png",  "Earth colour 2k"),
    ("Earth/world_topo_bathy_21600x10800.jpg", "Earth topo/bathy 21k"),
]

for rel, label in targets:
    p = os.path.join(SRC, rel.replace("/", os.sep))
    if not os.path.exists(p):
        print(f"{label:28} MISSING")
        continue
    im = Image.open(p)
    line = f"{label:28} {im.format:5} {im.mode:6} {im.size[0]}x{im.size[1]}"
    if im.mode in ("I;16", "I;16B", "I;16L", "I", "F"):
        a = np.array(im)
        line += f"  min={a.min()} max={a.max()} dtype={a.dtype}"
    elif im.mode == "RGB":
        a = np.asarray(im.resize((64, 32)))
        line += f"  RGB mean={a.reshape(-1,3).mean(axis=0).round(1)}"
    print(line)

# Moon DEM sanity: known lunar range is roughly -9.1 km .. +10.8 km vs 1737.4 km datum
p = os.path.join(SRC, "Moon", "ldem_16_uint.tif")
if os.path.exists(p):
    a = np.array(Image.open(p)).astype(np.float64)
    print()
    print("Moon DEM raw stats: min", a.min(), "max", a.max(), "mean", round(a.mean(), 1))
    for scale, off, name in ((0.5, -10000, "half-metre, -10km offset"),
                             (0.5, -20000, "half-metre, -20km offset"),
                             (1.0, -10000, "metre, -10km offset")):
        lo, hi = a.min()*scale+off, a.max()*scale+off
        print(f"  if {name:26}: {lo/1000:8.2f} km .. {hi/1000:8.2f} km")
