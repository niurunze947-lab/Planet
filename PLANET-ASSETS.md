# Planet — 地球与月球真实资产

> 全部来自 NASA / 公共领域真实探测数据，非手绘、非生成式贴图。
> 采集日期：2026-10-02 ｜ 处理脚本见 `_assets_src/`

---

## 1. 交付清单

| 文件 | 尺寸 | 格式 | 大小 | 用途 |
|---|---|---|---|---|
| `Earth/earth_color_8k.png` | 8192×4096 | RGB8 sRGB | 23.6 MB | 地球基础色（反照率） |
| `Earth/earth_color_2k.png` | 2048×1024 | RGB8 sRGB | 1.9 MB | 同上，轻量版 |
| `Earth/earth_height_16bit.png` | 8192×4096 | **16-bit 灰度** | 40.8 MB | 地球高程（含海底地形） |
| `Earth/earth_height_2k_16bit.png` | 2048×1024 | **16-bit 灰度** | 2.9 MB | 同上，轻量版 |
| `Moon/moon_color_8k.png` | 8192×4096 | RGB8 sRGB | 45.6 MB | 月球基础色 |
| `Moon/moon_color_2k.png` | 2048×1024 | RGB8 sRGB | 3.0 MB | 同上，轻量版 |
| `Moon/moon_height_16bit.png` | 5760×2880 | **16-bit 灰度** | 22.0 MB | 月球高程 |
| `Moon/moon_height_4ppd_16bit.png` | 1440×720 | **16-bit 灰度** | 1.6 MB | 同上，轻量版 |

### 派生贴图（由上述高程图计算得出）

| 文件 | 尺寸 | 说明 |
|---|---|---|
| `Earth/earth_normal_2k.png` | 2048×1024 | 切线空间法线，恢复网格分辨率以下的细节 |
| `Moon/moon_normal_2k.png` | 2048×1024 | 同上。月球平均坡度偏差 0.24，是地球的 5.5 倍 |
| `Earth/earth_metallic_smoothness.png` | 2048×1024 | R=金属度(0)，A=光滑度（海洋 0.85 / 陆地 0.12） |

> 海洋遮罩直接取自高程数据的 `h ≤ 0`，实测海洋占比 **70.5%**，与真实地球约 71% 吻合。

> 演示场景与星空天空盒见 **`PLANET-DEMO.md`**。

全部为**等距圆柱投影（equirectangular / Plate Carrée）**，即标准 UV 球面贴图，经度 −180°…+180° 从左到右，纬度 +90°…−90° 从上到下。可直接用于 Unity 的球体 UV 或自定义 shader 采样，**无需再投影**。

---

## 2. 数据来源

### 月球

**基础色** — NASA SVS *CGI Moon Kit*（ID 4720）
- 原始文件：`lroc_color_poles_8k.tif` / `_2k.tif`
- 数据来源：**LRO 探测器 LROC 广角相机（WAC）**，Hapke 归一化马赛克，由 10 万余张 WAC 影像合成
- 波段：643 nm→红、566 nm→绿、415 nm→蓝，曝光与白平衡调整为接近人眼观感
- 页面：https://svs.gsfc.nasa.gov/4720/

**高程** — 同一 Moon Kit 的 LOLA 数字高程模型
- 原始文件：`ldem_16_uint.tif`（16 ppd）/ `ldem_4_uint.tif`（4 ppd）
- 数据来源：**LRO 探测器 LOLA 激光高度计**
- 参考球半径：1737.4 km；网格 16 像素/度

### 地球

**基础色** — NASA Visible Earth *Blue Marble*
- 原始文件：`land_ocean_ice_8192.png`（8192×4096，无云真彩色）
- 页面：https://visibleearth.nasa.gov/collection/1484/blue-marble
- 说明：该图本身即为 8192×4096，**8K 输出未做任何重采样**，与 NASA 原图逐位一致

