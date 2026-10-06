# Planet — 程序化星球表面生成框架

> 代码：`Assets/Scripts/Procedural/`（程序集 `Planet.Procedural`）、
> `Assets/Scripts/PlanetAtmosphere.cs`、`PlanetLightingController.cs`，以及
> `Assets/Editor/` 下的三个程序化构建菜单
> 着色器：`Planet/ProceduralSurface`、`Planet/Atmosphere`、`Planet/CloudVolume`
> 定义资产与派生资源：`Assets/Resources/Solar/`

一句话：**给定一个 `PlanetDefinition`，框架生成这颗星球的地形几何、地表颜色、云密度、
大气与云壳层，并让同一份采样数据同时服务于运行时查询、网格生成和贴图烘焙。**

正式场景 `SolarSystem.unity` 的 **21 个天体均已接入**。Earth 保留原 Terra 地形与云层，
并使用 [四档视距纹理](PLANET-TEXTURE-LOD.md)；其余天体使用独立的岩壳、冰壳、气层、雾霾或
发光光球定义，特征与近似范围见 [PLANET-MATERIALS.md](PLANET-MATERIALS.md)。
太阳已迁移到独立[恒星框架](PLANET-STELLAR.md)：`CelestialDefinition → StellarDefinition →
StellarSurfaceView`；行星仍使用本文件的采样链，基础质量／半径由 `CelestialDefinition` 共享。

上游：[PROJECT-OVERVIEW.md](PROJECT-OVERVIEW.md)。
程序集边界与对外契约的权威版本见 [DEVELOPMENT-WORKFLOW.md](DEVELOPMENT-WORKFLOW.md) §2。

---

## 1. 设计原则

1. **定义是唯一权威。** `PlanetDefinition` 里的参数是真相；网格、Cubemap、材质都是**派生缓存**，
   可以随时删除重建，同一指纹 + 同一分辨率下结果可复现。
2. **采样器是纯函数。** `PlanetSurfaceSampler` 是 blittable 结构体，采样路径里没有 UV、没有网格 LOD、
   没有全局 `Random` 状态、不读贴图、不碰 `UnityEngine.Object`。它按**单位球方向**取值，
   因此与分辨率、与星球半径、与场景尺度全都无关。
3. **米制 + 比例渲染。** 参数用米、秒、千克；几何按「方向 × (参考扁球面 + 高度)」存储，
   渲染时用 `localScale = 场景半径` 缩放。于是 1× 与 400×（`G` 键）下，
   大气 / 云壳与地表的比例完全相同。
4. **一个数据源，多条消费路径。** 高度、气候、水体、生物群系、配色、云密度都在同一个采样器里，
   网格、贴图烘焙、星空测光各取所需，不会出现「贴图和几何对不上」。

> `Planet.Procedural` 引用了 `Unity.Mathematics` 与 `Unity.Burst`（`autoReferenced`）。
> 采样器写成 blittable 就是为了将来接 Burst，**但当前代码里没有任何 `[BurstCompile]`**，
> 目前的并行只来自 `Parallel.For` 与 `Task`。

---

## 2. 数据流

```
PlanetDefinition (ScriptableObject — 权威参数)
        │ CreateSampler()
        ▼
PlanetSurfaceSampler (blittable，纯函数)
        ├─ EvaluateHeight / Evaluate / EvaluateAlbedoOnly / EvaluateCloudDensity
        │        ├─► 离线网格路径  ─► PlanetMeshLodSet / PlanetMeshData ─► Mesh
        │        ├─► 运行时地形路径 ─► RuntimeTerrainSource ─► RuntimeTerrainChunk ─► Mesh
        │        └─► PlanetSurfaceBaker ─► Albedo / 云密度 Cubemap ─► 材质
        ▼
PlanetSurfaceView     (场景适配器：选走离线网格还是运行时分块，并绑定材质与纹理)
PlanetAtmosphere / PlanetCloudLayer  (壳层，每帧读 IPlanetRadiusProvider 的当前半径)
PlanetLightingController             (推太阳方向 + 全局星空曝光 EV)
```

- **构建期**（编辑器）：构建菜单走完整条链并保存场景（§15）。
- **运行期**：`PlanetSurfaceView` 按指纹与配置选择几何路径；壳层每帧反查半径。

**两条几何路径并存**是本框架当前最重要的现状，见 §16。

---

## 3. 代码地图

按职责分组；一行的粒度到「这一组负责什么」，具体文件用符号名检索。

