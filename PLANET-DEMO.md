# Planet — 演示场景说明

> 场景文件：`Assets/Scenes/PlanetDemo.unity`（已加入 Build Settings）
> 打开后按 Play 即可：地球与月球自转，相机缓慢环绕。

---

## 1. 关于"高度图变成红色"

**这是正常的，数据没有损坏。**

高度图导入为 **`R16`（16 位单通道）** 格式，只有红色通道有数值，绿蓝通道恒为 0。Unity 的 Inspector 按 RGB 预览时自然显示为红色。

我从 Unity 内部把纹理数据读出来做了验证，与 Python 写入时**逐位一致**：

| 贴图 | Python 写入 | Unity 读回 | 结论 |
|---|---|---|---|
| `earth_height_2k_16bit` | raw 3708 … 55944 | raw **3708 … 55944** | ✅ 一致 |
| `moon_height_4ppd_16bit` | raw 2243 … 41008 | raw **2243 … 41008** | ✅ 一致 |

Shader 里读 `.r` 通道即可拿到完整的 16 位精度。**不要**改成 RGBA32 来"修好颜色"——那会把 16 位压成 8 位，地球高程量化误差会从 **0.154 m 劣化到约 40 m**。

如果你只是想在 Inspector 里看着舒服，可以在纹理预览窗口左上角把显示模式切到 **Single Channel → Red**，这样就是灰度显示了。

---

## 2. 场景构成

```
PlanetDemo
├── Earth            PlanetMeshGenerator + PlanetSpin   位置 (-1.5, 0, 0)
├── Moon             PlanetMeshGenerator + PlanetSpin   位置 ( 2.3, 0, 0)
├── Sun              Directional Light
├── Focus            (空物体，相机环绕中心)
├── Main Camera      Camera + OrbitCamera + URP CameraData
└── Global Volume    Bloom + ACES Tonemapping + Vignette
```

### 地球

| 参数 | 值 |
|---|---|
| 网格 | 768 × 384 段，**296,065 顶点** |
| 基础半径 | 1.0 场景单位 |
| 真实半径 | 6,371,000 m |
| 垂直夸张 | **25×** |
| 海平面钳制 | 0 m（海底压平为光滑海面） |
| 实测起伏 | **半径的 2.08%** |
| 自转 | 5°/秒，轴倾角 23.4° |

### 月球

| 参数 | 值 |
|---|---|
| 网格 | 512 × 256 段，**131,841 顶点** |
| 基础半径 | 0.2725（= 1737.4 / 6371，**真实半径比**） |
| 真实半径 | 1,737,400 m |
| 垂直夸张 | **8×** |
| 海平面钳制 | 无（保留全部 19.7 km 起伏） |
| 实测起伏 | **半径的 7.13%** |
| 自转 | 2°/秒，轴倾角 6.7° |

> **两颗星的夸张倍数不同是有意的。** 地球真实起伏只占半径 0.31%，月球占 1.13%（3.6 倍）。
> 月球用 8×、地球用 25×，是为了让两者在屏幕上都有可见的立体感——否则地球看起来就是个台球。

### 相机与操作

| 操作 | 效果 |
|---|---|
| 按住左键拖动 | 环绕旋转 |
| 滚轮 | 缩放（1.2 – 40 单位） |
| `Esc` | 停止自动环绕 |

相机 FOV 45°，默认距离 7，俯仰 15°、方位 20°，`autoOrbit` 开启（1.5°/秒缓慢漂移）。

### 光照与后处理

- **太阳**：平行光，强度 1.7，暖白 `(1, 0.97, 0.92)`，软阴影
  方向 `(0.45, −0.30, 0.84)` —— 从相机侧上方打光，使两个天体都呈现明显昼夜分界线
- **环境光**：Flat 模式，暗蓝灰 `(0.08, 0.09, 0.13)`，让背光面不完全死黑
- **反射**：Skybox 模式（128px 探针），地球海洋会反射星空
- **后处理**：Bloom 0.55（阈值 1.0）、ACES Tonemapping、Vignette 0.25
- **抗锯齿**：SMAA High

---

## 3. 星空天空盒

