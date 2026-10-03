"""Derive tangent-space normal maps from the height maps.

The demo mesh has far fewer vertices than the height maps have pixels (a 768-segment sphere
is ~0.47 deg/segment, while the LOLA DEM is 0.0625 deg/pixel - about 7.5x more detail than
the silhouette can carry). A normal map recovers that sub-vertex relief for shading.

Equirectangular spacing is latitude dependent: a pixel spans (2*pi*R/W)*cos(lat) metres
east-west but a constant pi*R/H north-south, so the slope must be scaled by 1/cos(lat) or the
poles come out wildly over-perturbed. cos(lat) is clamped to avoid a divide-by-zero.
"""
import os
import numpy as np
from PIL import Image

Image.MAX_IMAGE_PIXELS = None
DST = r"D:\UnityProjects\Planet\Assets\Textures\Planets"

JOBS = [
    # name,                     height png,                          planet radius m, out size, strength
    ("Earth", "earth_height_16bit.png",       "earth_normal_2k.png",      6371000.0, (2048, 1024), 8.0),
    ("Moon",  "moon_height_16bit.png",        "moon_normal_2k.png",       1737400.0, (2048, 1024), 8.0),
]
# decode: earth h = raw/65535*20000-11000 ; moon h = raw*0.5-10000
DECODE = {"Earth": (20000.0, -11000.0), "Moon": (32767.5, -10000.0)}

for body, src_name, out_name, radius, (OW, OH), strength in JOBS:
    src = os.path.join(DST, body, src_name)
    im = Image.open(src)
    if im.size != (OW, OH):
        im = im.resize((OW, OH), Image.BILINEAR)
    raw = np.array(im).astype(np.float32)
    span, off = DECODE[body]
    h = raw / 65535.0 * span + off          # metres, +north at row 0

    H, W = h.shape
    lat = np.deg2rad(90.0 - (np.arange(H) + 0.5) * (180.0 / H))
    coslat = np.maximum(np.cos(lat), 0.10)          # clamp so poles don't explode

    dx_m = (2.0 * np.pi * radius / W) * coslat      # metres per pixel, east-west
    dy_m = np.pi * radius / H                       # metres per pixel, north-south

    # central differences with longitude wrap
    hp = np.concatenate([h[:, -1:], h, h[:, :1]], axis=1)
    dhdx = (hp[:, 2:] - hp[:, :-2]) / (2.0 * dx_m[:, None])      # +east
    dhdy = -(np.roll(h, -1, axis=0) - np.roll(h, 1, axis=0)) / (2.0 * dy_m)  # +north
    dhdy[0, :] = 0.0; dhdy[-1, :] = 0.0

    n = np.stack([-dhdx * strength, -dhdy * strength, np.ones_like(h)], axis=-1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)

    rgb = np.clip((n * 0.5 + 0.5) * 255.0, 0, 255).astype(np.uint8)
    out = os.path.join(DST, body, out_name)
    Image.fromarray(rgb).save(out, format="PNG", optimize=False)

    # flatness check: an all-(128,128,255) map would mean the source had no relief
    dev = np.abs(n[..., :2]).mean()
    print(f"  {out_name:24} {W}x{H}  {os.path.getsize(out)/1e6:5.2f} MB  "
          f"mean slope dev {dev:.4f}  max |nx|={np.abs(n[...,0]).max():.2f}")

print("\nDONE")