| 位置 | 职责 |
|---|---|
| `Procedural/CelestialDefinition.cs` | 天体定义**基类**：`displayName` + `PlanetPhysicalParameters physical`（质量、半径、极点、参考形状）。`PlanetDefinition` 与 `StellarDefinition` 都继承它，恒星不持有行星的海洋／生物群系／矿物 albedo |
| `Procedural/PlanetDefinition.cs` | 权威参数资产：物理 / 生成 / 配色 / 特性 / 外观；参数钳制、内容指纹、地形统计。`shapeScale` 支持三轴形状（Haumea） |
| `Procedural/PlanetSurfaceSampler.cs`、`PlanetSurfaceSample.cs` | 采样器与单次采样结果结构：高度、完整样本、仅颜色、云密度、表面位置、生物群系与配色 |
| `Procedural/PlanetCoordinates.cs` | 经纬度 ↔ 方向、双精度局部东/上/北坐标、渲染坐标换算 |
| `Procedural/PlanetAppearanceParameters.cs`、`PlanetAppearanceSampler.cs` | 非类地外观：表面类型枚举、矿物颜色、撞击坑、冰裂纹、气带、球面大斑、光学高度、发光与临边昏暗 |
| `Procedural/CubeSphere.cs`、`PlanetMeshData.cs` | 六面映射、面 ↔ 方向互转、按面并行的网格生成、托管缓冲 → `Mesh` |
| `Procedural/PlanetMeshLodSet.cs`、`PlanetMeshLodController.cs` | **离线** Cube Sphere 档位资产与切换器（回退路径，见 §16） |
| `Procedural/PlanetTextureLodSet.cs`、`PlanetTextureLodController.cs` | Earth 的纹理档位与异步绑定（[PLANET-TEXTURE-LOD.md](PLANET-TEXTURE-LOD.md)） |
| `Procedural/PlanetSurfaceView.cs` | 场景适配器：几何路径选择、网格缓存、材质与纹理绑定、渲染器开关、半径缩放 |
| `Procedural/PlanetShellMesh.cs`、`PlanetCloudLayer.cs`、`IPlanetRadiusProvider.cs` | 共享壳层球、云壳组件、壳层读取当前渲染半径的接口 |
| `Procedural/Stellar*.cs` | 恒星定义、Icosphere、温度辐射 LUT 与光球表面（[PLANET-STELLAR.md](PLANET-STELLAR.md)） |
| `Procedural/Terrain/PlanetViewState.cs`、`IPlanetSurfaceQuery.cs`、`ViewHeightRatios.cs` | **跨程序集契约** C1 / C2 / C-ratio（[DEVELOPMENT-WORKFLOW.md](DEVELOPMENT-WORKFLOW.md) §2.1） |
| `Procedural/Terrain/PlanetDetailParameters.cs`、`PlanetDetailSampler.cs` | 双精度米制域的细节频段场，按顶点间距淡入／截断未解析频段 |
| `Procedural/Terrain/TerrainLattice.cs`、`TerrainNodeKey.cs`、`RuntimeTerrain*.cs` | **运行时**分块地形：格点阶梯、节点键、选择、构建、渲染与 `IPlanetSurfaceQuery` 实现 |
| `Scripts/PlanetAtmosphere.cs` | 大气壳层组件 |
| `Scripts/PlanetLightingController.cs` | 太阳方向同步、壳层实例发现、星空曝光 EV |
| `Editor/PlanetSurfaceBaker.cs`、`PlanetFrameworkBuilder.cs`、`PlanetDefinitionEditor.cs` | 烘焙、派生资产生成与接线、定义 Inspector |
| `Editor/PlanetMeshLodBuilder.cs`、`EarthTextureLodBuilder.cs` | 离线网格档位与 Earth 纹理档位的烘焙菜单 |

---

## 4. 单位、坐标与尺度

| 项 | 约定 |
|---|---|
| 单位 | 米、秒、千克（几何位移、海平面、坡度采样距离全部是米） |
| 体固坐标 | **+Y 是北极，经度 0 在 +X，经度 +90 在 +Z** |
| 纬度 | **地心纬度**（不是地理纬度）；用 `atan2` 求纬度，避免极点附近 `asin` 放大归一化误差 |
| 海拔 | 相对**参考扁球面**的径向距离，不是大地水准面高 |
| 局部坐标 | `PlanetLocalFrame`：x = 东、y = 上、z = 北，`double` 精度，单位米 |
| 渲染换算 | 先在 `double` 下减去渲染原点，再转 `float` 场景坐标 |

顶点位置是「**单位方向 + 归一化高度**」的组合：

```text
参考半径系数 r = 1 / sqrt(x² + z² + y²/(1−f)²)         // f = 扁率
有海洋：显示位移 = seaLevel + max(height − seaLevel, 0) × visualReliefScale + opticalHeight
无海洋：显示位移 = height × visualReliefScale + opticalHeight
顶点 = 方向 × ( r + 显示位移 / radiusMetres )
```

因此同一份网格可以同时用于 1× 与 400× 显示，只要改 `localScale`。
查询接口返回的始终是**米制真值**：`visualReliefScale` 只改变显示几何与法线，
海平面、水深、海陆判定及物理高度保持不变。极端倍率可能穿过现有云／大气壳，
且相机目前仍按参考半径限位；**显示夸张模式不能当作最终近地碰撞表面**。

> `PlanetCoordinates` 是给外部调用者准备的 API，**当前渲染路径没有用到它**：
> `ToRenderPosition`、`NormalizeHeight` / `NormalEncodingScale`、`CubeSphere.DirectionToFace`
> 目前都没有消费者。改动这些不会影响画面，反之也不要以为它们参与了渲染。

---

## 5. 定义资产：`PlanetDefinition`

