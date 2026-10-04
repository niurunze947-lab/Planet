# Planet — 项目概览

> 更新：2026-10-04 ｜ 数据来源：Unity Editor 实时查询、工程文件与运行时验证。
> 本版包含真实比例太阳系与 Earth 的程序化星球尺度示例：场景与轨道见
> [PLANET-SOLARSYSTEM.md](PLANET-SOLARSYSTEM.md)，程序化星球表面生成框架见
> [PLANET-PROCEDURAL.md](PLANET-PROCEDURAL.md)，贴图来源见 [PLANET-ASSETS.md](PLANET-ASSETS.md)。

---

## 1. 这是什么

一个 **Unity 2022.3 LTS + URP** 的行星演示工程，现在有两条并行的内容线，外加一层共用的程序化框架：

**A. 贴图版地球与月球**（`PlanetDemo.unity`，**仅作参考**）

- 定位：这两颗星球的用途是**星系视角下星球渲染精度的参考基准**，不是工程主线。
  工程实际的承载场景是 `SolarSystem.unity`；新增功能、比例与验证都以它为准。
- 地表颜色来自 NASA 真实影像，高程来自真实地形测量数据（含海底地形）
- 星球**不是贴图球**，几何体由 16 位高程图逐顶点径向位移生成
- 背景是 NASA 基于 **Gaia DR2（17 亿颗恒星）** 绘制的 HDR 星空

**B. 真实比例的太阳系**（`SolarSystem.unity`）← **正式工程文件**

- 从**太阳**到**柯伊伯带**，比例不做任何压缩（海王星就在真实的 30.07 AU）
- 21 个天体 + 主带 2200 粒子 + 柯伊伯带 5000 粒子，轨道根数取自 JPL/Standish 与 Meeus
- **自转与公转全部由代码按开普勒方程推算**，不是动画、不是手 K
- Earth 接入统一程序化地形、海洋、生态色彩、云层和大气，其余天体沿用白模
- 天空盒保持完整 HDR 星图；可见表面的测光驱动星空整体曝光，经度采样采用周期导数处理接缝

**C. 程序化星球表面生成框架**（服务于 B 的实现层，见 [PLANET-PROCEDURAL.md](PLANET-PROCEDURAL.md)）

- 权威链 `PlanetDefinition → PlanetSurfaceSampler → CubeSphere → PlanetSurfaceView`：
  定义是唯一真相，网格与 Cubemap 都是可重建的派生缓存
- 一套米制采样函数同时供几何、颜色、云密度、星空测光使用，因此贴图与几何不可能对不上
- 目前**只有 Earth 接入**，其余 20 个天体仍是白模；框架本身与星球半径、场景尺度无关

---

## 2. 基本信息

| 项 | 值 |
|---|---|
| 项目名 | `Planet` |
| 路径 | `D:\UnityProjects\Planet` |
| 公司名 / 版本 | `DefaultCompany` / `0.1.0` |
| Unity | **2022.3.62f3**（LTS，revision `96770f904ca7`） |
| 渲染管线 | **Universal (URP) 14.0.12** |
| 色彩空间 | **Linear** |
| 输入系统 | 新输入系统（`activeInputHandler = 1`，**仅**新系统） |
| 序列化模式 | **Force Text**（所有资产 YAML 文本，可 diff） |
| Meta 文件 | Visible Meta Files |
| 默认分辨率 | 1024 × 768（窗口模式） |
| 场景数 | **3**（`PlanetDemo`、`SampleScene`、`SolarSystem`） |
| Run In Background | **开启**（编辑器默认关闭，见 §12.7 —— 这是本工程最容易踩的坑） |
| 版本控制 | ✅ **git + Git LFS**，分支 `main` |

---

## 3. 目录结构

