"""Derive a URP metallic/smoothness map for Earth from the height map.

URP/Lit reads metallic from the red channel and smoothness from alpha of _MetallicGlossMap.
Sea level is a hard physical boundary in the elevation data (h <= 0 m), so it gives us a free,
exact ocean mask - no hand painting. Oceans become glossy, land stays matte.

The transition is blended over 300 m of depth so coastlines don't alias into a hard edge.
"""
import os
import numpy as np
from PIL import Image

Image.MAX_IMAGE_PIXELS = None
SRC = r"D:\UnityProjects\Planet\Assets\Textures\Planets\Earth\earth_height_2k_16bit.png"
OUT = r"D:\UnityProjects\Planet\Assets\Textures\Planets\Earth\earth_metallic_smoothness.png"

raw = np.array(Image.open(SRC)).astype(np.float32)
h = raw / 65535.0 * 20000.0 - 11000.0            # metres

LAND_SMOOTH, OCEAN_SMOOTH = 0.12, 0.85
t = np.clip((0.0 - h) / 300.0, 0.0, 1.0)          # 0 on land, 1 once 300 m below sea level
t = t * t * (3.0 - 2.0 * t)                       # smoothstep
smooth = LAND_SMOOTH + (OCEAN_SMOOTH - LAND_SMOOTH) * t

H, W = h.shape
rgba = np.zeros((H, W, 4), np.uint8)
rgba[..., 0] = 0                                   # metallic = 0 everywhere (dielectric)
rgba[..., 3] = np.round(smooth * 255.0).astype(np.uint8)

Image.fromarray(rgba, "RGBA").save(OUT, format="PNG", optimize=False)
ocean_pct = float((h <= 0).mean() * 100)
print(f"  {os.path.basename(OUT)}  {W}x{H}  {os.path.getsize(OUT)/1e6:.2f} MB")
print(f"  ocean {ocean_pct:.1f}%  smoothness land={LAND_SMOOTH} ocean={OCEAN_SMOOTH}")