五组参数，`ValidateParameters()` 在 `OnValidate` 与 `CreateSampler()` 时钳制取值范围：

| 组 | 内容 |
|---|---|
| `physical` | 半径、质量、自转周期（**带符号，负值 = 逆向自转**）、扁率、IAU 极点赤经/赤纬、海平面、大气高度；`SurfaceGravity` 是派生量 |
| `generation` | 种子、大陆/山系/细节的频率与幅度、域扭曲、海平面比例、视觉夸张、坡度采样距离、气候偏移、云场 |
| `palette` | 10 个基色：深海、浅海、沙滩、沙漠、草原、森林、雨林、苔原、岩石、雪 |
| `features` | `hasOcean` / `hasAtmosphere` / `hasClouds` / `hasEcology` / `hasIceCaps` |
| `appearance` | 可见表面类型、矿物颜色、撞击坑、冰裂纹、气带、球面大斑、光学高度、发光、临边昏暗、环与大气外观；每个预设保存观测解释和局限 |

示例 `Assets/Resources/Solar/ProceduralTerra.asset`（Earth）的关键值——**本节是这些取值的 owner**：

| 参数 | 值 | 参数 | 值 |
|---|---|---|---|
| 半径 / 扁率 | 6,371,000 m / 1÷298.257 | 种子 | 271828 |
| 海平面 | −438.55 m | 海平面比例 | 0.52 |
| 大气高度 | 80,000 m | 视觉夸张 | **20×**（脚本默认 4×，以资产为准） |
| 大陆频率 / 域扭曲 | 1.65 / 0.32 | 大陆倍频 | 6 |
| 海盆深度 / 高原高度 | 7,200 m / 1,600 m | 山系频率 / 高度 | 12 / 6,200 m |
| 细节频率 / 高度 | 140 / 220 m | 坡度采样距离 | 2,200 m |
| 云频率 / 覆盖率 | 5.4 / 0.49 | 云细节频率 | 23 |
| 地形范围（粗采样） | −7,200 … +5,802.79 m | 水覆盖率（粗采样） | 0.5638 |
| 温度 / 湿度偏移 | 0 / +0.02 | `altitudeCoolingPerKilometre` | 0.085 |

**内容指纹**：`ContentFingerprint() = Hash128(GeneratorVersion + 五组参数的 JSON)`，
用于网格与贴图缓存判定；`appearance` 参与指纹。含 `PlanetDetailSampler` 的区块缓存改用
`TerrainContentFingerprint()`（`GeneratorVersion` + `detail`）。

以下 §6–§8 的高度与气候公式描述 `Terrestrial` 类型；其他类型由 `PlanetAppearanceSampler` 采样。
气巨星、雾霾、光球的高度为光学参考层上的零起伏，另由 `opticalSurfaceHeightMetres` 控制可见壳层
半径；样本 `biome` 可为 `CloudTop`、`Haze`、`Photosphere`、`Volcanic`、`Rock`。

> **改了采样数学（噪声、阈值、公式）就必须提升 `GeneratorVersion`**，否则参数一个没变、
> 指纹不变，缓存不会失效。失效方式是「旧地形套着新参数的颜色」，且不会有任何报错
> （[PLANET-PITFALLS.md](PLANET-PITFALLS.md) §4）。

---

## 6. 高度函数

`PlanetSurfaceSampler.EvaluateHeight(direction)`，全部噪声作用在**单位球方向**上：

```text
p    = d × 2.1 + seedOffset                     // 种子偏移，三个分量独立哈希
warp = (snoise(p), snoise(p + 31.73), snoise(p − 19.31))
w    = d + warp × domainWarp                    // 域扭曲：让大陆边缘不规则

continental = Fbm(w × continentFrequency + seedOffset, 6 倍频, 0.51)
              + 0.11 × snoise(d × 0.9 + seedOffset + 107.3)
coast       = continental − continentThreshold

coast < 0 （海盆）:
    h = −oceanDepth × saturate(−coast × 3.4) ^ 0.72

coast ≥ 0 （陆地）:
    interior = Smooth(0, 0.065, coast)
    plateau  = plateauHeight × saturate(coast × 3.2) ^ 1.35
    belt     = Smooth(0.27, 0.75, snoise(w × 4.7 + seedOffset − 47) × 0.5 + 0.5)  // 山脉带遮罩
    ridge    = Ridged(w × mountainFrequency + seedOffset, 6 倍频)                  // 脊状噪声
    relief   = mountainHeight × belt × ridge ^ 2.1 × interior
    detail   = detailHeight × Fbm(d × detailFrequency + seedOffset, 4 倍频, 0.5) × interior
    h        = max(0, plateau + relief + detail)
```

要点：

- **海盆与陆地是两支独立函数**，不是一条曲线取负。海盆用 `−coast` 的幂次压深，
  因此越远离大陆越深。
- **陆地高度恒 ≥ 0 m。** 海平面可以是负数（示例 −438.55 m），这不代表有问题：
  它只说明水位落在「陆地最低点 0 m」与「海盆最深 −7,200 m」之间。
- `interior` 把山地限制在大陆内部，`belt` 让山脉成带而不是均匀铺开。
  这两项是**刻意设计**，改动前先读本节与 §7。