```
D:\UnityProjects\Planet\
├── Assets/                     281.6 MB   ← 入库
│   ├── Scenes/                 3 个场景（含 SolarSystem.unity，707 KB）+ 1 个 Volume Profile
│   ├── Scripts/                Solar/ 轨道系统、Procedural/ 星球框架、相机与天空控制
│   ├── Editor/                 菜单 Tools ▸ Planet：构建、视觉刷新、截图
│   ├── Shaders/                行星表面、大气、云、星空、白模和轨道
│   ├── Settings/               URP 配置、3 个材质、后处理 Profile
│   ├── Resources/Solar/        白模材质、ProceduralTerra 定义与派生 Cubemap
│   ├── Textures/               182.9 MB，12 张贴图
│   ├── Screenshots/            93.6 MB，46 张验证截图（其中 solar_* 15 张）
│   └── TutorialInfo/           模板自带（可删）
├── Packages/                   3.4 MB     ← 入库
├── ProjectSettings/            0.1 MB     ← 入库
├── MCP-Setup/                  握手报告与工具清单 ← 入库
├── _assets_src/                384.2 MB   ← 仅 .py 与 qa/ 入库
├── _mcp_setup_backup_20261002/ 配置备份    ← 入库
├── .git/                       225.7 MB（含 LFS 对象池）
├── DEVLOG.md                   开发日志（日期线，细节以各文档为准）
├── PLANET-ASSETS.md            资产来源与授权
├── PLANET-DEMO.md              贴图版地球/月球参考场景说明
├── PLANET-PROCEDURAL.md        程序化星球表面生成框架（实现、参数、限制）
├── PLANET-SOLARSYSTEM.md       真实比例太阳系场景说明（正式工程场景）
├── PROJECT-OVERVIEW.md         本文档
└── .gitignore / .gitattributes
```

此目录树中的磁盘大小与旧截图数量沿用 2026-10-03 基线快照，新增代码与资源见 §8.5；
`Library/` 与 `Logs/` 已被 `.gitignore` 排除。

---

## 4. 技术栈与依赖

`Packages/manifest.json` 共 **12 个显式包 + 31 个内置模块**：

| 包 | 版本 | 用途 |
|---|---|---|
| `com.unity.render-pipelines.universal` | 14.0.12 | 主渲染管线 |
| `com.unity.inputsystem` | **1.19.0** | 新输入系统（**仅**此系统） |
| `com.unity.burst` | 1.8.21 | 高性能数学编译 |
| `com.unity.textmeshpro` | 3.0.7 | 文本 |
| `com.unity.timeline` | 1.8.12 | 时间轴 |
| `com.unity.visualscripting` | 1.9.11 | 可视化脚本 |
| `com.unity.ugui` | 1.0.0 | UI |
| `com.unity.test-framework` | 1.1.33 | 测试 |
| `com.unity.ide.rider` | 3.0.40 | IDE |
| `com.unity.ide.visualstudio` | 2.0.26 | IDE |
| `com.unity.collab-proxy` | 2.12.4 | 版本控制面板 |
| `com.coplaydev.unity-mcp` | `file:` 嵌入式 | **MCP 桥接** |

> 版本集已归一至 Unity 2022.3 兼容范围。

---

## 5. 渲染配置

```
Pipeline        : PC_RPAsset (Universal)
Color Space     : Linear
Quality Level   : PC
Shadow Distance : 40
Renderer Feature: ScreenSpaceAmbientOcclusion
备用管线        : Mobile_RPAsset（Quality 档位 "Mobile"）
渲染层          : Default + Light Layer 1~7
```

### 环境设置（逐场景）

| 项 | `PlanetDemo` | `SolarSystem` |
|---|---|---|
| Skybox | `Skybox_Starfield` | `Skybox_Starfield`（同一个材质） |
| Ambient | **Flat**，(0.08, 0.09, 0.13) | **Flat**，(0.055, 0.062, 0.082)，更暗 |
| Reflection | Skybox（128px 探针） | Skybox，`reflectionIntensity = 0.35` |
| Fog | 关闭 | 关闭 |

