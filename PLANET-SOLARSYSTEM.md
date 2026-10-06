# Planet — 真实比例太阳系

`Assets/Scenes/SolarSystem.unity`：从**太阳**到**柯伊伯带**按真实比例搭建的太阳系，
每个天体的**自转**与**公转**全部由代码按开普勒轨道根数推算，场景由代码生成而非手工摆放。

- 场景文件：`Assets/Scenes/SolarSystem.unity`（Build index 2）
- 代码：`Assets/Scripts/Solar/`（程序集 `Planet.Solar`，边界见
  [DEVELOPMENT-WORKFLOW.md](DEVELOPMENT-WORKFLOW.md) §2）
- 着色器：`Assets/Shaders/`；定义与派生资源：`Assets/Resources/Solar/`
- 上游：[PROJECT-OVERVIEW.md](PROJECT-OVERVIEW.md)

外观、环与未实现的天文特征见 [PLANET-MATERIALS.md](PLANET-MATERIALS.md)；
尺度坐标层见 [PLANET-SPACE.md](PLANET-SPACE.md)；Earth 的纹理分级见
[PLANET-TEXTURE-LOD.md](PLANET-TEXTURE-LOD.md)；太阳的独立光球框架与纯色发光回退见
[PLANET-STELLAR.md](PLANET-STELLAR.md)。
后两者只读取公开的观察契约，不修改输入与轨道。

---

## 1. 比例：为什么是 1 场景单位 = 1,000,000 km

Unity 的浮点坐标只有约 7 位有效数字。地球轨道 1 AU ≈ 1.496×10⁸ km，柯伊伯带外缘 50 AU
≈ 7.48×10⁹ km。若 1 单位 = 1 km，坐标会到 7.5×10⁹，float 在小数点后已无精度可言，抖动肉眼可见。

选定 `kmPerUnit = 1,000,000` 后：太阳半径 0.6963 单位、地球半径 0.006371 单位、
地球轨道 149.60 单位、柯伊伯带外缘 7,480 单位。

**比例本身没有被压缩**：海王星就在真实的 30.07 AU 处，没有为了好看把它拉近。
**太阳在地球处的视直径算得 0.533°，真实 0.532°**——这就是比例正确的直接证据。

代价是行星本身会变成亚像素：从 2 AU 外看，地球直径只有约 0.04 像素。
这是真实比例必然的结果，不是 bug。应对手段有三条，都在场景里：

- **轨道线**：行星永远在自己轨道的椭圆上，这是找位置的主要依据。
- **`G` 键**：把天体半径放大 400×（HUD 会标注"非真实比例"），用于确认"确实有东西在那里"。
- **数字键 1–0 / `Tab`**：直接飞到天体上，近到能看清为止。

### 浮动原点

所有轨道计算都在 `double` 下以 **km** 为单位进行，只在提交给 `Transform` 的最后一步才转成
`float` 的场景单位。正式漫游路径减去玩家观察者位置；未启用观察者的兼容工具路径仍跟随焦点：

```
世界坐标 = 焦点天体位置 + (天体位置 − 焦点天体位置) × unitsPerKm
```

正式路径中相机位于渲染原点附近，选中天体不决定原点。近地局部真值与投影契约见
[DEVELOPMENT-WORKFLOW.md](DEVELOPMENT-WORKFLOW.md) §3。

原点必须拆成「**刚性跟随 + 只衰减不反馈的残差**」：直接对焦点位置做指数平滑会引入
`速度 × 时间常数` 的稳态误差，时间流速越高越致命。机制与实测见
[PLANET-SPACE.md](PLANET-SPACE.md) §2.2，事故记录见
[PLANET-PITFALLS.md](PLANET-PITFALLS.md) §1。

### 尺度坐标层解决的问题

浮动原点解决的是"看得见的这一段"，但**没有解决"地表厘米级细节"**：单位球形式的地球顶点步长
实测 0.4657 m（float32 在地球半径处的步长），放不下 1 cm 的石头。这一层由
`Assets/Scripts/Solar/Space/` 承担：四级架树、Cube-Sphere 分块网格、屏幕空间误差 LOD、
每帧硬预算的流式加载、按投影像素分档的远场策略。**完整说明见 [PLANET-SPACE.md](PLANET-SPACE.md)**。

本层**不实现大气散射**：它只提供 `viewHeight` 的位置部分，让介质随相机高程连续变化，
从而**消除地表 / 太空场景切换的断点**。

---

## 2. 轨道力学