- 没用 UV、没有极点压缩，因此**同一颗种子在不同分辨率、不同半径下生成同一颗星球**。

---

## 7. 采样输出

`Evaluate(direction)` 返回 `PlanetSurfaceSample`：

| 字段 | 含义 |
|---|---|
| `heightMetres` | 米制高度（真值，不含视觉夸张） |
| `slopeDegrees` | 坡度，由**固定物理步长**的中心差分得到 |
| `temperature` / `moisture` | 归一化 0–1 气候场 |
| `ocean` | 连续海陆遮罩，水深 0–120 m 之间平滑过渡 |
| `waterDepthMetres` / `waterContent` | 水深（米）/ 归一化水深 |
| `biome` | `PlanetBiome`（见下表） |
| `normal` | 地形法线（含 `visualReliefScale`，与显示几何一致） |
| `albedoLinear` | 线性空间地表颜色 |

- **法线与分辨率无关**：切向差分用 `slopeSampleDistanceMetres`（示例 2,200 m）作为固定物理距离，
  而不是「网格格距」，所以换网格密度不会改变光照模型本身，只会把同一法线场采样得更细。
  （反过来说，换网格段数后光照「变了」说明改的是这个参数，而不是网格。）
- **气候**：温度 `= saturate(1.02 − |y|^1.35 − 海拔×0.085/km + 噪声)`；湿度在 `|y| = 0.43`
  （约 ±25.5° 纬度）处被压出一条干燥带——对应地球的副热带干旱带，沙漠因此出现在正确的纬度。
- **生物群系**按固定优先级判定，先命中先返回：

| 顺序 | 条件 | 结果 |
|---|---|---|
| 1 | 有海洋且 `h < 海平面` | `Ocean` |
| 2 | 有冰盖且温度 < 0.16 | `Ice` |
| 3 | 关闭生态 | `Alpine` |
| 4 | 有海洋且高出海平面 < 100 m | `Coast` |
| 5 | 高出海平面 > 3,600 m | `Alpine` |
| 6 | 温度 < 0.35 | `Tundra` |
| 7 | 湿度 < 0.34 且温度 > 0.4 | `Desert` |
| 8 | 湿度 < 0.52 | `Grassland` |
| 9 | 温度 > 0.72 且湿度 > 0.65 | `Rainforest` |
| 10 | 其它 | `Forest` |

群系只决定**配色倾向**（经 `Albedo()` 混合成连续颜色），不是硬边色块：配色还会叠加岩石
（高度 / 坡度阈值）、雪线、海滩过渡，以及两个尺度的噪声色变。

---

## 8. 水体与海平面

**水位是地形统计的产物，不是手填的数字**：

```text
terrainMin / terrainMax = 地形函数在六面均匀方向上的最小 / 最大值
seaLevel     = lerp(terrainMin, terrainMax, seaLevelFraction)
waterDepth   = max(0, seaLevel − h)
waterContent = waterDepth / max(seaLevel − terrainMin, 1 m)
oceanMask    = Smooth(0, 120 m, waterDepth)
```

- `RecalculateTerrainStatistics(resolution)` 默认 24 段，编辑器构建时用 28 段。
  这是**粗采样**：范围与覆盖率都是估计值，不是面积积分。
- **水位与地形函数分离**：调 `seaLevelFraction` 只改水位，山脉不会移动；
  反过来改地形参数后需要重算统计，否则水位还是旧的。
- `waterCoverage` 是「方向均匀采样中处于水下的比例」——不是按球面积分的真实海洋占比。
- **关闭海洋**（`hasOcean = false`）：水位记 0、顶点不再压平，**海底盆地几何直接可见**；
  颜色退回陆地配色（同时关闭生态则为纯岩石色）。

**海面本身没有波浪几何**：开启海洋时顶点高度被钳到水位，海床不参与几何，
只参与高度查询与水深计算；海面法线由地形法线平滑过渡到扁球面法线（`oceanMask` 控制），
高光由地表着色器负责。

---

## 9. 几何：Cube Sphere

六个面各自等分，避免 UV 球在极点的挤压：

| 面 | `CubePoint(face, uv)`，uv ∈ [−1, 1] |
|---|---|
| `PositiveX` | `( 1, −v, −u)` |
| `NegativeX` | `(−1, −v,  u)` |
| `PositiveY` | `( u,  1,  v)` |
| `NegativeY` | `( u, −1, −v)` |
| `PositiveZ` | `( u, −v,  1)` |
| `NegativeZ` | `(−u, −v, −1)` |

- **接缝**：顶点 UV 用**对称整数坐标** `(2x − R)/R` 之后再归一化，于是相邻两面共享边拿到
  **逐位相同**的方向输入。若先除再减，两边的舍入差会被地形法线的有限差分放大成可见缝。
