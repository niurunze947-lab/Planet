"""Build a global equirectangular Earth heightmap from AWS Open Data terrain tiles.

Input : _assets_src/Earth/terrarium/5/{x}_{y}.png   (32x32 tiles, 256px each -> 8192x8192)
        Web Mercator projection, terrarium encoding:
            elevation_metres = (R * 256 + G + B / 256) - 32768
Output: equirectangular (Plate Carree, lat/lon) 16-bit greyscale PNG.

The tiles are Web Mercator (conformal), which is NOT usable directly as a sphere UV map,
so the mosaic is resampled to an equirectangular grid. Latitude mapping:
    y_norm = (1 - asinh(tan(lat)) / pi) / 2      (inverse Mercator, y in 0..1)
"""
import os
import numpy as np
from PIL import Image

Image.MAX_IMAGE_PIXELS = None

SRC   = r"D:\UnityProjects\Planet\_assets_src\Earth\terrarium\5"
DST   = r"D:\UnityProjects\Planet\Assets\Textures\Planets\Earth"
Z     = 5
N     = 2 ** Z
TILE  = 256
MOS   = N * TILE                     # 8192
os.makedirs(DST, exist_ok=True)

# ---------- 1. mosaic + decode ----------
print(f"mosaicking {N}x{N} tiles -> {MOS}x{MOS} ...", flush=True)
red   = np.zeros((MOS, MOS), np.float32)
green = np.zeros((MOS, MOS), np.float32)
blue  = np.zeros((MOS, MOS), np.float32)

for y in range(N):
    for x in range(N):
        p = os.path.join(SRC, f"{x}_{y}.png")
        a = np.asarray(Image.open(p).convert("RGB"), dtype=np.float32)
        r0, c0 = y * TILE, x * TILE
        red  [r0:r0+TILE, c0:c0+TILE] = a[:, :, 0]
        green[r0:r0+TILE, c0:c0+TILE] = a[:, :, 1]
        blue [r0:r0+TILE, c0:c0+TILE] = a[:, :, 2]
    if (y + 1) % 8 == 0:
        print(f"  row {y+1}/{N}", flush=True)

elev = (red * 256.0 + green + blue / 256.0) - 32768.0
del red, green, blue

print(f"mercator elevation: min={elev.min():.0f} max={elev.max():.0f} mean={elev.mean():.1f}")
ocean = (elev <= 0).mean() * 100
print(f"  <=0 m (ocean/void): {ocean:.1f}%   land max {elev.max():.0f} m")

# ---------- 2. reproject Mercator -> equirectangular ----------
def mercator_rows(out_h):
    """Return source row index (float) in the mosaic for each output row."""
    lat = 90.0 - (np.arange(out_h) + 0.5) * (180.0 / out_h)      # degrees, +90 .. -90
    lat = np.clip(lat, -85.05112878, 85.05112878)                # tile coverage limit
    lat_rad = np.radians(lat)
    y_norm = (1.0 - np.arcsinh(np.tan(lat_rad)) / np.pi) / 2.0   # 0..1 top->bottom
    return np.clip(y_norm * MOS - 0.5, 0, MOS - 1)

def resample_rows(src, src_rows):
    i0 = np.floor(src_rows).astype(np.int64)
    i1 = np.minimum(i0 + 1, src.shape[0] - 1)
    w = (src_rows - i0).astype(np.float32)[:, None]
    return src[i0] * (1.0 - w) + src[i1] * w

def resample_cols(src, src_cols):
    j0 = np.floor(src_cols).astype(np.int64)
    j1 = np.minimum(j0 + 1, src.shape[1] - 1)
    w = (src_cols - j0).astype(np.float32)[None, :]
    return src[:, j0] * (1.0 - w) + src[:, j1] * w

def to_equirect(out_w, out_h):
    rows = mercator_rows(out_h)
    tmp = resample_rows(elev, rows)                      # (out_h, MOS)
    cols = np.clip((np.arange(out_w) + 0.5) / out_w * MOS - 0.5, 0, MOS - 1)
    return resample_cols(tmp, cols)                      # (out_h, out_w)

# ---------- 3. encode ----------
# Documented linear mapping. The AWS tiles carry genuine bathymetry (Challenger Deep is
# -10,911 m), so the range must span the whole planet or the sea floor gets destroyed.
# Decode in a shader is simply:  elevation_metres = (raw / 65535) * 20000 - 11000
H_MIN, H_MAX = -11000.0, 9000.0
SPAN = H_MAX - H_MIN

def encode(eq, name):
    clipped = np.clip(eq, H_MIN, H_MAX)
    n_below = int((eq < H_MIN).sum()); n_above = int((eq > H_MAX).sum())
    raw = np.round((clipped - H_MIN) / SPAN * 65535.0).astype(np.uint16)
    out = os.path.join(DST, name)
    Image.fromarray(raw).save(out, format="PNG", optimize=False)
    back = np.array(Image.open(out))
    assert np.array_equal(back, raw), "16-bit round-trip failed"
    dec = back.astype(np.float64) / 65535.0 * SPAN + H_MIN
    err = np.abs(dec - clipped)
    print(f"  {name:28} {raw.shape[1]}x{raw.shape[0]}  {os.path.getsize(out)/1e6:6.2f} MB  "
          f"quant err max {err.max():.3f} m  clamped {n_below+n_above} px")
    return raw
print("reprojecting to equirectangular ...", flush=True)
eq8k = to_equirect(8192, 4096)
print(f"  equirect 8192x4096: min={eq8k.min():.0f} max={eq8k.max():.0f}")
encode(eq8k, "earth_height_16bit.png")

# light version straight from the 8k grid (proper area-ish downsample)
small = np.asarray(Image.fromarray(eq8k.astype(np.float32)).resize((2048, 1024), Image.BILINEAR))
encode(small, "earth_height_2k_16bit.png")

np.save(os.path.join(DST, "_earth_elev_8192x4096.npy"), eq8k)
print("\nDONE ->", DST)