`Assets/Scripts/Solar/Astro.cs` 与 `SolarSystemCatalog.cs`。

轨道根数取 **JPL/Standish《Keplerian Elements for Approximate Positions of the Major Planets》
Table 1（1800–2050 适用）**，含 6 个根数各自的长期变化率 `ȧ, ė, i̇, L̇, ϖ̇, Ω̇`。

求解流程（`KeplerianElements.PositionAu`）：

1. `T = (JD − 2451545.0) / 36525`，用变化率把根数推到目标时刻；
2. `M = L − ϖ`，牛顿迭代解 **开普勒方程** `E − e·sin E = M`；
3. 轨道平面内坐标 `x' = a(cos E − e)`，`y' = a√(1−e²)·sin E`；
4. 用 `ω = ϖ − Ω`、`i`、`Ω` 三次旋转转到黄道坐标。

月球用 **Meeus《Astronomical Algorithms》** 的简化根数。卫星（木卫一至四、土卫六、海卫一、
冥卫一）用圆轨道近似，周期取实测值。

### 数值验证（场景中实测）

| 检验 | 结果 |
|---|---|
| 地球 J2000 日心距 | 0.9833 AU（真实近日点附近，✓） |
| 地球一个周期后的闭合误差 | 2.9×10⁻⁶ AU（约 437 km，来自长期变化率的线性化） |
| 地球黄经每 1/4 年推进 | 100.38° → 192.38° → 280.55° → 8.55° → 100.38° |
| 每季度推进角度不均匀 | 92.00° / 88.17° / 88.00° / 91.83°，近日点快、远日点慢（✓ 开普勒第二定律） |
| 太阳在地球处的视直径 | 计算值 0.533°，真实 0.532° |
| 地球自转周期 | 23.9344696 h，自转 360.000° 后 mod 360 回到 0.00° |
| 四颗巨行星 J2000 日心距 | 4.966 / 9.172 / 19.922 / 30.117 AU，与逐项手算一致 |

### 转轴倾角不是"相对黄道"

HUD 里显示的**转轴倾角**是自转轴与**该天体自身轨道面法线**的夹角，而不是与黄道北极的夹角。
两者差别很大：火星轨道倾角 1.85°，按黄道算会得到 26.71°，按自身轨道面算才是教科书上的 25.19°。

**IAU 极点并不总是自转矢量本身。** 规则是「逆向自转**且**极点位于黄道北侧」才翻转：
金星 2.64° → 177.36°，天王星 82.24° → 97.76°；而冥王星公布的极点本身就指向黄道南侧，
它就是自转矢量，再翻一次会得到荒谬的结果。三个方向各异的特例用同一条规则就都对上了
（推导与实测对照见 [PLANET-PITFALLS.md](PLANET-PITFALLS.md) §5）。

### 自转与公转是怎么驱动的

自转角度是**时间的一次函数**（不是每帧累加），所以拖时间轴不会积累误差，
暂停 / 恢复 / 跳到任意日期都能得到同一个姿态：

```csharp
Vec3d au = def.Orbit.PositionAu(julianDate);   // 日心黄道坐标，AU
_posKm[i] = au * OrbitalMechanics.AuKm;        // 公转

double spinDeg = 360.0 * (julianDate - J2000) / (RotationHours / 24.0);
transform.rotation = poleRotation * Quaternion.AngleAxis((float)spinDeg, Vector3.up);
```

### 坐标系：右手黄道 → 左手 Unity

```
(X, Y, Z)_黄道  →  (x, z, −y)_Unity
```

右手系转左手系必须镜像一个轴。选最后一个轴取反，行星从黄道北极看下去就是**逆时针**公转，
与真实太阳系一致。

---

## 3. 场景内容

| 分组 | 内容 |
|---|---|
| `Bodies/` | 21 个 `CelestialBody`：太阳、八大行星、月球、木卫一至四、土卫六、海卫一、冥卫一、谷神星、冥王星、妊神星、鸟神星 |
| `Orbits/` | 12 条 `OrbitPath`（行星 + 矮行星的完整椭圆）+ 2 条 `CircleLine`（30 AU / 50 AU 柯伊伯带边界） |
| `Asteroid Belt` / `Kuiper Belt` | 两个 `BeltField`，粒子带真实轨道根数 |
| `Sun Light` | 一盏平行光，从太阳指向当前焦点天体 |
| `Main Camera` | `SolarSystemCamera` + `SolarSystemHud` + URP `UniversalAdditionalCameraData` |
| `Global Volume` | `Assets/Settings/SolarSystemPost.asset` |
| `SolarSpaceContext` | 尺度坐标层组件（[PLANET-SPACE.md](PLANET-SPACE.md)） |
| `SolarSystemVisualCoordinator` | 把相机契约接到纹理 LOD、恒星与地表地形消费者的单向适配器 |

