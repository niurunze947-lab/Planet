# Planet — 宇宙尺度坐标阶梯与行星表面流式 LOD

`Assets/Scripts/Solar/Space/`：把「一整个星系」和「地表厘米级细节」拆成**两套坐标系**，
用**浮动原点 + 四级空间阶梯 + 分块本地坐标 + 多级 LOD 流式加载**把它们接起来。
本文回答两个问题：**单个数值格式能不能覆盖「一块石头到一整个星系」**，
以及**聚焦某颗行星时其余天体、轨道与碎带该怎么处理**。

- 代码目录：`Assets/Scripts/Solar/Space/`（编入 `Planet.Solar` 程序集）
- 审计工具：菜单 **Tools ▸ Planet ▸ Audit Space Scale**，报告写入 `Library/SpaceScaleAudit.txt`
- 回归测试：`Assets/Tests/EditMode/Solar/`，程序集 `Planet.Solar.Tests`
- 上游：[PROJECT-OVERVIEW.md](PROJECT-OVERVIEW.md)；
  [PLANET-SOLARSYSTEM.md](PLANET-SOLARSYSTEM.md)（比例与浮动原点的动机）、
  [PLANET-PROCEDURAL.md](PLANET-PROCEDURAL.md)（地表采样与网格）

---

## 1. 问题：一个 float 装不下太阳系

场景的 `kmPerUnit = 1,000,000`，即 **1 场景单位 = 10⁹ m**，地球半径因此是 **0.006371 单位**。
float32 在 0.006371 附近的**步长实测为 4.657×10⁻¹⁰ 单位 = 0.4657 m**：
单位球（globe）形式的地球，顶点位置最细只能分到 **46.6 cm**。

想在上面放一块 1 cm 的石头，需要再细 47 倍——但**问题不在格式的位数，在坐标的绝对值**：
同一条 float32，数值越接近 0 就越细。

| 距原点距离 | float32 步长 | 能分辨 |
|---|---|---|
| 1 m | 1.19×10⁻⁷ m | 0.1 µm |
| 1 km | 6.1×10⁻⁵ m | 0.06 mm |
| 6,371 km（地球半径） | 0.4657 m | 46.6 cm |
| 1 AU | 1.08×10⁴ m | 10.8 km |
| 50 AU | 5.24×10⁵ m | 524 km |

**同一套算术给出的结论**：float32 只能把 1 m 的细节保持到 **8,388.6 km**（2²³ m）以内、
1 cm 的细节保持到 **83.9 km** 以内；float64 对应 **30,105 AU** 和 **301 AU**。

而「一块石头到一整个星系」的跨度是 1 cm → 10⁵ 光年 ≈ **9.50×10²²，23 个数量级**：
float32 需要 **4 级**（每级细分 2²⁴），float64 需要 **2 级**（每级细分 2⁵³）。

> **所以答案是「不能」，而且对两种格式都是「不能」**——但这不是坏消息。这个算术同时说明
> **太阳系只需要天体之上的一级**（10¹³ m 跨度 ÷ 1 cm 特征）。这也是实际落地的级数。

---

## 2. 设计：两套坐标系，一条规则

| | 宇宙坐标系 | 地表坐标系 |
|---|---|---|
| 存什么 | 天体/轨道的**绝对位置** | 地形顶点的**相对位置** |
| 单位 | `double` **米** | `float` **米** |
| 原点 | 所在**坐标架**的原点 | **分块（chunk）自己的原点** |
| 精度 | 1 AU 处 ~0.02 mm | 9.5 m 的分块内 ~1 µm |
| 变动频率 | 每帧（浮动原点） | 只在切块时 |

**唯一的规则**：**在 double 里、在拥有该点的坐标架里做减法，最后一步才降到 float。**

这条规则的反面就是踩过的坑：把 50 AU 处的位置先降到 float 再相减，1 m 会**整个消失**
（那里 float32 的步长是 524 km）；先减再降，1 m 就是干净的 10⁻⁹ 场景单位。
`SpaceFrameTests.RenderOriginNarrowsToFloatOnlyAtTheEnd` 把这两种写法并排断言。

### 2.1 四级阶梯

```
Milky Way (Galactic)      depth 1   ← 星系尺度，默认原点即银河系中心
   └ Sol (Stellar)        depth 2   ← 太阳，也是模拟自身的原点
        └ Earth (Planetary) depth 3 ← 当前焦点天体，跟随焦点整体重建
             └ Earth/L12/13,27 (Surface) depth 4 ← 分块本地，1 单位 = 1 米
```

