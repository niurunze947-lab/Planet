"""Convert Blue Marble Earth colour maps into Unity-ready PNGs.

Sources (NASA Visible Earth, public domain):
  land_ocean_ice_8192.png                  : Blue Marble 2002, cloud-free true colour, 8192x4096
  world_200412_basecolor_21600x10800.jpg   : Blue Marble Next Generation, Dec 2004, 21600x10800
  world_topo_bathy_21600x10800.jpg         : BMNG with shaded relief + bathymetry (decorative)

We emit the cloud-free Blue Marble as the base colour. It is already exactly 8192x4096, so the
8k output needs no resampling at all and is bit-exact to the NASA source.
"""
import os
import numpy as np
from PIL import Image

Image.MAX_IMAGE_PIXELS = None
SRC = r"D:\UnityProjects\Planet\_assets_src\Earth"
DST = r"D:\UnityProjects\Planet\Assets\Textures\Planets\Earth"
QA  = r"D:\UnityProjects\Planet\_assets_src\qa"
os.makedirs(DST, exist_ok=True)
os.makedirs(QA, exist_ok=True)

def report(name, im):
    a = np.asarray(im.convert("RGB"))
    print(f"  {name:24} {im.size[0]}x{im.size[1]}  {os.path.getsize(os.path.join(DST,name))/1e6:6.2f} MB  "
          f"mean={a.reshape(-1,3).mean(axis=0).round(1)}")

print("=== EARTH COLOUR ===")
src = Image.open(os.path.join(SRC, "land_ocean_ice_8192.png")).convert("RGB")
assert src.size == (8192, 4096), src.size

src.save(os.path.join(DST, "earth_color_8k.png"), format="PNG", optimize=False)
report("earth_color_8k.png", src)

src.resize((2048, 1024), Image.LANCZOS).save(os.path.join(DST, "earth_color_2k.png"),
                                             format="PNG", optimize=False)
report("earth_color_2k.png", Image.open(os.path.join(DST, "earth_color_2k.png")))

# QA contact sheet: colour vs height vs the decorative topo/bathy render, for visual check
print("=== QA previews ===")
h = Image.open(os.path.join(DST, "earth_height_16bit.png"))
h8 = np.array(h).astype(np.float32) / 65535.0
h8 = (np.clip(h8, 0, 1) * 255).astype(np.uint8)
h_img = Image.fromarray(h8).convert("RGB").resize((1024, 512), Image.LANCZOS)

topo = Image.open(os.path.join(SRC, "world_topo_bathy_21600x10800.jpg")).convert("RGB").resize((1024, 512), Image.LANCZOS)

sheet = Image.new("RGB", (1024, 512 * 3 + 16), (20, 20, 20))
sheet.paste(src.resize((1024, 512), Image.LANCZOS), (0, 0))
sheet.paste(h_img, (0, 512 + 8))
sheet.paste(topo, (0, 512 * 2 + 16))
sheet.save(os.path.join(QA, "earth_qa.png"))
print("  earth_qa.png  (top: colour / middle: height / bottom: topo-bathy reference)")

mh = Image.open(r"D:\UnityProjects\Planet\Assets\Textures\Planets\Moon\moon_height_16bit.png")
mh8 = (np.array(mh).astype(np.float32) / 65535.0 * 255).astype(np.uint8)
mc = Image.open(r"D:\UnityProjects\Planet\Assets\Textures\Planets\Moon\moon_color_2k.png").convert("RGB")
msheet = Image.new("RGB", (1024, 512 * 2 + 8), (20, 20, 20))
msheet.paste(mc.resize((1024, 512), Image.LANCZOS), (0, 0))
msheet.paste(Image.fromarray(mh8).convert("RGB").resize((1024, 512), Image.LANCZOS), (0, 512 + 8))
msheet.save(os.path.join(QA, "moon_qa.png"))
print("  moon_qa.png   (top: colour / bottom: height)")

print("\nDONE ->", DST)
