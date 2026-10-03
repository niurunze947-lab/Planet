# Planet — 项目概览

> 更新：2026-10-03 ｜ 数据来源：Unity Editor 实时查询（MCP）+ 工程文件直读 + git 仓库状态
> 上一版把本工程描述为"几乎空白的 URP 模板工程"，那一版已完全过时，本版为准。
> **本版新增**：全包围星空天空盒、真实比例太阳系场景 `SolarSystem.unity`（详见
> **[PLANET-SOLARSYSTEM.md](PLANET-SOLARSYSTEM.md)**）。

---

## 1. 这是什么

一个 **Unity 2022.3 LTS + URP** 的行星演示工程，现在有两条并行的内容线：

**A. 真实数据的地球与月球**（`PlanetDemo.unity`）

- 地表颜色来自 NASA 真实影像，高程来自真实地形测量数据（含海底地形）
- 星球**不是贴图球**，几何体由 16 位高程图逐顶点径向位移生成
- 背景是 NASA 基于 **Gaia DR2（17 亿颗恒星）** 绘制的 HDR 星空

**B. 真实比例的太阳系**（`SolarSystem.unity`）

- 从**太阳**到**柯伊伯带**，比例不做任何压缩（海王星就在真实的 30.07 AU）
- 21 个天体 + 主带 2200 粒子 + 柯伊伯带 5000 粒子，轨道根数取自 JPL/Standish 与 Meeus
- **自转与公转全部由代码按开普勒方程推算**，不是动画、不是手 K
- 全白模，不含任何贴图