> 环境设置是**逐场景**保存在 `.unity` 里的，不是全局的。两个场景共用同一个天空盒材质，
> 但环境光不同。SolarSystem 的程序化 Earth 另有局部大气和云层；其余天体仍采用白模光照。

### 天空盒的全包围修复

历史症状：天空盒只铺满朝向 `+Z` 的半边天球，`-Z` / `-X` / `-Y` 三个方向全黑。

- **采样端已重写**：材质换成工程自有的 `Planet/StarfieldPhysical`，直接用视线方向求
  `longitude = atan2(x, z)` / `latitude = asin(y)` 采样全天球，**结构上不可能**只覆盖半边；
  当年内置 `Skybox/Panoramic` 上的 `_ImageType` 开关已不存在。
- **贴图端仍然必需**：`starmap_2020_4k.exr` 的导入必须是**逐轴** `U = Repeat / V = Clamp / W = Clamp`。
  单一 `wrapMode` 字段在重新导入时会被覆盖；垂直方向不 Clamp，天极会出现接缝。

> 完整过程与六面验证方式见 [PLANET-DEMO.md](PLANET-DEMO.md) §3.1 与 [PLANET-ASSETS.md](PLANET-ASSETS.md) §4.2。

---

## 6. 场景

### `Assets/Scenes/PlanetDemo.unity` ← **build index 0**

> **定位：渲染精度参考场景。** 它演示的是"星系视角下的星球表面应当精细到什么程度"，
> 是观感与精度的参照物，**不是工程主线**；新增功能与验证都在 `SolarSystem.unity` 上进行。
> 该场景不接入程序化框架，两者互不依赖（见 §1）。

| 对象 | 组件 | 说明 |
|---|---|---|
| **Earth** | Transform, MeshFilter, MeshRenderer, `PlanetMeshGenerator`, `PlanetSpin` | (-1.5, 0, 0)，半径 1，296,065 顶点 |
| **Moon** | 同上 | (2.3, 0, 0)，半径 0.2725（真实半径比），131,841 顶点 |
| **Sun** | Transform, Light, `UniversalAdditionalLightData` | 平行光，强度 1.7，软阴影 |
| **Focus** | Transform | 相机环绕中心 |
| **Main Camera** | Transform, Camera, `OrbitCamera`, `UniversalAdditionalCameraData` | FOV 45，SMAA High，后处理开启 |
| **Global Volume** | Transform, Volume | Bloom + ACES + Vignette |

地形起伏实测：**地球为半径的 2.08%，月球为 7.13%**。详情见 `PLANET-DEMO.md`。

### `Assets/Scenes/SolarSystem.unity` ← **build index 2**

真实比例太阳系与程序化 Earth 示例，**整个场景由代码生成**（`Tools ▸ Planet ▸ Rebuild Solar System Scene`），
不是手工摆放的。

| 对象 | 组件 | 说明 |
|---|---|---|
| **Solar System/Bodies** | 21 × `CelestialBody` | 太阳、八大行星、月球、木卫一至四、土卫六、海卫一、冥卫一、谷神星、冥王星、妊神星、鸟神星 |
| **Bodies/Earth** | `PlanetSurfaceView`、`PlanetAtmosphere`、`PlanetCloudLayer` | Cube Sphere 表面、海洋、可选生态与壳层；半径由统一接口 `IPlanetRadiusProvider` 供应 |
| **Solar System/Orbits** | 12 × `OrbitPath` + 2 × `CircleLine` | 12 条完整轨道椭圆 + 30 AU / 50 AU 柯伊伯带边界圈 |
| **Solar System/Asteroid Belt 2.1-3.3 AU** | `BeltField` | 2200 个粒子，真实轨道根数，`Graphics.DrawMeshInstanced` 绘制 |
| **Solar System/Kuiper Belt 30-50 AU** | `BeltField` | 5000 个粒子，同上 |
| **Solar System/Sun Light** | Light, `UniversalAdditionalLightData`、`PlanetLightingController` | 平行光，强度 1.35；阴影关闭；驱动壳层光照与星空测光 |
| **Main Camera** | Camera, `SolarSystemCamera`, `SolarSystemHud`, `UniversalAdditionalCameraData` | FOV 50，动态近/远裁剪面，SMAA High，后处理开启 |
| **Global Volume** | Volume | `Assets/Settings/SolarSystemPost.asset`（Bloom 0.85 + ACES + Vignette 0.22） |