- 三角形绕序 `a, c, b` 与 `b, c, d`（外向面）。
- **顶点属性**：`POSITION`（夸张后的位置）、`NORMAL`（含海洋法线混合）、
  `TEXCOORD0` = 面 UV、`TEXCOORD1` = 体固方向、
  `TEXCOORD2` = `(高度m, 坡度°, 海洋遮罩, 归一化水深)`。
  着色器靠 `TEXCOORD1` 采样 Cubemap、靠 `TEXCOORD2` 恢复坡度与海洋信息，不需要额外贴图。
- 索引格式按顶点数自动选择（> 65,535 → `UInt32`）。
- 六面用 `Parallel.For` 并行；`PlanetMeshData` 是托管缓冲，`CreateMesh` 之后即可回收。
- 运行时生成的 `Mesh` 带 `HideFlags.DontSave`：**网格不进场景文件**，每次打开场景重新生成
  （指纹命中则复用已生成的那一份）。离线档位资产见 §16。

---

## 10. 烘焙与缓存

`PlanetSurfaceBaker` 按面并行烘焙两张 Cubemap（`MaxDegreeOfParallelism = CPU − 2`）：

| 贴图 | 尺寸 | 格式 | 内容 |
|---|---|---|---|
| `ProceduralTerra_Albedo.cubemap` | 256² × 6 | RGBA32 | 地表颜色 |
| `ProceduralTerra_CloudDensity.cubemap` | 128² × 6 | RGBA32 | 云密度（灰度） |

- **颜色快路径**：`EvaluateAlbedoOnly` 与 `Evaluate` 共用高度、气候、水体、配色函数，
  只是省掉切向差分（每个纹素少 4 次高度采样）。代价是**烘焙颜色里没有坡度项**（坡度传 0），
  坡度明暗由地表着色器在运行时补上。
- **缓存判定**：importer 的 `userData == "PlanetBake:" + ContentFingerprint()`，同时校验贴图尺寸。
  改了参数 → 指纹变 → 自动重烘。
- **GUID 保护**：新结果先烘焙到临时对象，再 `EditorUtility.CopySerialized` 覆盖已有资产，
  **不是删除重建**；写入指纹后必须重新导入该派生资源，否则紧接着再次构建会读到旧指纹。
  否则场景与材质里的引用会全部变成 Missing。

构建耗时随机器变化，不作为契约；缓存命中会跳过烘焙。

---

## 11. 地表着色器 `Planet/ProceduralSurface`

- **按方向采样 Cubemap**（`SAMPLE_TEXTURECUBE(_AlbedoCube, 方向)`），不用 UV、不用展开、
  没有 UV 接缝。
- **海洋**：用顶点数据里的 `oceanMask` 把地形法线混到扁球面法线，与几何压平用的是同一套规则。
- **昼夜分界用扁球面法线**：`twilight = smoothstep(−0.32, 0.30, dot(扁球法线, 光方向))`。
  地形法线只影响受光面的明暗（`0.16 + 0.84 × N·L`），不会把晨昏线切出一条直线阴影，
  也不会放大面接缝。
- **不读取 URP 级联阴影**：渲染器彻底关闭阴影投射与接收，着色器只取主光方向。
  在 1×10¹⁰ km 的场景里级联阴影没有意义，采样它会带来闪烁的半圆盘和移动的级联接缝
  （构建器也把太阳光的 `shadows` 设为 `None`）。
- **云影是确定性的**：把方向朝光源偏 `_CloudShadowOffset` 后采样**同一张**云密度 Cubemap，
  所以云影与云壳天生同源，且不依赖阴影图。
- **高光刻意做宽**（指数随 smoothness 从 7 插值到 30）：早期用 128 会让 Cubemap 在轨道视角下
  读成「瓷砖式」波纹法线。
- 另有地形坡度压暗（最高 0.72×）与大气边缘项（`_RimColor` / `_RimStrength` / `_RimPower`），
  后者只是薄壳大气的补充，主要边缘辉光来自大气壳层。

---

## 12. 壳层：大气与云

两者共用 `PlanetShellMesh.Sphere`（平滑球，半单位半径，`HideFlags.DontSave`），
用 `localScale = 直径` 缩放，Y 轴额外乘 `1 − 扁率` 以贴合扁球。

**大气**（`PlanetAtmosphere` + `Planet/Atmosphere`）

- 壳层半径比 = `1 + atmosphereHeightMetres / radiusMetres`，是**物理高度算出来的**，
  不是手调的魔法数字，因此在 1× 与 400× 下一致。
- 半径未知（`RadiusUnits ≤ 0`）或特性关闭时**直接隐藏壳层**，而不是先画一帧单位半径的球。
- 光学厚度按视线夹角近似（临边路径更长），晨昏暖色被限制在**终止线附近的窄弧**内；
  负的 `N·L` 不会被钳到 0 去给整个夜面染色。
- ⚠️ **属性由 `MaterialPropertyBlock` 逐实例覆盖**：`_Density`、`_RimPower`、`_DayScatter`、
  `_NightScatter`、`_Twilight*`、`_AtmosphereColor`、`_SunColor`、`_ForwardGlow`、`_SunDirection`
  的实际值来自**组件字段**，改材质上同名属性不会有任何变化。
  `_MieStrength` / `_MiePower` / `_SoftEdge` 没有对应组件字段，仍由材质决定。

