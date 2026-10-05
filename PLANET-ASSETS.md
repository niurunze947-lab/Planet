# Planet — 地球与月球真实资产

> §1–7 的地月与星图资产来自 NASA / 公共领域真实探测数据；§8 的程序化资源由工程自行生成。
> 采集日期：2026-10-02 ｜ 处理脚本见 `_assets_src/`
>
> 更新：2026-10-04。§4 记录 HDR 星空导入与经度接缝修复，§8 记录 SolarSystem 的程序化
> 天体派生资源。真实 NASA 资产与程序化资源使用不同的数据来源。

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
| `Sky/starmap_2020_4k.exr` | 4096×2048 | **OpenEXR half-float** | 34.3 MB | 星空天空盒 |

### 派生贴图（由上述高程图计算得出）

| 文件 | 尺寸 | 说明 |
|---|---|---|
| `Earth/earth_normal_2k.png` | 2048×1024 | 切线空间法线，恢复网格分辨率以下的细节 |
| `Moon/moon_normal_2k.png` | 2048×1024 | 同上。月球平均坡度偏差 0.24，是地球的 5.5 倍 |
| `Earth/earth_metallic_smoothness.png` | 2048×1024 | R=金属度(0)，A=光滑度（海洋 0.85 / 陆地 0.12） |

> 海洋遮罩直接取自高程数据的 `h ≤ 0`，实测海洋占比 **70.5%**，与真实地球约 71% 吻合。

> 演示场景与星空天空盒见 **`PLANET-DEMO.md`**。真实比例太阳系与程序化 Earth 场景
> 见 **`PLANET-SOLARSYSTEM.md`** 与本文 §8。

§1 清单中的真实数据贴图为**等距圆柱投影（equirectangular / Plate Carrée）**，即标准 UV 球面贴图，经度 −180°…+180° 从左到右，纬度 +90°…−90° 从上到下。可直接用于 Unity 的球体 UV 或自定义 shader 采样，**无需再投影**。

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

### 星空（天空盒）

**星图** — NASA SVS *Deep Star Maps 2020*（ID 4851）
- 原始文件：`starmap_2020_4k.exr`（4096×2048）
- 星表：Hipparcos-2 + Tycho-2 + **Gaia DR2**，共 **17 亿颗恒星**
- 投影：等距圆柱（Plate Carrée），天球坐标（ICRF/J2000）
- 页面：https://svs.gsfc.nasa.gov/4851/
- **这是工程里唯一一张 HDR 贴图**，`RGBAHalf`、sRGB 关闭、线性

> 该图本身是**完整全天球**的等距圆柱投影，`-180°…+180°` 经度 × `-90°…+90°` 纬度全覆盖。
> 因此天空盒出现"只有一半有星星"时，问题一定出在采样端（材质参数或 shader），
> **不要**去重新下载或裁剪贴图。

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

### 4.1 高度图为什么必须保持 R16

别为了"颜色好看"或"统一格式"把高程图改成 RGBA32。那会把 16 位压成 8 位，
地球高程的量化误差从 **0.154 m 劣化到约 40 m**——地形会变成阶梯状。
R16 在 Inspector 里预览显示为**红色**是正常的：只有 R 通道有数据，Unity 用红色通道做预览着色。

### 4.2 星空 EXR 的逐轴 Wrap（全包围修复的一部分）

`starmap_2020_4k.exr` 的导入设置**不能只设一个 `wrapMode` 字段**，必须逐轴设置：

| 轴 | 值 | 原因 |
|---|---|---|
| **U**（经度） | `Repeat` | 经度 −180°/ +180° 是同一条子午线，必须环绕，否则接缝处出现一条黑线 |
| **V**（纬度） | `Clamp` | 纬度到极点就结束了，**环绕会让天极出现接缝** |
| **W** | `Clamp` | 未使用，保持一致 |