**比例**：1 场景单位 = **1,000,000 km**，无任何压缩。太阳半径 0.6963 单位，地球轨道 149.60 单位，
柯伊伯带外缘 7,480 单位。所有轨道在 `double` 下以 km 计算，只在提交 `Transform` 时转 float 并减去
焦点位置（浮动原点）。太阳在地球处的视直径算得 **0.533°**，真实 0.532°。

**完整说明见 [PLANET-SOLARSYSTEM.md](PLANET-SOLARSYSTEM.md)**（轨道力学、数值验证、坐标系映射、
操作键位、光照取舍、编辑器工具）。

### `Assets/Scenes/SampleScene.unity` ← **build index 1**

模板自带的空场景（Main Camera / Directional Light / Global Volume，无内容），保留作为对照。

---

## 7. 纹理资产与显存

`Assets/Textures/` 共 **12 张贴图，磁盘 182.9 MB**。

| 贴图 | 尺寸 | 格式 | sRGB | GPU |
|---|---|---|---|---|
| `earth_color_8k` | 8192×4096 | BC7 | ✔ | 42.7 MB |
| `earth_color_2k` | 2048×1024 | BC7 | ✔ | 2.7 MB |
| `earth_height_16bit` | 8192×4096 | **R16** | ✘ | 85.3 MB |
| `earth_height_2k_16bit` | 2048×1024 | **R16** | ✘ | 5.3 MB |
| `earth_normal_2k` | 2048×1024 | DXT5 | ✘ | 2.7 MB |
| `earth_metallic_smoothness` | 2048×1024 | DXT5 | ✘ | 2.7 MB |
| `moon_color_8k` | 8192×4096 | BC7 | ✔ | 42.7 MB |
| `moon_color_2k` | 2048×1024 | BC7 | ✔ | 2.7 MB |
| `moon_height_16bit` | 5760×2880 | **R16** | ✘ | 42.2 MB |
| `moon_height_4ppd_16bit` | 1440×720 | **R16** | ✘ | 2.6 MB |
| `moon_normal_2k` | 2048×1024 | DXT5 | ✘ | 2.7 MB |
| `starmap_2020_4k` | 4096×2048 | RGBAHalf | ✘ | 85.3 MB |
| | | | **合计** | **319.5 MB** |

> **关于显存数字的口径**：上表按格式几何计算（含 mip 链，即 ×4/3），是真实的 GPU 占用。
> Unity 的 `Profiler.GetRuntimeMemorySizeLong` 对这 12 张贴图报告 **639.0 MB**，恰好是上表的 1.91 倍——
> 该 API 把 CPU 侧镜像也算进去了。**做显存预算时用 319.5 MB。**

**所有 8K 贴图都已把 `maxTextureSize` 提到 8192** —— Unity 默认值 2048 会静默把所有 8K 图压成 2K。
月球的 5760×2880 与 1440×720 是 NPOT，另需 `npotScale = None`，否则会被降到 4096/1024。

---

## 8. 代码

太阳系逻辑使用 UnityEngine 与新输入系统；独立 `Planet.Procedural` 程序集使用已安装的
Unity.Mathematics。地形与纹素缓冲按面并行计算，Unity Mesh / Texture 上传仍在主线程。

### 8.1 太阳系（`Assets/Scripts/Solar/`，13 个文件）