`SpaceFrame` 只存三样东西：`originInParentMetres`（双精度米）、`unitsPerMetre`、`parent`。
`ToRoot` / `FromRoot` / `ConvertTo` 全部沿父链做**减法**——
**换架是减法，永远不是缩放**，所以换架不引入任何精度损失。

`SpaceFrameTree` 只保留**一级**行星架：再 `EnsureBodyFrame` 同一个天体就复用并移动它，
换天体则新建一个。刻意不做对象池——池里活过头的架会在天体移动之后继续被当作渲染原点使用，
这种失效引用只会在很后面表现为「几何体出现在错误的位置」。

### 2.2 浮动原点：不许有稳态误差

`FloatingOrigin` 有两种模式：

- **`RigidFollow`（默认）**：原点**就是**锚点，另加一个只衰减、不反馈的残差。
- **`Threshold`**：原点按住不动，漂移超过阈值才付一次 rebase 的代价。地表架用这个模式
  （`surfaceRebaseThresholdMetres = 64`）。

**正确写法是把「跟随」和「平滑」拆开**：原点刚性等于锚点，残差**由换焦点时播种**
（`Reanchor` 把当前偏移塞进残差），然后只做衰减。于是跟踪在任何时间倍率下都是精确的，
换焦点的视觉平滑也没有丢。直接对焦点位置做指数平滑会引入 `速度 × 时间常数` 的稳态误差，
时间流速越高越致命（事故记录见 [PLANET-PITFALLS.md](PLANET-PITFALLS.md) §1）。
`FloatingOriginTests.RigidFollowHasNoSteadyStateErrorAtAnyTimeRate` 在 1 → 10¹⁰ m/s
六档速度下断言误差为 **0（逐位相等）**。

`DriftPenalty(format)` 把「原点漂了多远」换算成一个可读的倍数：它是漂移距离处的 float 步长
除以 1 m 处的步长。1.0 表示漂移免费，64 m 恰好是 2⁶ = 64 倍，2²⁰ m 是 2²⁰ 倍。
这个倍数**与格式无关**——它是「漂移吃掉了多少尾数位」。

---

## 3. Cube-Sphere 分块网格

`SurfaceChunkGrid`：六个面，每面 `resolution × resolution` 顶点，`maxLevel = 20`
（每面 2²⁰ = 1,048,576 块）。

### 3.1 接缝：索引是全局整数，不是每块的参数

顶点方向的符号距离用**全局整数格点索引**：

```
gx   = chunk.x * resolution + ix            // 该面内的全局整数索引
u    = (2 * gx − N) / N                     // N = cellsPerFace(level) * resolution
```

**相邻同级块对共享顶点算出的是同一个整数**，所以两边得到的 `u` 逐位相同，
不需要 ε、也不需要焊点。实测（L3，5,712 个共享顶点）：**最大偏差 0 ULP**。

### 3.2 跨面邻接：手工边表必错，所以不写边表

Cube Sphere 的经典错误是手写 24 条边的邻接表。六个面的 `u/v` 轴朝向两两不同，
手写表几乎不可能一次写对，而且写错了只表现为「某条缝上有裂缝」。

这里的做法是**翻过边界、在父面的 uv 方形里继续走一步**（`CubeFaceMath.DirectionToFace`），
让投影公式自己给出结果。实测（L2，六面全部 4×4 块，384 条边）：

| 检验 | 结果 |
|---|---|
| 每条边都找到邻居 | **384 / 384** |
| 共享点集**完全一致** | **384 匹配，0 不匹配** |
| 其中共享边被**反向**遍历 | 32 |
| 跨面解析（手工边表出错的地方） | 96 |

> 检验本身也有坑：按**索引**配对同一条边会把反向遍历误报成巨大裂缝，必须比**集合相等**。
> 工具里现在显式统计「反向遍历」的条数，把这个事实留在报告里而不是留在注释里
> （[PLANET-PITFALLS.md](PLANET-PITFALLS.md) §2）。

### 3.3 两级上限，必须分开看

| 上限 | 含义 | 地球上的值 |
|---|---|---|
| **精度上限** | 分块本地 float32 步长首次达到目标的层 | 1 m→L1，1 cm→**L7**，1 mm→L11，0.1 mm→L14 |
| **分辨率上限** | 网格间距首次达到目标的层 | 1 m→**L20**，1 cm→L26，1 mm→L30 |