**云**（`PlanetCloudLayer` + `Planet/CloudVolume`）

- 外半径比与内半径比是示例值；传入着色器的 `_InnerRadius` 是「内 / 外」之比。
- ⚠️ **`_InnerRadius` 当前着色器并未使用**：壳层厚度实际由网格缩放决定，
  调这个属性不会有任何变化。
- 密度来自云密度 Cubemap；光学路径长度用视线夹角近似，于是云层在边缘有厚度感，
  而不是一张贴在球上的贴花；银边来自前向散射。
- **ShadowCaster 关闭**：让 URP 阴影图去投云影，会在天体尺度下闪烁并切出半圆直线，
  云影改走共享密度场。
- 云密度场本身（`EvaluateCloudDensity`）是「基础噪声 0.82 + 细节噪声 0.18」，
  再按 `cloudCoverage` / `cloudSoftness` 做平滑阶跃，因此改覆盖率是**平移阈值**而不是重画图案。
- 类地表面的云场随后读取同源高度、海陆与生物群系使用的温湿度函数；近海岸平滑过渡，
  浅水同样提供水汽，未把水深当作蒸发强度。`cloudOceanDensity` / `cloudLandDensity` 控制海陆密度，
  `cloudMoistureInfluence` 控制湿度响应，`cloudAltitudeBias` 控制海拔响应。
- `cloudOrographicInfluence` 通过 `cloudUpwindDistanceMetres` 范围内的迎风高程差近似抬升与雨影，
  纬度决定平滑的盛行风方向，极点处切向风趋零。它是**静态统计近似**，
  未模拟水汽守恒、锋面或真实天气。
- `cloudTerrainInfluence = 0` 恢复原纯噪声场；非类地外观不套用海陆水汽规则，
  `hasClouds = false` 始终输出零。

---

## 13. 半径同步：`IPlanetRadiusProvider`

`CelestialBody`（`Planet.Solar`）实现该接口并发布 `RadiusUnits`；
大气与云在 `LateUpdate` / `Refresh()` 里**自取**，不再由模拟每帧推送。

- 于是 `G` 键的 400× 放大同时作用于地表与壳层，**比例不变**。
- 早期版本会给壳层强制一个最小直径，在 400× 下比例就崩了；现在**不设固定最小直径**，
  只按实际半径缩放。
- 因为半径由模拟写入，场景刚打开、模拟还没跑第一帧时壳层是隐藏的——**这是刻意行为，不是丢失**。

---

## 14. 光照与星空曝光

`PlanetLightingController` 挂在太阳平行光上，每 `updateInterval` 跑一次：

1. 取当前焦点天体；若不是程序化天体，用相位近似退回到简单估算。
2. 缓存球面方向的法线与亮度（按 `ContentFingerprint` 失效重算），按**可见投影权重**估计地表亮度，
   得到星空曝光 EV（钳在 −4 … +1.5 EV，并乘上星球屏幕覆盖度）。
3. 写入全局 `_PlanetSkyExposureEV`，由 `PlanetStarfield.shader` 用 `exp2(_Exposure + EV)` 消费——
   星球越亮、占屏越大，星空整体越暗，避免「天空比星球还亮」。
4. 定期重新发现场景里的所有 `PlanetAtmosphere` / `PlanetCloudLayer` 并推太阳方向。
   **新增壳层不需要手工接线**，挂上组件即可。

这是**表面测光近似**，不是 URP 的全画面自动曝光；运行时**不会写回**共享天空盒材质资产。
相机移动、暂停后环绕、切换焦点都会继续更新。

---

## 15. 编辑器工具与操作顺序

菜单全表在 [PLANET-SOLARSYSTEM.md](PLANET-SOLARSYSTEM.md) §6（那里的 owner）。
本节只讲与程序化框架相关的**调用顺序**：

| 场景 | 该跑什么 |
|---|---|
| 首次生成 Earth，或改了 `PlanetDefinition` 的生成参数 | **Build Procedural Planet Example**（重算地形统计 → 烘焙 Cubemap → 材质 → 接线 → 保存场景） |
| 只调材质 / 壳层数值 | **Refresh Procedural Planet Visuals**（复用已有 Cubemap，省掉一次烘焙；**编辑模式专用**） |
| 改 `SolarSystemCatalog` 或比例之后 | **Rebuild Solar System Scene**（内部调用 `SolarSystemMaterialBuilder.ConfigureSolarSystem` 接入全部天体材质） |
| 全部天体接入、修改定义后重烘 | **Build Solar System Materials**（按编辑器更新分批构建，保留已有定义的手工外观调整） |
| 改了采样数学 / 地形参数，正式场景要更新几何 | **Build Planet Mesh LODs**（生成器按 `ContentFingerprint + resolution` 写入 Mesh importer 元数据，更新时用 `CopySerialized` 保留 GUID） |

- 烘焙本身是纯 CPU 采样，不依赖 Play 模式；但两个构建菜单都会**写并保存场景**，
  所以都别在 Play 模式下跑。
- `Rebuild Solar System Scene` 会在场景生成后接入离线 Mesh Set；**单独原型若没有缓存，
  必须显式打开 `allowRuntimeMeshGeneration`**，否则缺少缓存会报错，而不会把长时间生成
  隐藏在 Play 初始化中。