这是 `SolarSystem.unity` 的轨道与姿态逻辑；位置和自转每一帧从日期算出来，Earth 的外观另由
`Procedural/` 及其派生 Cubemap 驱动。

| 脚本 | 职责 |
|---|---|
| `Astro.cs` | 双精度矢量、轨道根数、开普勒方程、日期和坐标换算 |
| `SolarSystemCatalog.cs` | 21 个天体的物理与轨道数据、土星环分段 |
| `SolarSystemSimulation.cs` | 日期、位置、自转、浮动原点、光照和轨道线 |
| `SolarSystemFactory.cs` | 从 Catalog 创建场景对象、相机和环境 |
| `CelestialBody.cs` | 白模或程序化表面适配、实际渲染半径、环和转轴 |
| `SolarSystemCamera.cs` | 焦点切换、取景、对数缩放、环绕、输入 |
| `SolarSystemHud.cs` | 日期、比例、天体物理量与操作提示 |
| `BeltField.cs` | 轨道粒子实例化与恒定张角标记 |
| `SolarMeshFactory.cs` | UV 球、扁率与多段圆环 |
| `OrbitPath.cs` | 轨道椭圆线 |
| `SolarSystemPoses.cs` | 可复现截图机位 |
| `SolarMaterials.cs` | 材质缓存与经纬网开关 |
| `CircleLine.cs` | 柯伊伯带边界参考圆 |

### 8.2 编辑器工具（`Assets/Editor/`，3 个文件）

| 脚本 | 职责 |
|---|---|
| `SolarSystemBuilder.cs` | 重建太阳系、白模材质、11 机位出图 |
| `PlanetFrameworkBuilder.cs` | 程序化示例构建、缓存管理、视觉刷新 |
| `PlanetSurfaceBaker.cs` | 按面并行生成地表与云密度 Cubemap |

### 8.3 其余脚本

| 目录 | 文件数 | 职责 |
|---|---|---|
| `Assets/Scripts/`（根） | 5 | 高程球、相机、自转、大气和表面测光 |
| `Assets/Scripts/Procedural/` | 10 | 统一定义、坐标、采样、Cube Sphere、云、半径接口与共享壳层 Mesh |

### 8.4 着色器

| 着色器 | 行数 | 用途 |
|---|---|---|
| `ClayGrid.shader` | 119 | 太阳系白模：Lambert + 平坦环境光 + 由 UV 生成的经纬网 |
| `OrbitLine.shader` | 68 | 轨道线：顶点色 + alpha 混合（URP/Unlit 会忽略 `LineRenderer` 的顶点色） |
| `PlanetStarfield.shader` | — | 全天球 HDR 星空、运行时全局曝光 EV、周期 UV 导数采样 |
| `PlanetEarthPhysical.shader` | 167 | 地球物理材质 |
| `PlanetProceduralSurface.shader` | — | 程序化地表、宽海面高光、稳定太阳光照与确定性云影 |
| `PlanetAtmosphere.shader` | — | 薄大气边缘、窄晨昏暖色、昼夜散射 |
| `PlanetCloudVolume.shader` | — | 云壳密度与视角光程近似 |

> **为什么太阳系场景需要自己写着色器**：URP/Unlit 会丢弃 `LineRenderer` 的顶点色，做不出
> 可调透明度的轨道线；而一个完全均匀的白色球体，**转与不转看起来一模一样**，所以白模着色器
> 带了一层经纬网（`L` 键可关掉），让自转可见。

模板自带的 `Readme.cs` / `ReadmeEditor.cs` 仍在 `Assets/TutorialInfo/`，不影响构建。

### 8.5 程序化星球表面生成框架

一条**定义驱动**的生成链：`PlanetDefinition → PlanetSurfaceSampler → CubeSphere / PlanetMeshData →
PlanetSurfaceView`。定义是唯一权威，网格与 Cubemap 都是可重建的派生缓存；采样器是纯函数，
只按体固单位球方向取值，因此与网格密度、星球半径和场景尺度都无关。