```csharp
var ti = (TextureImporter)AssetImporter.GetAtPath(path);
ti.wrapModeU = TextureWrapMode.Repeat;
ti.wrapModeV = TextureWrapMode.Clamp;
ti.wrapModeW = TextureWrapMode.Clamp;
ti.SaveAndReimport();
```

> ⚠️ **踩过的坑**：老的 `ti.wrapMode`（单值）字段在重新导入时会被 Unity 覆盖回默认值，
> 改了看起来"没生效"。**逐轴设置才是可靠的写法**，而且必须调 `SaveAndReimport()` 才会落盘。

实测确认（`Assets/Textures/Sky/starmap_2020_4k.exr`）：

```
wrapMode = Repeat        (U/V/W) = Repeat/Clamp/Clamp
maxTextureSize = 4096    sRGB = False    mipmap = True
textureType = Default    alphaSource = None
```

这一设置与天空盒用的是内置 `Skybox/Panoramic` 还是工程自有的 `Planet/StarfieldPhysical`
无关——两者采样的是同一张贴图，**U 环绕 / V 钳制的组合都必须保持**。

### 4.3 经度接缝的 mip 采样修复

仅设置 U Repeat 无法解决导数跳变：经度在 `atan2` 的 ±π 处跳过一个整周期，普通 `tex2D`
隐式求导会把这一差值当成一个巨大纹理足迹，选择最模糊的 mip，形成竖向暗线。当前 Shader：

1. 在 `frac` 包装前计算 UV 的屏幕导数；
2. 对经度导数使用 `delta -= round(delta)`，取最短周期差；
3. 使用 `tex2Dgrad` 和校正后的导数采样，纬度钳制到边缘纹素中心。

没有对星图进行接缝模糊带处理，也没有修改 NASA 源 EXR。验证相机正对经度接缝：
`Assets/Screenshots/skybox_seam_fixed.png`。

天空整体曝光由 `PlanetLightingController` 的可见表面测光近似驱动，全局 EV 不写入材质资产。
`_DayFadePower`、`_DayIntensity` 和 `_HorizonGlow` 的半天空方案已由此替换。

---

## 5. 使用建议

### 垂直夸张

真实地形相对星球半径极小：

- 地球：20 km / 6371 km ≈ **0.31%**
- 月球：19.7 km / 1737.4 km ≈ **1.13%**

直接按真实比例位移在视觉上几乎看不出起伏，实际项目中通常做 **10×–100× 垂直夸张**。

### 显存占用

按格式几何计算（**含完整 mipmap 链**，即 ×4/3）。这是真实的 GPU 占用，也是工程里
**唯一**一份逐张显存表——`PROJECT-OVERVIEW.md` 与 §9 只给合计，不复述本表。

| 贴图 | 尺寸 | 格式 | sRGB | GPU |
|---|---|---|---|---|
| `earth_color_8k` | 8192×4096 | BC7 | ✔ | 42.7 MB |
| `earth_height_16bit` | 8192×4096 | R16 | ✘ | **85.3 MB** |
| `moon_color_8k` | 8192×4096 | BC7 | ✔ | 42.7 MB |
| `moon_height_16bit` | 5760×2880 | R16 | ✘ | **42.2 MB** |
| `earth_color_2k` | 2048×1024 | BC7 | ✔ | 2.7 MB |
| `earth_height_2k_16bit` | 2048×1024 | R16 | ✘ | 5.3 MB |
| `moon_color_2k` | 2048×1024 | BC7 | ✔ | 2.7 MB |
| `moon_height_4ppd_16bit` | 1440×720 | R16 | ✘ | 2.6 MB |
| `earth_normal_2k` | 2048×1024 | DXT5 | ✘ | 2.7 MB |
| `moon_normal_2k` | 2048×1024 | DXT5 | ✘ | 2.7 MB |
| `earth_metallic_smoothness` | 2048×1024 | DXT5 | ✘ | 2.7 MB |
| `starmap_2020_4k` | 4096×2048 | RGBAHalf | ✘ | 85.3 MB |
| **合计（12 张全载）** | | | | **319.5 MB** |