### Earth

使用 `ProceduralTerra.asset`：地表、海洋、生态、云与大气全部由程序化采样生成，没有贴图。
**框架实现见 [PLANET-PROCEDURAL.md](PLANET-PROCEDURAL.md)**，当前取值见该文档 §5；
近景纹理分级见 [PLANET-TEXTURE-LOD.md](PLANET-TEXTURE-LOD.md)。

`features` 分别提供 `hasOcean`、`hasAtmosphere`、`hasClouds`、`hasEcology`、`hasIceCaps`；
水位与地形函数分离，改水位不会移动山脉。

`CelestialBody` 把**自己的场景缩放**序列化在组件上，而不是每帧从目录推算；
换算的出处记在 `bakedKmPerUnit` 里，比例对不上时会在运行时警告。
规则详见 [DEVELOPMENT-WORKFLOW.md](DEVELOPMENT-WORKFLOW.md) §4.1。

其余 20 个天体各有独立的 `PlanetDefinition`、Albedo Cubemap 和材质，位于
`Assets/Resources/Solar/BodyMaterials/`。气态行星不套用生态或表面海洋；
Venus/Titan 以不透明可见层隐藏地表；Haumea 保留三轴形状。解释范围见
[PLANET-MATERIALS.md](PLANET-MATERIALS.md)。

### 两条碎带是真的在转

每个粒子都带真实的 `a`、`e`、`i`、`Ω`、`ϖ`、`M₀`，用**和行星同一套**开普勒求解器推进，
周期由开普勒第三定律给出。于是内圈比外圈跑得快，碎带会自己剪切开——这是手摆的装饰粒子
做不到的。分布实测：主带 a ∈ [2.06, 3.27] AU，柯伊伯带 a ∈ [30.00, 50.00] AU 且在 26 AU
以内一个粒子都没有。

**唯一刻意的夸张是标记点的大小，不是它们的位置。** 100 km 的小行星在真实比例下是
1×10⁻⁴ 场景单位，永远不可能被看见，所以标记点按"相对相机的固定张角"缩放并带随机尺寸抖动。
求解节奏的自适应优化见 [PLANET-SPACE.md](PLANET-SPACE.md) §6.2。

### 材质与经纬参考网

所有天体使用 `Planet/ProceduralSurface`；太阳走自发光与临边昏暗分支，环使用
`Planet/ProceduralRing`。`Planet/ClayGrid` 与旧白模材质保留作为缺少程序化定义时的回退资源。

浏览默认关闭经纬网；按 `L` 切换程序化表面的全局角度经纬参考网。
网格使用 `fwidth` 抗锯齿——这个场景从 0.006 单位的天体跨到 7,480 单位的柯伊伯带，
固定世界宽度的线在某一端必然是错的。

### 土星环的卡西尼缝

土星环不是一整片。`RingSet` 存的是**分段的内外半径**（单位 km），`SolarMeshFactory.Rings`
按这些分段生成多个同心圆环：D / C / B 环、卡西尼缝、A 环（内含约 325 km 的恩克缝与
约 42 km 的开勒缝）、罗希缝、F 环。只画一个实心圆环，土星看起来就是个盘子。

分段半径表是目录数据的一部分，见 `SolarSystemCatalog`；环的渲染与遮光近似见
[PLANET-MATERIALS.md](PLANET-MATERIALS.md) §3–§4。

---

## 4. 操作

