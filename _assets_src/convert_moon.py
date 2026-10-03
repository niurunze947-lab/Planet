"""Convert NASA Moon Kit source TIFFs into Unity-ready PNGs.

Sources (NASA SVS CGI Moon Kit, ID 4720, public domain):
  lroc_color_poles_8k.tif / _2k.tif : LROC WAC Hapke-normalised colour mosaic, 24-bit sRGB RGB
  ldem_16_uint.tif / ldem_4_uint.tif: LOLA DEM, uint16, elevation = raw * 0.5 m - 10000 m

Verified empirically: the uint16 DEM spans 2037..41371 raw == 19.67 km, matching the
published LOLA topographic range of ~19.9 km (-9.13 km .. +10.78 km). The 16 ppd grid
smooths the extremes slightly, exactly as expected. Scale 0.5 m/unit confirmed.

Output preserves the official SVS encoding losslessly (no renormalisation), so the
shader-side decode stays: elevation_metres = (raw / 65535) * 32767.5 - 10000
                                    == raw * 0.5 - 10000
"""
import os
import numpy as np
from PIL import Image

Image.MAX_IMAGE_PIXELS = None  # sources are legitimately huge

SRC = r"D:\UnityProjects\Planet\_assets_src\Moon"
DST = r"D:\UnityProjects\Planet\Assets\Textures\Planets\Moon"
os.makedirs(DST, exist_ok=True)

def save16(arr_u16, path):
    """Save a uint16 array as a genuine 16-bit greyscale PNG, then verify the round-trip."""
    im = Image.fromarray(arr_u16)          # numpy uint16 -> mode 'I;16'
    im.save(path, format="PNG", optimize=False)
    back = np.array(Image.open(path))
    assert back.dtype == np.uint16, f"round-trip dtype {back.dtype}"
    assert back.shape == arr_u16.shape, f"round-trip shape {back.shape}"
    assert np.array_equal(back, arr_u16), "round-trip mismatch - PNG is NOT lossless 16-bit"
    return os.path.getsize(path)

def save8(rgb, path):
    im = Image.fromarray(rgb)
    im.save(path, format="PNG", optimize=False)
    return os.path.getsize(path)

print("=== COLOUR (LROC WAC) ===")
for src_name, out_name in (("lroc_color_poles_8k.tif", "moon_color_8k.png"),
                           ("lroc_color_poles_2k.tif", "moon_color_2k.png")):
    p = os.path.join(SRC, src_name)
    im = Image.open(p).convert("RGB")
    rgb = np.asarray(im)
    out = os.path.join(DST, out_name)
    n = save8(rgb, out)
    print(f"  {out_name:24} {im.size[0]}x{im.size[1]}  {n/1e6:7.2f} MB  "
          f"mean={rgb.reshape(-1,3).mean(axis=0).round(1)}")

print("=== ELEVATION (LOLA DEM) ===")
for src_name, out_name in (("ldem_16_uint.tif", "moon_height_16bit.png"),
                           ("ldem_4_uint.tif",  "moon_height_4ppd_16bit.png")):
    p = os.path.join(SRC, src_name)
    a = np.array(Image.open(p))
    assert a.dtype == np.uint16, f"{src_name} dtype {a.dtype}"
    out = os.path.join(DST, out_name)
    n = save16(a, out)
    lo_m = a.min() * 0.5 - 10000.0
    hi_m = a.max() * 0.5 - 10000.0
    print(f"  {out_name:24} {a.shape[1]}x{a.shape[0]}  {n/1e6:7.2f} MB  "
          f"raw {a.min()}..{a.max()}  == {lo_m/1000:.2f}..{hi_m/1000:.2f} km  "
          f"span {(hi_m-lo_m)/1000:.2f} km  mean {a.mean()*0.5-10000:.0f} m")

print("\nDONE ->", DST)