**高程** — AWS Open Data *Terrain Tiles*
- 端点：`https://elevation-tiles-prod.s3.amazonaws.com/terrarium/{z}/{x}/{y}.png`
- 本次使用 **zoom 5**（32×32 = 1024 块瓦片，拼合为 8192×8192）
- 数据由多个公共领域高程集聚合而成（SRTM、GMTED2010、NED、ETOPO1 等）
- **原始为 Web Mercator（墨卡托）投影，已重投影为等距圆柱**

---

## 3. 高度图编码（重要）

高度图**不是**归一化的 0–1 数据，而是各自保留官方/约定的线性编码。Unity 以 R16 格式导入后，shader 中 `tex2D().r` 返回 `raw / 65535`，因此解码公式为：

### 月球

```
elevation_metres = raw * 0.5 - 10000
                 = (tex.r * 65535) * 0.5 - 10000
                 = tex.r * 32767.5 - 10000
```

- 即**半米为单位，偏移 20000 个半米单位（10 km）**
- 实测数据范围：raw 2037 … 41371 → **−8.98 km … +10.69 km**（跨度 19.67 km）
- 与 LOLA 公布的月球地形跨度 ~19.9 km（−9.13 km … +10.78 km）吻合；16 ppd 网格会轻微平滑极值，故两端各略窄约 0.1 km，属预期
- 全球平均高程：**−519 m**（相对 1737.4 km 参考球）

### 地球

```
elevation_metres = raw / 65535 * 20000 - 11000
                 = tex.r * 20000 - 11000
```

- 映射区间 **−11000 m … +9000 m**
- 实测数据范围：**−10,591 m … +7,111 m**，**零截断**（无任何像素被 clamp）
- 量化误差最大 **0.154 m**（即 20000/65535 的半步长，理论极限）
- 海底地形为**真实水深**（马里亚纳海沟 −10,591 m），未做海平面抹平

> 若你希望海面平整，可在 shader 中 `h = max(h, 0)`，或做软过渡 `h = max(h, 0) + min(h,0)*0.1`。

---

## 4. Unity 导入设置

已通过 `TextureImporter` API 逐张配置并验证：

| 项 | 基础色图 | 高度图 |
|---|---|---|
| sRGB | ✅ 开启 | ❌ **关闭（线性）** |
| 格式 | BC7（高质量压缩） | **R16（未压缩）** |
| 最大尺寸 | 8192 | 8192 |
| Mipmap | 开启 | 开启 |
| Wrap Mode | Repeat | Repeat |
| NPOT 缩放 | — | **None** |

实测导入结果（`Texture2D.format` / 尺寸）：

```
earth_color_8k.png           8192x4096  BC7  sRGB=True
earth_height_16bit.png       8192x4096  R16  sRGB=False
earth_color_2k.png           2048x1024  BC7  sRGB=True
earth_height_2k_16bit.png    2048x1024  R16  sRGB=False
moon_color_8k.png            8192x4096  BC7  sRGB=True
moon_height_16bit.png        5760x2880  R16  sRGB=False
moon_color_2k.png            2048x1024  BC7  sRGB=True
moon_height_4ppd_16bit.png   1440x720   R16  sRGB=False
```

> ⚠️ **踩过的坑**：Unity 默认 `maxTextureSize` 只有 **2048**，会把 8K 贴图静默降采样；且默认 NPOT 策略会把 5760×2880 的 LOLA DEM 向下取整到 **4096×2048**，损失 29% 横向分辨率。两者均已修正。

---

## 5. 使用建议

### 垂直夸张

真实地形相对星球半径极小：

- 地球：20 km / 6371 km ≈ **0.31%**
- 月球：19.7 km / 1737.4 km ≈ **1.13%**

直接按真实比例位移在视觉上几乎看不出起伏，实际项目中通常做 **10×–100× 垂直夸张**。

### 显存占用

Unity 实测值（`Profiler.GetRuntimeMemorySizeLong`，**含完整 mipmap 链**）：