---

## 16. 几何与纹理的运行时路径

统一漫游路径使用固定全球网格作为远景，`RuntimeTerrainRenderer` 只在玩家实际靠近时预热，
完整覆盖就绪后显示并继续细化。其碰撞代理复用区块显示顶点并排除裙边，位于局部米制物理场景；
显示碰撞查询与未夸张的物理高程接口分开。交接契约见 [DEVELOPMENT-WORKFLOW.md](DEVELOPMENT-WORKFLOW.md) §3。
下表保留运行时地形与历史离线 Mesh LOD 的区别；固定远景网格不是距离驱动的旧 Mesh LOD 链。


**两条几何路径并存，正式场景走的是运行时那条。**

| | 运行时分块地形（**正式场景在用**） | 离线 Mesh LOD（回退路径） |
|---|---|---|
| 入口 | `RuntimeTerrainRenderer`（由 `PlanetSurfaceView` 在 `useRuntimeChunks = true` 时自动挂载） | `PlanetMeshLodSet` / `PlanetMeshLodController` |
| 格点 | `TerrainLattice`：32 格点 × 25 层 | Cube Sphere 六面 × 2^level |
| 采样域 | 双精度体固**米制**域（`RuntimeTerrainSource`），宏高度按 16384 段插值以把 float 噪声量化挡在厘米网格之外 | `PlanetSurfaceSampler` 单位球方向 |
| 调度 | 自己的 `LateUpdate`：选择任务 0.3 s 节流、构建任务每帧最多一个、全局最多两个 worker、`uploadBudgetMilliseconds` 预算 | 只切换已导入的 Mesh |
| 档位 | 屏幕空间误差 + 叶节点预算，`maxActiveChunks` / `maxResidentChunks` | `16/32/64/96` 四档 |
| 现状 | 正式场景 21 个天体全部为 `useRuntimeChunks = 1`，`meshLodSet` 引用为空 | 场景里已无引用；派生资产保留在 `Assets/Editor/LegacyPlanetMeshLod/` |

`RuntimeTerrainRenderer` 同时实现 `IPlanetSurfaceQuery`（C2），把全频高程与背压
`PendingDetailWork` 暴露给交互层，用于厘米级离地真值。

生成器把指纹写入 Mesh importer 元数据，更新时用 `CopySerialized` 保留 GUID。
**离线段落目前没有消费者**：正式场景已改用运行时地形，菜单仍保留以便让原型在
`useRuntimeChunks = false` 时能独立重建。存废决定见 [DEVLOG.md](DEVLOG.md) 待补。

---

## 17. 分辨率预算

用「噪声特征尺度 ≈ 1 个域单位（≈ 1/f 弧度）」做的量级估计，
用来判断**某一项能不能被当前网格 / 贴图表达**：

| 量 | 说明 |
|---|---|
| 大陆（频率 1.65） | 特征约 35°，网格充分表达 |
| 山系（频率 12，6 倍频） | 基频约 4.8°，最高倍频约 0.11°；网格实际只能表达**前 2 个倍频** |
| 细节（频率 140） | 特征约 0.41°（约 45 km），**低于 96 段网格的采样率**，当前网格上看不见 |
| Albedo Cubemap | 256²/面 → 每纹素约 0.39°，高频配色（频率 670）被平均掉，靠 mip 抑制闪烁 |
| 云密度 Cubemap | 128²/面 → 每纹素约 0.70°，云带（频率 5.4 / 23）能表达，细节偏软 |

**增加显示倍率能看清已采样山体的轮廓，不能补回欠采样的山谷。**
运行时地形的格点阶梯另有一套预算，见 §19。

---

## 18. 已知限制

- **云与大气是壳层近似**，不是体积光线步进：没有真正的散射积分、没有云自阴影、没有云内阴影。
- **地表不投 / 不接收 URP 阴影**，也没有地形自遮挡（山体不会互相遮挡），因此没有日食、没有山影。
  环影和天体遮住环的太阳光采用解析求交近似，详见 [PLANET-MATERIALS.md](PLANET-MATERIALS.md) §3–§4。
- **没有植被实例、河流 / 湖泊、潮汐、海面波浪几何**；水体只有水位、水深与颜色。
- **几何缓存不会自动跟随参数变化**：改 `PlanetDefinition` 后必须跑对应构建菜单，
  不要通过启用 runtime fallback 绕过缓存。
- `PlanetLightingController` 依赖 `SolarSystemSimulation`，只能用在正式太阳系场景里，不能单独搬到别的场景。
- 新外观仍是统计场：不含观测地图、气带动力学、地下海洋、日冕、喷发、多层散射或环弧。
  `appearance.interpretation` 与 [PLANET-MATERIALS.md](PLANET-MATERIALS.md) §1 明确每颗天体的解释边界。
- **两套分块层级尚未统一**：本框架的 `TerrainLattice`（32 格点 × 25 层）与
  `Planet.Solar.Space` 的 `SurfaceChunkGrid`（6 面 × 2^level，上限 20）定义了不同的格点常数与层数，
  而只有前者接入了帧循环。见 [PLANET-SPACE.md](PLANET-SPACE.md) §11。