| 项 | 值 |
|---|---|
| 来源 | **NASA SVS《Deep Star Maps 2020》**（ID 4851） |
| 原始文件 | `starmap_2020_4k.exr`（4096 × 2048，34.3 MB） |
| 星表 | Hipparcos-2 + Tycho-2 + **Gaia DR2**，共 **17 亿颗恒星** |
| 投影 | 等距圆柱（Plate Carrée），天球坐标（ICRF/J2000） |
| 格式 | OpenEXR **half-float、线性、HDR** |
| Unity 导入 | `RGBAHalf`，**sRGB 关闭**，Wrap = Clamp |
| 材质 | `Assets/Settings/Skybox_Starfield.mat`，Shader = **`Skybox/Panoramic`** |
| Shader 参数 | Mapping = Latitude Longitude，Image Type = Skybox，Exposure 1.1 |

**为什么必须用 EXR、不能用 PNG**：恒星亮度跨越多个数量级。8 位 sRGB 会把暗星压成纯黑、亮星削顶，银河细节全丢。half-float 保住了动态范围，配合 Bloom 才有正确的星芒。

**为什么 Wrap 设为 Clamp**：等距圆柱贴图在垂直方向不能环绕，否则天极会出现接缝。

> 若需要天文级准确取向：原始星图以 0h 赤经为中心、赤经向左递增。当前作为背景板使用，未做相对黄道的姿态对齐。

---

## 4. 材质

| 材质 | 贴图链 |
|---|---|
| `Earth_Surface` | Base Map `earth_color_8k` + Normal `earth_normal_2k` + Metallic/Smoothness `earth_metallic_smoothness` |
| `Moon_Surface` | Base Map `moon_color_8k` + Normal `moon_normal_2k`，Smoothness 0.08 |
| `Skybox_Starfield` | `starmap_2020_4k.exr` |

**海洋遮罩是白送的**：高程数据里 `h ≤ 0` 就是精确的海陆分界，不需要手绘。
`earth_metallic_smoothness.png` 的 alpha 通道 = 海洋 0.85 / 陆地 0.12（300 m 深度内平滑过渡），
实测海洋占比 **70.5%**，与真实地球的约 71% 吻合。

---

## 5. 一个容易踩的坑：网格为什么用 2K 高程而不是 8K

`PlanetMeshGenerator` 需要 CPU 读取高程图（Read/Write Enabled）。但：

- 网格是 **768 × 384**，即横向 768 个采样点
- 2K 高程图横向有 **2048** 像素

**网格分辨率本来就低于 2K 贴图**，所以用 2K 做几何体不损失任何轮廓精度，却省下约 100 MB 的 CPU 副本（8K R16 光是 CPU 端就要 67 MB）。

8K / 5760×2880 高程图仍然是"主资产"，留给：
- Shader 位移 / 视差
- 更高密度的网格（把 `longitudeSegments` 调大即可）
- 地表近景

---

## 6. 可调参数

在 Inspector 里选中地球或月球，调整 `PlanetMeshGenerator`：

| 参数 | 说明 |
|---|---|
| `verticalExaggeration` | 垂直夸张倍数，最直观的观感旋钮 |
| `floorClampMetres` | 地球设为 −11000 可**打开真实海底地形**（海沟会变成深坑） |
| `longitudeSegments` / `latitudeSegments` | 网格密度，注意顶点数按乘积增长 |
| `heightOffsetMetres` / `heightSpanMetres` | 高度解码参数（**改错会导致地形错乱**） |

各天体的解码参数：

```
地球：metres = (raw / 65535) * 20000  - 11000
月球：metres = (raw / 65535) * 32767.5 - 10000     (= raw * 0.5 - 10000)
```

改完点组件右上角 **⋮ → Generate Mesh** 重新生成。

---

## 7. 截图

`Assets/Screenshots/` 下留了三张验证截图：

| 文件 | 内容 |
|---|---|
| `planet_demo_01.png` | 默认机位全景（地球 + 月球 + 银河） |
| `planet_demo_earth_closeup_4.png` | 地球近景（可见海岸线与地形起伏） |
| `planet_demo_moon_lit.png` | 月球近景（第谷环形山辐射纹、月海） |