> ⚠️ **注意 API 口径**：`Profiler.GetRuntimeMemorySizeLong` 对这 12 张贴图报告 **639.0 MB**，
> 恰好是上表的 **1.91 倍** —— 该 API 把 CPU 侧镜像也算进去了。
> 做显存预算时用**上表的 319.5 MB**，不要用 Profiler 的数字。

> ⚠️ **导入设置的两个静默降级**（详见 §4）：Unity 默认 `maxTextureSize = 2048` 会把 8K 静默压成 2K；
> 默认 NPOT 策略会把月球的 5760×2880 向下取整到 4096×2048（损失 29% 横向分辨率）。
> 所有 8K 贴图都已显式提到 8192，两张 NPOT 高度图已设 `npotScale = None`。

**两张 8K 未压缩 R16 高度图 + 星空 EXR 是三大头**（合计 213 MB）。若目标平台受限：

- 只载 2K 版本 = **约 16 MB**，视觉在远距离几乎无差别
- 或关闭高度图 mipmap（省约 25%，但远处会闪烁）
- 或按距离流式加载（Addressables + Texture Streaming）
- 星空图若换成 2K EXR，可从 85 MB 降到 21 MB

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

---

## 8. 太阳系场景：全部天体的程序化资源

`Assets/Scenes/SolarSystem.unity`（真实比例太阳系，见 `PLANET-SOLARSYSTEM.md`）
使用同一张 NASA HDR 星空；全部天体来自种子采样后烘焙的独立 Cubemap，Earth 另有独立云密度。
程序化 Earth 不使用本文件中的 NASA 地球色彩 / DEM 来生成大陆，因此示例地貌不对应真实地球。

| 项 | `PlanetDemo` | `SolarSystem` |
|---|---|---|
| 表面贴图 | 真实色彩、高程、法线与材质图 | 21 份独立 Albedo Cubemap、Earth CloudDensity 和五份径向环 profile |
| 星球外观 | 8K 反照率 + 法线 + 金属度/光滑度 | 定义驱动的岩壳、冰壳、气层、雾霾、光球及环 |
| 几何来源 | 16 位高程图逐顶点位移 | 全部天体六面 Cube Sphere，环使用 `SolarMeshFactory` |
| 天空盒 | `starmap_2020_4k.exr`（共用） | 同左（**唯一共用的贴图**） |

### 白模材质（`Assets/Resources/Solar/`）

太阳系场景保留 5 个白模 / 轨道材质：

| 材质 | 着色器 | 用途 |
|---|---|---|
| `WhiteClay.mat` | `Planet/ClayGrid` | 缺少程序化定义时的回退表面 |
| `WhiteRing.mat` | `Planet/ClayGrid`（`_GridStrength = 0`） | 缺少独立环材质时的回退 |
| `WhiteSun.mat` | URP/Lit（自发光） | 缺少太阳定义时的回退 |
| `BeltMarker.mat` | URP/Unlit（`enableInstancing`） | 碎带标记点 |
| `OrbitLine.mat` | `Planet/OrbitLine` | 轨道线 |

这些回退资源保留白模基线；正式场景已使用独立程序化材质。
21 份 256²×6 RGBA32 Albedo 含 mip 共约 42 MiB GPU 缓存；Earth 云密度约 0.5 MiB，
五份环 profile 约 40 KiB。当前仍保留纹理 CPU 镜像，因此 Unity 总运行内存口径会更高。

### Earth 的程序化派生资源

| 资源（`Assets/Resources/Solar/`） | 内容 |
|---|---|
| `ProceduralTerra.asset` | 权威物理 / 地形 / 气候参数、种子 271828、可选特性与地形统计 |
| `ProceduralTerra_Albedo.cubemap` | 256²×6、线性 RGBA32、mipmap；约 2 MiB GPU 缓存 |
| `ProceduralTerra_CloudDensity.cubemap` | 128²×6、线性 RGBA32、mipmap；约 0.5 MiB GPU 缓存 |
| `ProceduralTerraSurface.mat` | 程序化表面与确定性云影 |
| `ProceduralTerraClouds.mat` | 云壳密度 / 视角光程近似 |