| 关注点 | 现状 |
|---|---|
| 接入范围 | 只有 `SolarSystem.unity` 的 **Earth**；其余 20 个天体仍是白模 |
| 参数 | 米 / 秒 / 千克；`features` 分别开关海洋、大气、云、生态、冰帽 |
| 网格 | 6 面 × 96 段，56,454 顶点 / 110,592 三角形；物理高度保持米制，显示用 4× 起伏夸张 |
| 派生缓存 | 256²×6 Albedo + 128²×6 云密度 Cubemap，指纹存于 importer 元数据，重建保留 GUID |
| 海平面 | 由地形最低 / 最高点按比例推导（示例 −438.55 m）；与地形函数分离，改水位不会移动山脉 |
| 壳层 | 半径经 `IPlanetRadiusProvider` 取自模拟，不设固定最小直径；大气比 1.012557、云比 1.009 |
| 光照 | 不读 URP 级联阴影；云影用共享密度场；96 方向样本驱动全局星空曝光 EV |

菜单 **Tools ▸ Planet ▸ Build Procedural Planet Example** 走完整条链并保存场景；
**Refresh Procedural Planet Visuals** 复用现有 Cubemap，只刷新材质与壳层参数。

> **完整说明见 [PLANET-PROCEDURAL.md](PLANET-PROCEDURAL.md)**：设计原则、坐标与单位、高度函数与
> 水体推导、网格接缝处理、烘焙缓存与指纹、三个着色器的取舍、壳层与半径同步、分辨率预算、
> 新增天体的步骤、已知限制与踩坑备忘。

仍属近似的部分：云与大气是壳层近似（无体积光线步进、无云自阴影）；地表不投 / 不收 URP 阴影，
没有地形自遮挡；位移只在网格顶点上，没有位移贴图 / 视差；没有地表流式 LOD 与植被实例。

---

## 9. 版本控制

```
分支        : main
基线提交    : efd0999  "星系浏览产品基线（附最新文档）"
本轮内容    : 程序化星球可选特性、水体统计、构建优化、大气比例、星空曝光与接缝修复
文档提交    : 新增 PLANET-PROCEDURAL.md，并同步总览 / 场景 / 资产 / 演示文档的定位与交叉引用
历史提交    : 110212a（材质修复）、84f07b5（项目总览）、1171425（初始工程）
```

提交状态与哈希以 `git log` 为准；示例资源、场景、源码及本轮验证截图纳入版本控制。

**Git LFS 跟踪**：贴图、模型、音频、视频、字体、压缩包。
**保留为普通文本**（可 diff）：`.unity` `.asset` `.mat` `.prefab` `.meta` `.inputactions` 及全部源码——靠的是 Unity 的 Force Text 模式。

`.gitattributes` 里同时把文本文件统一为 **LF**，并设 `core.autocrlf=false`。
这两件事必须一起做：否则 git 会在每次 checkout 时改写行尾，导致 Unity 反复重新导入。

**未入库的内容**（`_assets_src/` 下约 380 MB）：原始 TIFF/JPG/EXR 与 SRTM 瓦片缓存。
它们可由 `_assets_src/*.py` 从头重新生成，没必要进仓库。
**`.py` 脚本和 `qa/` 对比图是入库的**，所以整条资产管线可复现。

---

## 10. MCP 集成

工程已接入 **MCP for Unity**，四个本机客户端可驱动编辑器：

| 客户端 | 状态 |
|---|---|
| DSH | ✅ 已验证（本次所有场景/资产操作均通过它完成） |
| codex | ✅ 已验证（`codex mcp list` 显示 enabled） |
| opencode | ✅ 已验证（`opencode mcp list` 显示 connected） |
| MiMo AI | ⚠️ 配置已写入，需人工在界面确认 |