| 按键 | 作用 |
|---|---|
| `1` … `9`, `0` | 依次飞向太阳、水星、金星、地球、火星、木星、土星、天王星、海王星、冥王星 |
| `Tab` / `Shift+Tab` | 下一个 / 上一个天体（21 个全在这条链上，含卫星与矮行星） |
| `F` | 重新取景 |
| 滚轮 / `PgUp` `PgDn`（或 `Z` `X`） | 有目标且非近地时推进/后退；近地或无目标时滚轮调速 |
| 右键拖动 / 右键 + QWEASD | 环视 / 自由飞行，深空与近地均可用 |
| `Esc` | 取消临时聚焦和导航目标，保留当前运动状态 |
| `空格` | 暂停 / 继续 |
| `-` / `=` | 时间流速减半 / 加倍 |
| `\` | 时间流速复位到默认值 |
| `N` / `R` | 跳到当前真实日期 / 回到 J2000 历元（并取消暂停） |
| `G` | 天体半径放大 400×（仅为可见，非真实比例） |
| `L` | 经纬网格开 / 关 |
| `H` | 显示 / 隐藏 HUD |

正式运行时由 `UnifiedFlightController` 统一驱动自由飞行与临时聚焦。
右键环视和 QWEASD 在深空/近地均可用；数字键/Tab/F 临时聚焦，Esc 取消目标。
滚轮在目标推进与自由飞行调速之间按近地状态分配。契约与阈值见
[DEVELOPMENT-WORKFLOW.md](DEVELOPMENT-WORKFLOW.md) §3.1。

HUD 显示模拟日期、时间流速、焦点天体的半径/扁率/自转周期/转轴倾角/轨道半长轴/偏心率/
轨道倾角/公转周期，以及比例尺和当前视距。

---

## 5. 光照的已知取舍

场景只有**一盏**平行光，方向从太阳指向当前焦点天体。

- 焦点天体上的光照是**准确的**：太阳在任何比水星更远的地方张角都小于 1°，平行光完全够用，
  昼夜分界线、极昼极夜、土星环的受光面都对。
- **广角俯视时不对**：整个太阳系同框时，海王星和地球收到的是同一个方向的光，
  而真实情况是光从中心向外辐射。
- 阴影被**关闭**：1×10¹⁰ km 的场景里级联阴影贴图没有意义，因此没有行星落在卫星上的食。
  环投在天体表面、天体遮住环的太阳光改由**解析求交**计算；当前遮挡球体忽略扁率／三轴形状，
  环影忽略细小地形。

这些都是为了「每个天体单独看的时候是对的」而做的取舍。

程序化地表同样**不读取 URP 级联阴影贴图**；云影由共享密度 Cubemap 沿主光方向偏移采样。
地形法线影响光照，但地形遮挡式自阴影、食和完整体积云尚未实现。

### 星空曝光

`PlanetLightingController` 缓存球面方向样本，读取统一地表采样器的反照率与法线，
按可见投影权重、太阳颜色和强度估计地表亮度，据此平滑更新星图的全局曝光 EV——
星球越亮、占屏越大，星空整体越暗。

这是**表面测光近似**，不是 URP 全画面的自动曝光，且运行时**不会写回**共享天空盒材质资产。
相机移动、暂停后的环绕和焦点变化都会继续更新。完整的相机曝光设计见
[PLANET-STELLAR.md](PLANET-STELLAR.md) §5。

### 星空天空盒

贴图 `Assets/Textures/Sky/starmap_2020_4k.exr`（4096×2048，OpenEXR half-float，线性）：
NASA SVS《Deep Star Maps 2020》ID 4851，Hipparcos-2 + Tycho-2 + Gaia DR2；
**工程里唯一一张 HDR 贴图**，材质 `Assets/Settings/Skybox_Starfield.mat`，
shader `Planet/StarfieldPhysical`。必须用 EXR 而非 PNG：恒星亮度跨多个数量级，
8 位 sRGB 会把暗星压成纯黑、亮星削顶，银河细节全丢。

导入设置：`RGBAHalf`、**sRGB 关闭**、`maxTextureSize` 4096、mipmap 开启、`textureType = Default`、
`alphaSource = None`。两条**契约**：

1. **逐轴 wrap，不能用单值 `wrapMode`。** 经度 −180° 与 +180° 是同一条子午线，U 必须 `Repeat`；
   纬度到极点就结束了，**V 环绕会在天极拉出接缝**，必须 `Clamp`；W 未使用，同样 `Clamp`。
   单值字段会在重新导入时被 Unity 覆盖回默认值，所以必须逐轴设 `wrapModeU/V/W` 后
   `SaveAndReimport()`。该组合与用哪套 shader 无关——同一张贴图它都成立。
2. **经度接缝要在采样端修。** `atan2` 在 ±π 处跳过一个整周期，普通 `tex2D` 的隐式求导会把这个
   差值当成巨大的纹理足迹，选到最模糊的 mip，形成一条竖向暗线。写法是：① 在 `frac` 包装**之前**
   求 UV 的屏幕导数；② 对经度导数取最短周期差 `delta -= round(delta)`；③ 用 `tex2Dgrad` 采样，
   纬度钳制到边缘纹素中心。**没有**对星图做接缝模糊带处理，也没有修改源 EXR。

半包围这个故障**结构上不可能再出现**：老材质用内置 `Skybox/Panoramic`，它有一个
`_ImageType` 开关（180°/360° 两种铺法），半包围就是它被设成 180° 造成的。现在的
`Planet/StarfieldPhysical` 直接用视线方向求经纬度（`longitude = atan2(x, z)`、`latitude = asin(y)`），
整个天球由同一个公式覆盖，不存在一个能把它关掉一半的开关。

验证方式：用一台 `cullingMask = 0`、只渲染天空盒的相机朝六个轴向渲染 96×96 面板，
统计非黑像素比例——修复前 `-Z` / `-X` / `-Y` 三块全黑，修复后六个方向都布满银河与恒星。

> 若需要天文级准确取向：原始星图以 0h 赤经为中心、赤经向左递增。当前只作背景板使用，
> 未做相对黄道的姿态对齐（`_Rotation` 保持 0）。整体曝光由 `PlanetLightingController` 驱动，
> **运行时不会写回**共享天空盒材质资产。

---

## 6. 编辑器菜单（Tools ▸ Planet）

本节是**菜单全表的唯一 owner**。菜单按优先级排序，加新菜单时在此登记。

| 菜单项 | 脚本 | 作用 |
|---|---|---|
| **Rebuild Solar System Scene** | `SolarSystemBuilder` | 从 `SolarSystemCatalog` 重新生成整个场景（含全部天体材质接线与秤重烘焙）并保存 |
| **Rebuild White-Model Materials** | `SolarSystemBuilder` | 重新生成 `Assets/Resources/Solar/` 里的回退白模 / 轨道材质 |
| **Open Solar System Scene** | `SolarSystemBuilder` | 打开正式场景 |
| **Build Procedural Planet Example** | `PlanetFrameworkBuilder` | 走完 `PlanetDefinition → 采样 → Cubemap → 材质 → 场景` 整条链并保存 |
| **Refresh Procedural Planet Visuals** | `PlanetFrameworkBuilder` | 复用已有 Cubemap，只刷新材质与壳层参数并保存场景；**编辑模式专用** |
| **Build Solar System Materials** | `SolarSystemMaterialBuilder` | 分批构建全部天体的定义／独立缓存／材质，完成后保存场景；保留已有定义的手工外观调整 |
| **Bake Body Scales** | `BodyScaleBaker` | 把 `RadiusKm ÷ kmPerUnit` 一次性烘焙进星球的 `sceneRadiusUnits`；改目录或 `kmPerUnit` 后必须重跑 |
| **Build Earth Texture LODs** | `EarthTextureLodBuilder` | 烘焙 Earth 的四档纹理（[PLANET-TEXTURE-LOD.md](PLANET-TEXTURE-LOD.md)） |
| **Build Stellar Framework** | `StellarFrameworkBuilder` | 生成独立恒星定义、温度 LUT 与两个材质，并迁移 Sun 接线（[PLANET-STELLAR.md](PLANET-STELLAR.md)） |
| **Validate Body Scales** | `BodyScaleBaker` | 只读审计：列出与目录不符、未烘焙、或按另一个 `kmPerUnit` 烘焙过的星球 |
| **Build Planet Mesh LODs** | `PlanetMeshLodBuilder` | 生成离线 Cube Sphere Mesh 档位资产。**正式场景已改用运行时分块地形，该路径只在 `useRuntimeChunks = false` 的原型里作为回退**（[PLANET-PROCEDURAL.md](PLANET-PROCEDURAL.md) §16） |
| **Audit Space Scale** | `SpaceScaleAuditor` | 只读尺度审计，报告七节写入 `Library/SpaceScaleAudit.txt`（[PLANET-SPACE.md](PLANET-SPACE.md) §9） |
| **Capture Solar System Poses** | `SolarSystemBuilder` | 按 `SolarSystemPoses` 的机位出图到 `Assets/Screenshots/`（需在 Play 模式） |
| **Reimport Solar Screenshots** | `SolarSystemBuilder` | 出图后手动触发资源导入 |

场景是**代码生成**的：改比例、加天体、调分辨率都改 `SolarSystemCatalog` / `SolarSystemFactory`
里的数字，然后重跑菜单，不会出现"场景和 HUD 数字对不上"的情况。

**出图工具的两条约束**（原因见 [PLANET-PITFALLS.md](PLANET-PITFALLS.md) §7）：
`CapturePoses` 必须**一个编辑器 tick 只处理一个机位**，且末尾**不能**调 `AssetDatabase.Refresh()`
——前者会让多个浮动原点的同一片碎带叠在一帧，后者会把编辑器踢出 Play 模式。