分辨率上限远深于精度上限（L26 > maxLevel=20），因为网格间距 = 弧长 ÷ (2^L × resolution)，
而 float 步长只按 2^L 缩小。**真正决定「最小能分辨多大的地形特征」的是分辨率上限**，
它比「顶点够不够精确」苛刻得多。L20 在地球上是 **9.544 m 的块、59.65 cm 的网格间距、
953.7 nm 的 float32 步长**——比单位球表示细 **524,288 倍**。

---

## 4. LOD 选择：屏幕空间误差

一条规则，对 2,440 km 的水星和 696,340 km 的太阳同时成立：**块的几何误差投影到屏幕
不超过 `lodPixelTolerance` 像素时，就用这一层。**

```
LodDistanceMetres(L) = 几何误差(L) × 屏高 / (2 · tan(fov/2) · 像素容差)
几何误差(L)          = 最坏块边长(L) × reliefRatio
```

取「**满足容差的最粗一层**」，因为它是在满足画质前提下最便宜的一层。
取反会得到「越远越贵」且阶梯等于不存在的失效模式——回归测试把方向钉死
（[PLANET-PITFALLS.md](PLANET-PITFALLS.md) §1）。

`BuildLevelFunction` 再把邻块的「分裂压力」并进来：块的层取自己和周围 8 块所需层里最细的那个。
没有这一项，恰好跨过阈值的块会比旁边的块粗一级，叶节点规则又会把它拆开——
结果就是摄像机停在阈值上时每帧建了又拆。预分裂保证生成的树处处只差一级。

---

## 5. 流式加载：预算、最近优先、迟滞

`ChunkStreamer` 三条规则（顺序即优先级）：

1. **每帧硬预算**：`maxBuildsPerFrame` 与 `budgetMilliseconds` 双限。把相机丢到行星上会一次
   想要几千块，全建完是数秒卡死；预算把它变成「先粗后细，一秒内收敛」。
2. **最近优先**：请求按距离排序后再服务，所以**脚下的地面先清晰，地平线后清晰**。
   按请求顺序建会得到最糟的伪影：一块块精细的补丁，唯独相机正看的地方是糊的。
3. **逐出迟滞**：块掉出选择集后**不立刻销毁**，要等相机移动量**证明**它已经出了 keep 带。
   keep 带 = 该层 LOD 距离 × `keepFactor`。没有这一条，相机停在层边界上会每帧
   「建了拆、拆了建」，网格肉眼可见地闪。

因为块掉出选择集后就没人再算它的距离了，所以 `ResidentChunk` **记住**最后一次选中的距离，
用「相机自那以后的位移」作为它后退速度的上界来判断。这是唯一站得住的做法：不知道新距离，
但知道它最多远了这么多。

`maxResident` 是兜底：层数尖峰时按**距离从远到近**逐出。
`stats.pending` 的计数口径也有坑（只有**成功**的请求才推进游标），见
[PLANET-PITFALLS.md](PLANET-PITFALLS.md) §3。

> **这一层的 `ChunkStreamer` 目前没有运行时消费者**，见 §11。

---

## 6. 聚焦一颗行星时，其他东西怎么办

`SolarSystemSimulation.ApplyFarFieldPolicy` 按**投影像素**给每个天体分档，只切
`MeshRenderer`，**不动** `transform`、半径和已发布的 `RadiusUnits`——半径槽极小，
所以这是省 draw call，不是省填充率；壳层和取景算术因此完全不受影响。

以焦点 = 地球、相机在编辑器原点为例（实测）：

| 档位 | 判据 | 实测数量 | 谁 |
|---|---|---|---|
| Full | ≥ 64 px | **1** | 地球（相机就在它身上） |
| Proxy | ≥ 3 px | **2** | 太阳 7.522 px、月球 6.905 px |
| Marker | ≥ 0.15 px | **1** | 木星 0.161 px |
| Skipped | < 0.15 px | **17** | 水星 0.018 px … 海王星 0.008 px |

**没有藏起来的星球仍然参与计算**：位置、自转、光照、壳层、材质、HUD 数值全部照旧，
被关掉的只有那个画不出东西的 `MeshRenderer`。

### 6.1 轨道线：一次顶点都不用重算

`OrbitPath` 是**静态几何**，位置由整个 `orbitsRoot` 的偏移承担（即浮动原点的刚性跟随）。
**切焦点不重算任何一个顶点**，只挪一次根节点变换。

`cullOrbitsNearBody`（默认 false）可以在地表模式下隐藏轨道线，带 3/4 迟滞
（`camDist / focusRadius` 进 3 退 4），避免在阈值上反复开关。默认关闭是有意的：
轨道线正是「星球亚像素时你唯一能看见的东西」，进地表模式时它通常已经在画面外。