- 服务端 `mcp-for-unity-server` 3.4.7，协议 `2024-11-05`
- 桥接 `127.0.0.1:6400` —— **单消费者**，同一时刻只应一个客户端驱动
- 实例标识 `Planet@73cccd62`，注册工具 **47 个** + 19 个资源

详见 `MCP-Setup/MCP-HANDSHAKE-REPORT.md` 与 `MCP-Setup/mcp-tool-list.json`。

---

## 11. 磁盘占用（2026-10-03 基线快照）

| 目录 | 大小 | 备注 |
|---|---|---|
| `Library/` | **2,353.7 MB** | 导入缓存，可安全删除重建，**不入库** |
| `_assets_src/` | 384.2 MB | 原始数据缓存，**基本不入库** |
| `.git/` | 225.7 MB | 含 LFS 对象池 |
| `Assets/` | **281.6 MB** | **入库**（较上一版 +84.1 MB，主要是截图） |
| `Packages/` | 3.4 MB | **入库** |
| `Logs/` | 0.9 MB | 不入库 |
| `ProjectSettings/` | 0.1 MB | **入库** |
| `UserSettings/` | 0.1 MB | 不入库 |
| **合计** | **≈ 3,250 MB** | |

`Assets/` 内部构成：`Textures/` 182.9 MB、`Screenshots/` 93.6 MB、`Resources/` 4.0 MB、
`Scenes/` 0.7 MB、`Scripts/` 0.2 MB、`Settings/` 0.1 MB，其余为 `.meta` 与模板文件。

> `Screenshots/`（93.6 MB，46 张 PNG）是纯验证产物，删掉不影响工程运行，是 `Assets/` 里最容易
> 回收的一块。真正必需的资产只有 `Textures/` 与 `Settings/`。

---

## 12. 风险与注意事项

1. **不要用 Unity 6 打开本工程。** `Library/` 曾被 Unity 6.3 打开并降级，`manifest.json` 也一度混入 Unity 6 的包（URP 17.3.0、`ai.navigation` 2.0.12 等），已修复。再用 Unity 6 打开会重新污染。

2. **`com.unity.ai.navigation` 已移除。** 原 Unity 6 模板引入的 2.0.12 在 2022.3 下编译失败（调用了不存在的 `NavMesh.SetLinkActive`），且工程并未引用。若日后需要寻路，请装 2022.3 兼容的 1.x。

3. **改高度解码参数会让地形错乱。** `heightOffsetMetres` / `heightSpanMetres` 必须与贴图编码一致：
   ```
   地球：metres = (raw / 65535) * 20000  - 11000
   月球：metres = (raw / 65535) * 32767.5 - 10000
   ```

4. **别为了"颜色好看"把高程图改成 RGBA32。** 那会把 16 位压成 8 位，地球高程量化误差从 **0.154 m 劣化到约 40 m**。R16 在 Inspector 里显示为红色是正常的（只有 R 通道有数据）。

5. **git clone 后必须装 git-lfs**，否则贴图会是指针文件而非图像。

6. **网格分辨率与 CPU 副本的权衡**：`PlanetMeshGenerator` 需要 CPU 读图，用的是 2K/4ppd 高程版本。8K 高程图是给 shader 位移和近景用的。若把网格分段数调大，需要相应换用 8K 高程图，并注意 CPU 端内存（8K R16 的 CPU 副本约 67 MB）。

7. **`Run In Background` 必须保持开启。** Unity 编辑器默认**关闭**它，此时只要 Unity 窗口失去焦点（比如你在旁边用浏览器看文档），**播放循环整个停止**——实测 Play 模式开了 9 分钟只跑了 **2 帧**。后果极其隐蔽：`Update()` / `LateUpdate()` 从不执行，一切"每帧做一次"的逻辑全部失效，而画面停在第一帧看起来还算正常。太阳系场景最初就是因为这个，所有轨道线停留在相机还在原点时算出的线宽（`1×10⁻⁵`）而完全不可见，排查了很久才定位到根因。**已设为 `true` 并保存**，请勿改回。