名字里的 "Planet" 现在名副其实了。

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
│   ├── Scripts/                25 个 C#（3,420 行）—— Solar/ 13 个、Procedural/ 7 个、根目录 5 个
│   ├── Editor/                 3 个 C#（444 行）—— 菜单 Tools ▸ Planet
│   ├── Shaders/                6 个 .shader（639 行）
│   ├── Settings/               URP 配置、3 个材质、后处理 Profile
│   ├── Resources/Solar/        太阳系白模材质（4.0 MB）
│   ├── Textures/               182.9 MB，12 张贴图
│   ├── Screenshots/            93.6 MB，46 张验证截图（其中 solar_* 15 张）
│   └── TutorialInfo/           模板自带（可删）
├── Packages/                   3.4 MB     ← 入库
├── ProjectSettings/            0.1 MB     ← 入库
├── MCP-Setup/                  握手报告与工具清单 ← 入库
├── _assets_src/                384.2 MB   ← 仅 .py 与 qa/ 入库
├── _mcp_setup_backup_20261002/ 配置备份    ← 入库
├── .git/                       225.7 MB（含 LFS 对象池）
├── PLANET-ASSETS.md            资产来源与授权
├── PLANET-DEMO.md              贴图版地球/月球演示场景说明
├── PLANET-SOLARSYSTEM.md       真实比例太阳系场景说明
├── PROJECT-OVERVIEW.md         本文档
└── .gitignore / .gitattributes
```

`Library/`（2,353.7 MB）与 `Logs/` 已被 `.gitignore` 排除。

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
> 但环境光不同：太阳系场景整体更暗，因为那里没有大气散射，只有恒星本身。

### 天空盒的全包围修复

原材质用的是内置 `Skybox/Panoramic`，`_ImageType` 被设成了 **`1`（180 Degrees）**，
于是天空盒只铺满朝向 `+Z` 的半边天球，`-Z` / `-X` / `-Y` 三个方向全黑。

修复分两步：

1. `_ImageType` 由 `1` 改为 **`0`（360 Degrees）**；`_Mapping` 保持 `1`（Latitude Longitude）、
   `_MirrorOnBack = 0`、`_Layout = 0`。
2. `starmap_2020_4k.exr` 的导入由单一 `wrapMode` 改为**逐轴**设置：
   **U = Repeat，V = Clamp，W = Clamp**。只改 `wrapMode` 字段在重新导入时会被覆盖，
   逐轴设置才是可靠的写法。垂直方向必须 Clamp，否则天极出现接缝。

> **后续变更**：该材质随后被换成了工程自有的 `Planet/StarfieldPhysical`
> （`Assets/Shaders/PlanetStarfield.shader`）。该 shader 直接用视线方向求
> `longitude = atan2(x, z)` / `latitude = asin(y)` 采样全天球，**本身就是全包围的**，
> 不存在 `_ImageType` 这个开关，因此上面第 1 步的枚举已不再是当前的实现方式；
> 第 2 步的**逐轴 wrap 设置仍然是必需的**，因为采样的是同一张贴图。

---

## 6. 场景

### `Assets/Scenes/PlanetDemo.unity` ← **build index 0**

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

真实比例的太阳系白模，**整个场景由代码生成**（`Tools ▸ Planet ▸ Rebuild Solar System Scene`），
不是手工摆放的。

| 对象 | 组件 | 说明 |
|---|---|---|
| **Solar System/Bodies** | 21 × `CelestialBody` | 太阳、八大行星、月球、木卫一至四、土卫六、海卫一、冥卫一、谷神星、冥王星、妊神星、鸟神星 |
| **Solar System/Orbits** | 12 × `OrbitPath` + 2 × `CircleLine` | 12 条完整轨道椭圆 + 30 AU / 50 AU 柯伊伯带边界圈 |
| **Solar System/Asteroid Belt 2.1-3.3 AU** | `BeltField` | 2200 个粒子，真实轨道根数，`Graphics.DrawMeshInstanced` 绘制 |
| **Solar System/Kuiper Belt 30-50 AU** | `BeltField` | 5000 个粒子，同上 |
| **Solar System/Sun Light** | Light, `UniversalAdditionalLightData` | 平行光，强度 1.35，**阴影关闭**（1e10 km 场景里级联阴影无意义） |
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

`Assets/Scripts/` 共 **25 个脚本、3,420 行**，`Assets/Editor/` **3 个脚本、444 行**，
**无第三方依赖**（只用 UnityEngine / UnityEditor / 新输入系统）。

### 8.1 太阳系（`Assets/Scripts/Solar/`，13 个文件，2,345 行）

这是 `SolarSystem.unity` 的全部逻辑。**没有任何贴图、没有任何动画文件**——位置和姿态每一帧从
日期算出来。

| 脚本 | 行数 | 职责 |
|---|---|---|
| `Astro.cs` | 237 | `Vec3d`（双精度矢量）、`KeplerianElements`（含 6 个长期变化率）、`MoonOrbit`、`OrbitalMechanics`（开普勒方程求解、儒略日换算、IAU 极点、黄道→Unity 映射） |
| `SolarSystemCatalog.cs` | 308 | 21 个天体的物理与轨道数据、`RingSet`（含土星环分段） |
| `SolarSystemSimulation.cs` | 408 | 主循环：推进日期、算位置、算自转、浮动原点、光照与轨道线宽度 |
| `SolarSystemFactory.cs` | 162 | 从 Catalog 生成整个场景（天体、轨道、碎带、光照、相机、环境） |
| `CelestialBody.cs` | 185 | 单个天体的可见部分：UV 球网格、扁率、环、转轴姿态 |
| `SolarSystemCamera.cs` | 193 | 飞行相机：焦点切换、取景、对数缩放、环绕、输入 |
| `SolarSystemHud.cs` | 214 | IMGUI 中文面板：日期、时间流速、焦点天体的全部物理量 |
| `BeltField.cs` | 161 | 碎带粒子场：真实轨道根数 + 实例化绘制 + 恒定张角标记 |
| `SolarMeshFactory.cs` | 161 | 程序化网格：UV 球（支持扁率与三轴形状）、多段圆环 |
| `OrbitPath.cs` | 105 | 轨道椭圆线（按偏近点角均匀采样） |
| `SolarSystemPoses.cs` | 77 | 11 个固定机位，用于可复现出图 |
| `SolarMaterials.cs` | 72 | 材质缓存与经纬网开关 |
| `CircleLine.cs` | 62 | 参考圆（30 AU / 50 AU 柯伊伯边界） |

### 8.2 编辑器工具（`Assets/Editor/`，3 个文件，444 行）

| 脚本 | 行数 | 职责 |
|---|---|---|
| `SolarSystemBuilder.cs` | 268 | 菜单 **Tools ▸ Planet**：重建太阳系场景、重建白模材质、11 机位出图（`EditorApplication.update` 驱动，每 tick 一帧） |
| `PlanetFrameworkBuilder.cs` | 133 | 程序化星球框架的搭建入口 |
| `PlanetSurfaceBaker.cs` | 43 | 地表数据烘焙 |

### 8.3 其余脚本

| 目录 | 文件数 | 行数 | 职责 |
|---|---|---|---|
| `Assets/Scripts/`（根） | 5 | 389 | `PlanetMeshGenerator`（高程图位移球）、`OrbitCamera`、`PlanetSpin`、`PlanetAtmosphere`、`PlanetLightingController` |
| `Assets/Scripts/Procedural/` | 7 | 686 | 程序化星球框架：立方球体、坐标映射、地表采样、网格数据、`PlanetDefinition` |

### 8.4 着色器（`Assets/Shaders/`，6 个，639 行）

| 着色器 | 行数 | 用途 |
|---|---|---|
| `ClayGrid.shader` | 119 | 太阳系白模：Lambert + 平坦环境光 + 由 UV 生成的经纬网 |
| `OrbitLine.shader` | 68 | 轨道线：顶点色 + alpha 混合（URP/Unlit 会忽略 `LineRenderer` 的顶点色） |
| `PlanetStarfield.shader` | 83 | 天空盒：全天球星空 + 朝日方向的昼侧淡出 |
| `PlanetEarthPhysical.shader` | 167 | 地球物理材质 |
| `PlanetProceduralSurface.shader` | 106 | 程序化星球表面 |
| `PlanetAtmosphere.shader` | 96 | 大气散射 |

> **为什么太阳系场景需要自己写着色器**：URP/Unlit 会丢弃 `LineRenderer` 的顶点色，做不出
> 可调透明度的轨道线；而一个完全均匀的白色球体，**转与不转看起来一模一样**，所以白模着色器
> 带了一层经纬网（`L` 键可关掉），让自转可见。

模板自带的 `Readme.cs` / `ReadmeEditor.cs` 仍在 `Assets/TutorialInfo/`，不影响构建。

---

## 9. 版本控制

```
分支        : main
提交        : 110212a  "Fix silently-reset _Smoothness on Earth_Surface, document the trap"
上一个提交  : 84f07b5  "Add git repo health check corrections + rewrite the project overview"
初始提交    : 1171425  "Initial commit: Unity 2022.3 LTS planet demo with real Earth/Moon assets"
工作区      : 有未提交改动（太阳系场景 / 程序化框架 / 着色器均尚未提交）
```

**未提交的主要内容**：`Assets/Scenes/SolarSystem.unity`、`Assets/Scripts/Solar/`、
`Assets/Scripts/Procedural/`、`Assets/Editor/`、`Assets/Shaders/`、`Assets/Resources/Solar/`、
`Assets/Screenshots/solar_*.png`，以及 `Skybox_Starfield.mat` 与 `starmap_2020_4k.exr.meta` 的修改。

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

## 11. 磁盘占用

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

---

## 13. 文档索引

| 文档 | 内容 |
|---|---|
| `PROJECT-OVERVIEW.md` | 本文档：工程结构与配置总览 |
| `PLANET-ASSETS.md` | 贴图来源、授权、编码公式、复现步骤 |
| `PLANET-DEMO.md` | 贴图版地球/月球演示场景：构成、参数、操作方法、"高度图为何是红色" |
| `PLANET-SOLARSYSTEM.md` | 真实比例太阳系场景：轨道力学、数值验证、坐标系、操作键位、光照取舍 |
| `MCP-Setup/MCP-HANDSHAKE-REPORT.md` | 四客户端握手验证记录 |
| `MCP-Setup/mcp-tool-list.json` | 47 个工具的完整清单 |

### 上手顺序建议

1. 打开 `Assets/Scenes/SolarSystem.unity` → Play。按 `1`–`0` 在太阳与行星间飞行，`空格` 暂停，
   `-` / `=` 调时间流速看公转快慢，`L` 开关经纬网看自转。
2. 想看贴图版地球，打开 `Assets/Scenes/PlanetDemo.unity` → Play。
3. 场景是**代码生成**的：改了 `SolarSystemCatalog` / `SolarSystemFactory` 里的数字后，
   跑一次 **Tools ▸ Planet ▸ Rebuild Solar System Scene**，不要手工在 Hierarchy 里挪物体。

---

*本文档数据由 Unity Editor 实时查询与 git 仓库状态生成，非推测。*