### 6.2 碎带：赢在求解节奏，不在剔除

7,200 个粒子每个都要解一次开普勒方程。**它们必须以亚像素的点出现**——那正是它们唯一能被
看见的方式，剔掉亚像素点等于删掉碎带。真正的开销是求解，所以优化落在**自适应节奏**上：

```
interval = angularStalenessDegrees / (maxMeanMotion × daysPerSecond)
```

| 时间倍率 | 求解间隔 | 120 帧内的矩阵构建数 |
|---|---|---|
| 每帧都解（旧行为） | — | 176,793 |
| 5 天/秒 | 0.006 s（短于一帧 → 仍每帧解） | 70,292 |
| 0.002 天/秒 | 约 15 s | 2,952 |
| 暂停 | ∞ | **1,476** |

也就是说在真正需要它的区间里，**2.047 → 约 0.03 ms/帧**。另外视锥外的标记从不提交
（实测约 **49.2%** 的环粒子在相机背后）。

节奏是**推导出来的，不是调出来的**：粒子的**方向**变化远慢于帧率，所以被限流的帧复用的
位置与重新求解的结果**逐位相同**——这才让「跳过求解」是合法的，而不只是一种近似。
`BeltTimeState.nowSeconds` 允许注入合成时钟，这让节奏成为可测的量而不是对真实帧率的观察。

---

## 7. 数值预算总表（全部实测）

| 量 | 值 | 来源 |
|---|---|---|
| 1 场景单位 | 10⁹ m（`kmPerUnit = 1e6`） | 场景设置 |
| float32 有效位 / 相对精度 | 24 bit / 1.192×10⁻⁷ | `SpacePrecision` |
| float64 有效位 / 相对精度 | 53 bit / 2.220×10⁻¹⁶ | `SpacePrecision` |
| 单位球地球的顶点步长 | **0.4657 m** | 实测 |
| L20 分块的 float32 步长 | **953.7 nm** | 实测 |
| L20 块边长 / 网格间距（地球） | **9.544 m / 59.65 cm** | 实测 |
| 1 m 特征在 float32 里的最大跨度 | 8,388.6 km | `SpanForFeature` |
| 1 cm 特征在 float64 里的最大跨度 | 301.05 AU | `SpanForFeature` |
| 最远可见天体的 float32 步长 | **488.3 km** | 实测（海王星轨道处） |
| 太阳系所需级数（10¹³ m ÷ 1 cm） | **1**（天体之上） | `LevelsRequired` |
| 石头→星系所需级数 | float32 **4** / float64 **2** | `LevelsRequired` |
| 框架与原浮动原点的位置差 | **0.000×10⁰ 场景单位**（21 天体全部一致） | 实测 |

最后一行是这次改造的验收条件：新框架**必须逐位复现**原有的浮动原点变换
（`OrbitalMechanics.ToUnity(positionKm − originKm, unitsPerKm)`），
否则它是一次重写，不是一次增补。

---

## 8. 代码地图

| 文件 | 内容 |
|---|---|
| `Space/SpacePrecision.cs` | `NumericFormat`、`PrecisionBudget`、`SpacePrecision`：`ExactUlp`（不调 `Math.Log`，在 2 的幂处精确）、`CanRepresent`、`SpanForFeature`、`LevelsRequired`、`ChunkLevelForPrecision` |
| `Space/SpaceFrame.cs` | `SpaceFrameLevel`、`SpaceFrame`、`RenderOrigin`（**唯一**把宇宙坐标降成 `Vector3` 的地方）、`SpaceFrameTree` |
| `Space/SurfaceChunkGrid.cs` | `CubeFaceMath`、`SurfaceChunkKey`、`SurfaceChunkGrid`（`maxLevel = 20`）、`ChunkLodSelector` |
| `Space/ChunkLocalGeometry.cs` | `ChunkLocalPoint`（**地址 + float 偏移**，不可拆开）、`ChunkGeometryBuffers`、顶点步长换算 |
| `Space/ChunkStreamer.cs` | `ChunkRequest`、`IChunkContentProvider`、`ChunkStreamStats`、`ChunkStreamer` |
| `Space/FloatingOrigin.cs` | `OriginRebaseMode`、`FloatingOrigin`（`Reset` / `Reanchor` / `Update` / `DriftPenalty`） |
| `Space/SolarSpaceContext.cs` | 把上面六件接到场景上的组件：架树、聚焦天体的架、地表分块架、远场分档 |
| `Assets/Editor/SpaceScaleAuditor.cs` | 只读审计（见 §9） |
| `Assets/Tests/EditMode/Solar/` | EditMode 回归测试 |