8. **太阳系的坐标精度依赖浮动原点。** 不要"顺手"把 `SolarSystemSimulation` 里的原点改成对焦点位置的指数平滑——那会引入 `速度 × 时间常数` 的稳态误差：5 天/秒时只有 4 个单位看不出问题，36,500 天/秒时涨到 30,000 单位，行星直接飞出画面。正确写法是"刚性跟随 + 衰减残差"。

9. **`CapturePoses` 必须一个编辑器 tick 只处理一个机位。** 碎带标记走 `Graphics.DrawMeshInstanced`，其提交会保留到**帧末**；在一个循环里同步跑完 11 个机位，会让 11 个不同浮动原点的同一片碎带叠在一帧里画出来，画面变成一圈圈同心甜甜圈。同理，那个函数末尾**不能**调 `AssetDatabase.Refresh()`——它会重载域并把编辑器踢出 Play 模式。

10. **程序化星球：改了采样数学就要提升 `PlanetDefinition.GeneratorVersion`。** 网格与 Cubemap 的缓存判定只看内容指纹；噪声、阈值、公式变了但序列化参数没变时，指纹不变，缓存不会失效——结果是"旧地形套着新参数的颜色"，而且不会有任何报错。细节见 [PLANET-PROCEDURAL.md](PLANET-PROCEDURAL.md) §5 与 §19。

---

## 13. 文档索引

| 文档 | 内容 |
|---|---|
| `PROJECT-OVERVIEW.md` | 本文档：工程结构与配置总览 |
| `DEVLOG.md` | 开发日志：按日期的简要记录与踩坑备忘 |
| `PLANET-ASSETS.md` | 贴图来源、授权、编码公式、复现步骤 |
| `PLANET-DEMO.md` | 贴图版地球/月球**参考场景**：构成、参数、操作方法、"高度图为何是红色" |
| `PLANET-PROCEDURAL.md` | **程序化星球表面生成框架**：数据链、坐标与单位、地形 / 水体 / 云算法、网格与烘焙缓存、着色器取舍、已知限制 |
| `PLANET-SOLARSYSTEM.md` | 真实比例太阳系场景（正式工程场景）：轨道力学、数值验证、坐标系、操作键位、光照取舍 |
| `MCP-Setup/MCP-HANDSHAKE-REPORT.md` | 四客户端握手验证记录 |
| `MCP-Setup/mcp-tool-list.json` | 47 个工具的完整清单 |

> **长期规划刻意不在根目录留档。** 这是为了避免过长的文档挤占注意力；路线图与后续目标另行保存，
> 根目录只放"当前工程状态"类文档，请勿在此补充规划性内容。

### 上手顺序建议

1. 打开 `Assets/Scenes/SolarSystem.unity` → Play。按 `1`–`0` 在太阳与行星间飞行，`空格` 暂停，
   `-` / `=` 调时间流速看公转快慢，`L` 开关经纬网看自转。
2. 想看贴图版地球（精度参考），打开 `Assets/Scenes/PlanetDemo.unity` → Play。
3. 场景是**代码生成**的：改了 `SolarSystemCatalog` / `SolarSystemFactory` 里的数字后，
   跑一次 **Tools ▸ Planet ▸ Rebuild Solar System Scene**，不要手工在 Hierarchy 里挪物体。
4. 改程序化星球的外观：先读 [PLANET-PROCEDURAL.md](PLANET-PROCEDURAL.md)，
   参数改 `Assets/Resources/Solar/ProceduralTerra.asset`，再跑
   **Tools ▸ Planet ▸ Build Procedural Planet Example**（改到噪声 / 公式时别忘了提升 `GeneratorVersion`）。

---

*本文档数据由 Unity Editor 实时查询与 git 仓库状态生成，非推测。*