---

## 19. 厘米尺度与区块接入评估

**当前生成输出还不能保证任意地点的厘米级连续地形与海湖。**
本节区分已经实现的能力、尚缺的语义与建议的接入策略；观察契约以
[DEVELOPMENT-WORKFLOW.md](DEVELOPMENT-WORKFLOW.md) §2–§3 为准。

### 19.1 现有采样能表达什么

- 宏观高度已经有大陆 FBM、ridged 山系、domain warp 与多层细节 Simplex，绝非单层随机贴图。
  增加网格只能更密地采到这个场，**不会自动长出岩石、冲沟或湖泊**。
- `PlanetSurfaceSampler` 的方向和噪声输入仍为 float，且带较大种子偏移。
  把 double 方向先降成 float 再调用它，会丢失厘米变化；事后把返回值转成 double 不能补回信息。
- `Terrain/PlanetDetailSampler` 已用 double 的星体坐标米制域，频段逐级减半，
  按顶点间距淡入／截断未解析频段。**参数是截止下限**，不保证恰好有该波长，
  也不是高程精度证明。
- `TerrainLattice` 的 32 格点、25 层约能达到地球面中心厘米量级；
  它与 `Planet.Solar.Space` 的另一套 `SurfaceChunkGrid` 层级不同，**两套必须统一，
  不能只打开一个"厘米级"开关**。
- 水体目前只有一个全局海平面。低洼区域被淹**并不等于**按流域、溢流口和水量形成了湖泊，
  也没有独立水面网格与海底几何的分离。

### 19.2 按画面误差分配面积与精度

近似垂直俯视平坦地面、竖直 FOV 60°、1080 像素高时，
一个像素约覆盖 `2 × 离地高度 × tan(30°) / 1080` 米：

| 离地高度 | 地面米／像素（估算） | 合理的处理方向 |
|---|---|---|
| 10 km | 10.7 m | 大面积宏观地形，区域侵蚀缓存；无需厘米网格 |
| 1 km | 1.07 m | 加载米级网格和局部材质页 |
| 100 m | 10.7 cm | 缩小高精度区块覆盖范围，保留远处粗层 |
| 10 m | 1.07 cm | 脚边与视锥附近的厘米位移，远处继续降档 |

这是**量级示例，不是交互模式阈值**。实际选择要用投影误差、坡度、屏幕分辨率、
视锥／地平线裁剪、流式内存与每帧生成预算。网格误差和纹素密度应分别控制；
陡崖需要更多几何，平地可以主要增加材质细节。高度、颜色、水体与碰撞共享种子和世界坐标；
区块接缝用共享格点、边界一致采样及父子形变处理。

### 19.3 侵蚀与多层噪声的职责

若目标包括可信的山谷、冲积平原、河道和湖盆，建议增加**水力侵蚀／排水网络**；
若需要自然的崩积坡、碎石坡脚，建议增加**热力侵蚀**（坡角驱动搬运），按材质设休止角。
它们解决地貌**结构**，Simplex/Perlin FBM 解决统计**细节**，不能相互替代。

不要在跨入 10 km 时才改变基础地形。侵蚀应按连续区域／流域离线生成或后台生成并缓存，
远景消费其低频结果，近景逐渐加载更高频残差。缓存键包含种子、算法版本、参数与区域坐标；
邻域 halo 和统一边界通量／排水条件必须覆盖侵蚀的影响范围，**不能每块独立侵蚀后拼接**。
改变迭代次数、随机种子或水位来迎合某一 LOD，会导致海岸、河床和碰撞随视距改变。

厘米层优先用带宽限制的多层 Simplex/Perlin、岩性／湿度／坡度掩码及少量散布物；
现有 double Simplex 可以继续用，不必为了换成 Perlin 重写。低于屏幕可解析尺度的高频应转为
法线／粗糙度，不必全做顶点。水力模拟可以局部用于玩法，但不是无限地形每帧生成的必要步骤。

### 19.4 接口与缓存约束

- 区块的物理高度、法线、水体类型、局部水位、水深与碰撞应**从同一物理采样结果派生**。
  先在 double 域完成采样与减去区块原点，再生成局部 float 顶点。
- 海洋与湖泊需要**独立的水体模型**：海洋全局水位、流域／连通性、湖泊溢流口和区域水位。
  几何细化时不能重新随机判定海岸或湖岸；单纯增加噪声会制造会随 LOD 出现／消失的小水洼。
- `ContentFingerprint()` 仍用于不含近地细节的全球贴图／网格；
  **包含 `PlanetDetailSampler` 的区块缓存使用 `TerrainContentFingerprint()`**，
  后者包含 `detail` 参数与细节生成器版本。运行时地形已接这个键（§16）。
- globe 与 chunk 的可视位移统一调用 `DisplacementMetres(height)`：
  有水时围绕固定海平面夸张陆地，物理采样始终不乘显示倍率。
  **两侧必须用同一公式**，否则 globe / chunk 的交接高度不一致。