`ChunkLocalPoint` 把**块的地址**和**块内的 float 偏移**放在同一个值类型里，这不是风格问题：
两者分开存，调用方迟早会留下 float 而丢掉原点——那正是「换个焦点后网格跑到 6,000 km 之外」
的成因。

---

## 9. 验证

### 9.1 审计工具

菜单 **Tools ▸ Planet ▸ Audit Space Scale**（只读）。报告分七节写入
`Library/SpaceScaleAudit.txt`，同时分节打到 Console：
坐标阶梯 / 尾数天花板 / 远场策略 / 地表分块阶梯 / 分块网格不变量 / 碎带 / 轨道线。

> 用文件而不是 Console：MCP 的 `read_console` 会把每条日志截断在第一个换行处，
> 多行报告读不出来。

### 9.2 回归测试

| 文件 | 钉住的性质 |
|---|---|
| `SpacePrecisionTests` | `ExactUlp` 与硬件逐位一致、每级步长恰翻倍、地球 0.4657 m、级数计算 |
| `SpaceFrameTests` | 架树深度与父子、往返精确、**先减后降 vs 先降后减**、逐位复现旧变换 |
| `ChunkGridTests` | 384/384 邻接、集合相等而非索引配对、跨面解析、同级 0 ULP、四叉树覆盖无缝无叠、LOD 方向 |
| `ChunkStreamerTests` | 最近优先、时间预算、在途请求不越序、keep 带迟滞、驻留上限、收敛后零开销 |
| `FloatingOriginTests` | 六档速度下稳态误差为 0、`Reanchor` 是滑行不是剪切、残差单调衰减到 0、阈值模式 |
| `BeltCadenceTests` | 节奏推导、暂停=无穷、跳跃阈值、慢钟跨 15 s、快钟塌回每帧、轨道根数物理合理且确定 |

程序集 `Planet.Solar.Tests` 位于 `Assets/Tests/EditMode/Solar/`。
**Unity 不允许同一目录下有两个 asmdef**，所以它必须有自己的子目录。

---

## 10. 与程序化星球框架的边界

**本文档所属的代码不实现大气散射，也不实现地表采样。** 那些属于 `Planet.Procedural`
领地（见 [PLANET-PROCEDURAL.md](PLANET-PROCEDURAL.md)）。这一侧提供两样东西：

1. **`viewHeight` 的位置部分**，以及由它导出的两套比例。契约定义、常量来源与阈值表在
   [DEVELOPMENT-WORKFLOW.md](DEVELOPMENT-WORKFLOW.md) §3，那里是唯一权威版本。
   本层只负责让相机高程在「地表 ↔ 太空」之间**连续**变化：介质随之连续计算，
   因此不需要在某个高度切换场景。
2. **去掉位置突变的那套原点栈**：地表架用 `Threshold` 模式，所以「地表 / 太空」之间不存在
   坐标系切换的断点——只有同一个原点体系里连续变化的相机高程。

渲染侧的连续过渡由 `PlanetAtmosphere` 负责，本层不改它的着色器。

---

## 11. 已知限制

- **`maxLevel = 20`**：每面 2²⁰ 块是索引的架构上限。想在地球上分辨 1 cm 的**网格间距**
  需要 L26，那要换更深的索引（或分层索引）。当前上限对应地球 59.65 cm 网格间距。

- **这一层的地表分块还不是真正在渲染的那一套。** 本目录的 `SurfaceChunkGrid` +
  `ChunkStreamer` 是可审计、可测试的**策略实现**，但没有任何运行时路径构造 `ChunkStreamer`
  （连 `SolarSpaceContext` 也只驱动地表原点的 `Threshold` 跟随，不接 `IChunkContentProvider`）。
  正式场景里真正绘制分块地形的是 `Planet.Procedural.Terrain` 下另一套实现：
  `TerrainLattice`（32 格点 × 25 层）+ `RuntimeTerrainSelection` + `RuntimeTerrainRenderer`，
  由渲染器自己的 `LateUpdate` 驱动，并已被 `PlanetSurfaceView` 自动挂载
  （见 [PLANET-PROCEDURAL.md](PLANET-PROCEDURAL.md) §16）。
  **两套层级尚未统一**：层数定义、格点常数、坐标系与消费者都不同，
  改这一侧不会影响画面。

- **银河系架是装饰性的**：`solInGalaxyMetres` 默认为 0。它证明了阶梯能承载这一级，
  但目前没有数据源把它填上。