| 贴图 | 尺寸 | 格式 | 实测显存 |
|---|---|---|---|
| `earth_color_8k` | 8192×4096 | BC7 | 42.7 MB |
| `earth_height_16bit` | 8192×4096 | R16 | **85.3 MB** |
| `moon_color_8k` | 8192×4096 | BC7 | 42.7 MB |
| `moon_height_16bit` | 5760×2880 | R16 | **84.4 MB** |
| `earth_color_2k` | 2048×1024 | BC7 | 2.7 MB |
| `earth_height_2k_16bit` | 2048×1024 | R16 | 5.3 MB |
| `moon_color_2k` | 2048×1024 | BC7 | 2.7 MB |
| `moon_height_4ppd_16bit` | 1440×720 | R16 | 5.3 MB |
| **合计（8 张全载）** | | | **271.0 MB** |

**4 张 8K 就占 255 MB**，其中两张未压缩 R16 高度图是大头。若目标平台受限：

- 只载 2K 四张 = **16 MB**，视觉在远距离几乎无差别
- 或关闭高度图 mipmap（省约 25%，但远处会闪烁）
- 或按距离流式加载（Pro 版 Unity 可用 Addressables + Texture Streaming）

### 分辨率上限（诚实说明）

- **地球高程**：zoom 5 瓦片在赤道约为 **4.9 km/像素**。因此珠穆朗玛峰在此网格上被平滑到约 **7,111 m**，而非真实的 8,849 m——这是 8K 全球贴图的固有极限（单像素覆盖约 5 km）。
- **月球高程**：16 ppd（5760×2880），赤道约 **1.9 km/像素**。
- 若需更高精度，可修改 `_assets_src/download_terrarium.py` 中的 `Z` 值取 zoom 6/7，或接入 SRTM 30 m 区域数据。

---

## 6. 许可与署名

| 数据 | 许可 |
|---|---|
| NASA SVS *CGI Moon Kit*（LROC 色彩 / LOLA 高程） | NASA 影像通常**不受版权保护**，可自由使用 |
| NASA Visible Earth *Blue Marble* | 同上 |
| AWS Open Data *Terrain Tiles* | 项目声明为**公共领域**，聚合自 SRTM / GMTED2010 / NED / ETOPO1 等公共领域数据源 |

NASA 媒体使用指南：https://www.nasa.gov/nasa-brand-center/images-and-media/

**建议署名**（非强制，但符合学术惯例）：

```
Lunar colour: NASA / GSFC / Arizona State University (LROC WAC mosaic)
Lunar topography: NASA / GSFC / MIT (LOLA)
Earth colour: NASA Earth Observatory, Blue Marble
Earth topography: SRTM / GMTED2010 / ETOPO1 via AWS Open Data Terrain Tiles
```

> 用于商业项目前，建议自行复核上述条款的当前版本。

---

## 7. 复现方式

原始下载与处理脚本保留在 `_assets_src/`：

| 脚本 | 作用 |
|---|---|
| `download_terrarium.py` | 下载全球高程瓦片（改 `Z` 可调分辨率） |
| `build_earth_height.py` | 拼合瓦片 → 解码 → 墨卡托转等距圆柱 → 16-bit PNG |
| `convert_moon.py` | LROC TIFF → PNG；LOLA uint16 解码校验 |
| `convert_earth_color.py` | Blue Marble → PNG，并生成 QA 预览图 |
| `inspect_sources.py` | 查看原始文件尺寸/位深/数值范围 |

QA 预览图（色彩/高程对照）位于 `_assets_src/qa/`：`earth_qa.png`、`moon_qa.png`。

所有转换脚本均带**往返校验**：16-bit PNG 写出后会重新读回并逐像素断言与源数据完全一致，确保没有精度损失。

> `_assets_src/` 位于 `Assets/` **之外**，Unity 不会导入它，原始 TIFF（约 115 MB）与瓦片缓存（64 MB）可随时删除。