`Assets/Settings/Atmosphere_Earth.mat` 是大气材质；壳层 Mesh 在内存生成，共享高分辨率球体，
按米制大气高度和场景半径缩放。纹理全部是派生缓存，权威地形仍是 `PlanetSurfaceSampler`。

海平面由粗采样地形范围与 `seaLevelFraction` 推导；`waterCoverage` 是粗 Cube Sphere 网格上的
采样占比，不能当成精确全球面积占比或总液体体积（当前示例约 56.4%）。

构建菜单 **Build Procedural Planet Example** 按面并行烘焙并逐资产保存；只调壳层或光照时，
可用 **Refresh Procedural Planet Visuals** 复用已生成缓存。派生指纹写入 `.meta` 的 importer
`userData`，命中即跳过烘焙；替换采用 `CopySerialized` 保留 GUID。本机实测完整重建 11.48 秒、
缓存命中 0.93 秒。

> 采样函数、参数含义、网格与壳层的实现细节见 **[PLANET-PROCEDURAL.md](PLANET-PROCEDURAL.md)**，
> 本节只负责资产清单。当前云与大气是星球尺度近似；地形遮挡自阴影和完整体积光线步进尚未加入。

### 其他 20 个天体的派生资源

资源位于 `Assets/Resources/Solar/BodyMaterials/`，每个天体有 `<Name>.asset`、`<Name>_Albedo.cubemap`
与 `<Name>Surface.mat`；有大气／环者另有独立材质，五组环保存 `<Name>_RingProfile.asset`。
它们由工程自行生成，没有新增远程影像下载或第三方图像授权。
外观与天文特征的具体依据、示意性质和未实现项见 [PLANET-MATERIALS.md](PLANET-MATERIALS.md)。
**Build Solar System Materials** 参数化独立路径并保留现有 GUID；Earth 延续原路径。

### 经纬参考网

白模回退用 UV 经纬网；正式程序化表面按单位方向的角度生成经纬参考网，使用 `fwidth` 抗锯齿。
它不占贴图显存。默认关闭；按 `L` 切换全局 `_PlanetGridStrength`，方便观察转轴与自转。

### 碎带标记点：位置真实，尺寸不真实

主带 2200 个 + 柯伊伯带 5000 个标记点，**位置严格按真实轨道根数**（a ∈ [2.06, 3.27] AU 与
[30.00, 50.00] AU）。但 100 km 的小行星在真实比例下只有 `1×10⁻⁴` 个场景单位，永远不可能被看见，
所以标记点按**相对相机的固定张角**缩放（默认 0.0012 rad ≈ 900p 下 2.5 像素），并带 ±55%
的随机尺寸抖动。

> 引用这个场景的数据时请说明这一点：**碎带的位置是真实的，标记点的大小不是。**

---

## 9. 显存预算小结（按场景）

逐张贴图的格式与占用在 **§5「显存占用」**，本节只回答"哪个场景总共吃多少"：

| 场景 | 贴图显存 | 说明 |
|---|---|---|
| `PlanetDemo` | **319.5 MB** | 12 张全载（§5 表） |
| `SolarSystem` | **≈ 87.8 MiB** | 星空 EXR + Earth Albedo / CloudDensity 缓存，未含 Mesh 与 CPU 镜像 |

两个场景共用 `starmap_2020_4k`，所以同时加载两者时不是简单相加；程序化 Earth 缓存另加约 2.5 MiB。
若内存吃紧，**优先降星空图**：换成 2K EXR 可从 85.3 MB 降到约 21 MB，而它对两个场景都是背景，
视觉损失最小。
