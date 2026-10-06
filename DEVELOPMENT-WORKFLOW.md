# Planet 隔离开发与合并协议

本文件是各工作树共同遵守的工程协议，同时是**视场高度契约（§3）**与**跨程序集接口清单（§2）**
的唯一权威来源。其它文档提到这两件事时只给符号名或链接，不复述数字。

写作规则见 [DOCS.md](DOCS.md) §1–§2。

## 1. 权威分支

- `main` 是唯一的整合基线。任何工作树都从 `main` 的已提交点创建，不从另一条工作树复制文件。
- 工作树必须保持独立的 Unity 工程根目录和 MCP 实例。开始改动前先确认 `Application.dataPath`
  指向自己的工作树，再执行编辑器操作（[PLANET-PITFALLS.md](PLANET-PITFALLS.md) §8）。
- 每轮合并完成后，主线提交打上 `integration/authority-YYYY-MM-DD` 标签，该标签是本轮的
  权威母分叉点。工作树刷新后旧分支名保留，但分支指针统一指向这个提交。
- 新分支使用职责前缀：`planet-definition/`、`interaction/`、`solar-system/`。
  不得在功能分支里合并另一条功能分支。
- **分支名不保证稳定**（历史遗留过 `codex/`、`dsh/`、`mimo/` 等前缀）。合并一律按
  **提交哈希**执行，不要按分支名——工作树是活的，分支指针会在操作期间移动。

## 2. 工作树分区与程序集边界

三条长期并行的工作树，各自独占一个 Unity 工程根与 MCP 实例，只通过 `main` 交换提交：

| 工作树 | 长期 ownership |
|---|---|
| `planet-definition` | `Assets/Scripts/Procedural/`、程序化着色器、程序化构建菜单、`Assets/Resources/Solar/BodyMaterials/`、程序化 EditMode 测试 |
| `interaction` | `Assets/Scripts/Interaction/`（程序集 `Planet.Interaction`）：玩家输入、视距边界、近地状态机 |
| `SolarSystem` | `Assets/Scripts/Solar/`（含 `Space/` 尺度层）、星系目录与模拟、`SolarSystem.unity` 的结构性接线、尺度审计与 Solar EditMode 测试 |

**共享面是唯一的冲突来源。** 合并时的冲突点等于「被两侧同时改动的文件」，所以规则是
「**接口先定，实现后做**」：需要新数据源时先在 `main` 改本文件，再由调用方注入。
类型即使目前是 `public`，只要不在 §2.1 清单里，就视为程序集内部实现。

编辑器工具**故意不另立 asmdef**（`Assets/Editor/` 留在 `Assembly-CSharp-Editor`）：
多一个 asmdef 就多一个逐行冲突点。测试程序集则必须各占一个子目录——
Unity 不允许同一目录下存在两个 asmdef。

依赖方向单向：`Assembly-CSharp` → `Planet.Solar` → `Planet.Procedural`，反向禁止。两条硬约束：

- **`Planet.Solar` 不得引用 `Unity.Mathematics`**（引用即 `CS0012`）。要参考半径就调用
  `CubeFaceMath.ReferenceRadiusMetres`，不要绕过程序集边界去拿吃 `double3` 的版本；
- **`Planet.Solar.asmdef` 的 `references` 是穷举列表**，新增任何一条都会与其他工作树的同名
  asmdef 逐行冲突。

### 2.1 跨程序集接口清单

当前稳定的接口只有下面这些。

| 接口 | 方向 | 语义 |
|---|---|---|
| `IPlanetRadiusProvider.RadiusUnits` | `Planet.Solar` **发布** → 壳层订阅 | 只读派生量 = `sceneRadiusUnits × bodySizeExaggeration`。半径不再由模拟每帧推送（旧 `ApplyState(position, radiusUnits, spin)` 已简化为 `ApplyState(position, spin)`），星球自己持有缩放 |
| `PlanetSurfaceView.definition` / `BuildSurface(float)` / `SetRadius(float)` / `SurfaceTransform` | `Planet.Solar` → `Planet.Procedural` | 星系侧驱动表面的**全部**接触面 |
| `SolarSystemSimulation.FocusView` / `FocusRadiusUnits`、`SolarSystemCamera.CurrentViewHeightUnits` / `FocusSurfaceRadiusUnits` / `SurfaceClearanceUnits` / `SurfaceClearanceRatio` / `SurfaceClearanceOverView` | 只读查询 | 视场高度契约的取值入口（§3） |
| `SolarSystemCamera.handleZoomInput`、`ClampToSurfaceFloor` / `ClampCurrentDistanceToSurface` | 写 | 外部接管缩放与距离钳制 |
| **C1** `Planet.Procedural.Terrain.PlanetViewState` | 协调器每帧推送 | 双精度体固定（BF）米制视图状态 |
| **C2** `Planet.Procedural.Terrain.IPlanetSurfaceQuery` | 地形系统实现，经 `CelestialBody` 暴露 | 全频地表高程/半径查询 + `PendingDetailWork` 背压 |
| **C-ratio** `Planet.Procedural.Terrain.ViewHeightRatios` | 纯函数 | 两套命名比例，定义见 §3 |
| **C6 交互所有权** | `SolarSystemCamera` 发布开关 | `freeRoam`（true 时轨道 rig 不写 transform、不接轨道输入），由 `SurfaceRoamController` 接管 body-fixed 米制位姿。**裁面硬顶 near/far ≤ 1e5:1**，精确模式与轨道路径都必须过消杀 |
| **C6 精确地表高度** | 只读真值 | `HasPreciseSurfaceDistance` / `SetSurfaceClearanceMetres` / `TargetSurfaceClearanceMetres` / `CameraBodyFixedMetres`。与 `freeRoam` 为**或**关系释放 `BodyFixedCameraAnchor` |
| **交互层对外** | `Planet.Interaction` 发布 | `ViewDistanceBoundary`（纯函数：比例、速率、钳制、闸门判定、`ApplySurfaceClearanceZoomMetres`）、`SurfaceModeController.EnteredSurfaceMode` / `ExitedSurfaceMode` 事件与 `[SurfaceMode]` 日志口径。预订这些事件的资源预加载由 `planet-definition` 实现 |
| **显示位移** | `PlanetSurfaceSampler.DisplacementMetres(height)` | globe 与 chunk 的唯一入口。有海时围绕固定海平面夸张陆地，物理高度不乘倍率 |
| **缓存键** | 纯函数 | 全球贴图/网格用 `ContentFingerprint()`；含 `PlanetDetailSampler` 的区块缓存用 `TerrainContentFingerprint()`（`GeneratorVersion` + `detail`） |

新增接口时先在 `main` 修改本文件，再在职责分支实现。禁止通过复制字段、读取私有组件或
增加反向 asmdef 绕过接口。

### 2.2 尺度层接口（`SolarSystem` ownership）

`Assets/Scripts/Solar/Space/` 把「宇宙尺度」和「地表厘米级细节」拆成两套坐标系，细节见
[PLANET-SPACE.md](PLANET-SPACE.md)。它对其他工作树只开放四个接触点：

| 接触点 | 谁会用 | 语义 |
|---|---|---|
| `SolarSpaceContext.Origin` | 编辑器工具、调试视图 | 当前渲染原点。**唯一的降 float 点** |
| `SolarSpaceContext.Tree` | 编辑器工具 | 四级架树；换架是**减法**，永远不是缩放 |
| `SolarSpaceContext.SurfaceGrid` | 审计、未来 provider | 聚焦天体的分块网格；`SampleDirection` / `ChunkOriginInBodyMetres` / `ChunkSizeMetres` |
| `SpaceFrame.BudgetAt(position, format)` | 任何要判断「这里还够不够精确」的调用方 | 该点的 float32/64 步长与「能否表达某特征」 |

这一层**不实现大气散射，也不实现地表采样**：它只提供 `viewHeight` 的**位置**部分（§3）与
一套不产生坐标断点的原点栈；介质随高程的连续计算由 `Planet.Procedural` 的 `PlanetAtmosphere` 负责。

> **尺度层当前不是地表分块的唯一实现。** `Solar/Space/SurfaceChunkGrid` + `ChunkStreamer` 是
> 6 面 × 2^level（上限 20 层）的**审计与测试**用地图，`ChunkStreamer` 至今没有运行时消费者；
> 真正在渲染的是 `Planet.Procedural.Terrain` 里另一套 32 格点 × 25 层的格点阶梯，
> 由 `RuntimeTerrainRenderer` 在自己的帧循环里驱动，**不经过 `IChunkContentProvider`**。
> 两套层级尚未统一——不要以为改 `ChunkStreamer` 的参数会影响画面。见
> [PLANET-SPACE.md](PLANET-SPACE.md) §11 与 [PLANET-PROCEDURAL.md](PLANET-PROCEDURAL.md) §16。

## 3. 当前视场高度契约

三条工作树使用同一定义，单位统一为当前 Unity 场景单位：

```text
viewHeight            = 相机到当前焦点天体中心的距离
surfaceRadius         = SolarSystemSimulation.FocusRadiusUnits
surfaceClearance      = max(viewHeight - surfaceRadius, 0)

clearanceOverView     = surfaceClearance / max(viewHeight, 1e-12)    // 地表模式闸门
clearanceOverRadius   = surfaceClearance / max(surfaceRadius, 1e-6)  // 层级切换 / 资源预载滞回
```

两套比例**都只在行星外**（`viewHeight >= surfaceRadius`，相对地表正距离）参与进入判定；
行星内部 / 球心附近不得打开地表模式。渲染 LOD 连续性不依赖模式阈值。

| 用途 | 比例 | 进入 | 离开 | 状态 |
|---|---|---|---|---|
| 地表模式闸门（interaction） | `clearanceOverView` | ≤ **5%**（Earth ≈ 335 km 高度） | ≥ **8%** | **已实现**；2026-10-05 C6 放宽，保证闸门可达 |
| 层级切换 / 资源预载滞回 | `clearanceOverRadius` | ≤ 0.25 | ≥ 0.35 | **已实现**；由 Earth 纹理档位与 Mesh LOD 档位消费 |

**取值的唯一入口是 `SolarSystemCamera`**：`SurfaceClearanceOverView` 与 `SurfaceClearanceRatio`
（后者即 `clearanceOverRadius`）由相机计算，消费方不得自己重算。

两处常量各自只有一处定义：

- `Planet.Interaction.ViewDistanceBoundary.DefaultSurfaceModeEnterRatio` / `DefaultSurfaceModeExitRatio`
  （`0.05f`、`0.08f`），由 `SurfaceModeController` 使用；历史标定 `100/6562` 只作注释；
- `Planet.Procedural.Terrain.ViewHeightRatios.ClearanceOverRadius`：命名与公式的权威定义；
  层级的**具体阈值**保存在可编辑的 LOD Set 资产里（Earth 纹理档位、Mesh LOD 档位），
  与 §3 的表同值，改表必须同时改资产。

`viewHeight` 不是大气半径、云层半径或网格顶点到相机的距离。浮动原点把焦点天体放在世界原点，
因此当前相机轨道距离就是唯一的中心距来源。未来若相机改为局部地表坐标，必须继续对外提供
等价的中心距和半径，不能让下游重新猜测。

> **这套契约同时承担「消除地表 / 太空切换断点」的职责。** 大气散射介质随相机高程**连续**计算，
> 读的就是 `viewHeight`；位置层面的断点由 `Space/` 的原点栈消除——地表架用 `Threshold` 模式，
> 所以不存在「切换到另一套坐标系」的那一刻，只有同一个原点体系里连续变化的相机高程。
> 两条路径都**不产生**需要被补偿的跳变，因此介质可以一直算下去而不必分场景。

### 3.1 交互输入边界（已实现）

`Assets/Scripts/Interaction/`（程序集 `Planet.Interaction`）按下面的顺序做，**实测顺序不可调换**：

1. 目标距离和当前距离都钳制为 `viewHeight >= surfaceRadius`，允许等于半径，不允许进入实体地表
   （`ViewDistanceBoundary.ClampToSurface` / `SolarSystemCamera.ClampCurrentDistanceToSurface`）。
2. 缩放使用对数距离；靠近地表只降低距离变化率，不改变距离单位。
   `ViewDistanceBoundary.ZoomRateMultiplier` 用 smoothstep 在 `nearRate 0.08 → farRate 1.0`
   之间过渡，远端拉远不阻尼。近地后的对数高程缩放 `h' = h · exp(δ · rate)` 见 `ApplyLogZoom`（C4）。
3. 每帧顺序固定为「读取半径 → 处理输入 → 限制目标距离 → 平滑到当前距离 → 发布事件」。
   **禁止**先移动相机再用碰撞体回弹；那会在浮动原点和极小天体上产生抖动。

`ViewDistanceController` 通过 `SolarSystemCamera.handleZoomInput` 接管缩放输入，退出时还原；
`BodyFixedCameraAnchor`（C5）在进入近地带后把轨道 yaw 表达在焦点天体的旋转系里，
使地面不再从玩家脚下滑走。

`IPlanetSurfaceQuery.PendingDetailWork` 背压降速**尚未接入**。

### 3.2 近地转场状态机

已实现的是**判定与事件**，不是模式切换：

| 状态迁移 | 条件 | 当前行为 | 未来行为 |
|---|---|---|---|
| `SystemView` → `SurfaceMode` | `clearanceOverView <= DefaultSurfaceModeEnterRatio` 且 `viewHeight >= surfaceRadius` | 发 `SurfaceModeController.EnteredSurfaceMode` + `[SurfaceMode] ENTER` 日志 | 预加载地表 LOD、植被和碰撞资源 |
| `SurfaceMode` → `SystemView` | `clearanceOverView >= DefaultSurfaceModeExitRatio` | 发 `ExitedSurfaceMode` + `[SurfaceMode] EXIT` 日志 | 卸载近地专属资源并恢复星系层预算 |

阈值只有 §3 那一处定义，本表给符号名不给数字。

事件**只表达「是否应加载星球表面细节」**，不等于 mode 已经切换，也不接管相机。
行星内部与球心附近不触发（`ViewHeightRatios.IsOutsideSurface` 守住入口）。

未来进入近地 mode 时，计划切换为以地表 down direction 为基准的玩家相对坐标、降低时间流速，
并使用右键 + `QWEASD` 漫游。退出 mode 必须经过同一个滞回阈值，且由状态机统一发布，
不能由滚轮、相机脚本和资源加载器各自判断。

## 4. SolarSystem 数据规则

`SolarSystemCatalog` 是物理半径、质量、轨道和环的**唯一真相来源**；`SolarSystemBuilder`、
`SolarSystemMaterialBuilder` 只能生成或接线派生资产，不能成为运行时真相。

### 4.1 星球缩放：目录是真相，星球持有派生缓存

星球的场景缩放**固化在星球自己身上**，不每帧从目录推算：

```text
SolarSystemCatalog.RadiusKm  ÷  SolarSystemSimulation.kmPerUnit
        ↓  （只在编辑器烘焙时执行一次）
CelestialBody.sceneRadiusUnits        // 序列化在星球的组件上，Inspector 可读可改
CelestialBody.bakedKmPerUnit          // 出处：烘焙时用的 kmPerUnit，只用于漂移检测
        ↓  （运行时只做一次乘法，'G' 键切换）
CelestialBody.RadiusUnits = sceneRadiusUnits × bodySizeExaggeration
```

这条规则**没有**推翻「不得写死第二份常数」的本意：它把缩放定义成**带出处的派生缓存**，
和 Albedo / 云密度 Cubemap 的 `ContentFingerprint()` 是同一个模式：

- 物理半径仍然只有 `SolarSystemCatalog` 一份，星球上存的是已换算成场景单位的**结果**；
- `bakedKmPerUnit` 记下这笔换算是在什么比例下做的，所以漂移是**可检测**的，不是静默的：
  `CelestialBody` 在运行时对不上就发警告，`Tools ▸ Planet ▸ Validate Body Scales` 可以离线审计；
- 只有编辑器烘焙会写这两个字段（`CelestialBody.BakeScale` + `BodyScaleBaker`），运行时只读。

因此**新增天体或修改比例时**按以下顺序：

1. 先改目录数据和对应的外观预设/定义；
2. 再运行 `Tools ▸ Planet ▸ Bake Body Scales` 重算全部星球的缩放（`Rebuild Solar System Scene`
   会在建场景时自动烘焙；`Build Solar System Materials` **不会**，它只碰定义和材质）；
3. 检查场景引用、GUID、`RadiusUnits`、`Validate Body Scales` 的报告和文档；
4. 最后提交生成结果与源数据同一提交。

后续移除一键生成式场景构建器时，保留菜单作为迁移工具，直到场景序列化数据和验证脚本能够
独立重建；不得在一次功能提交中同时改变目录尺度、坐标系和场景生成机制。

## 5. 分支提交规则

- 一个提交只表达一个职责边界内的可回滚变化；生成资源、`.meta` 和源定义必须同提交提交。
- 改采样数学、噪声、材质解释或默认参数时，必须更新 `PlanetDefinition.GeneratorVersion` 或
  `ContentFingerprint()` 的输入，并附上验证结果。
- 改程序集引用、公开接口、场景对象层级或资源 GUID 时，提交说明中写出影响的工作树和迁移方式。
- 调试输出允许进入 `interaction` 的工作提交，但必须带统一事件名和可关闭开关；不能把
  一次性截图探针、临时材质、临时场景或 Unity `Library/` 带入提交。
- 长期回归测试放在 `Assets/Tests/EditMode/`；一次性验证脚本和模板遗留脚本不进入该目录。
- **审计工具的报告写入 `Library/`**（已被 `.gitignore` 排除），不要把生成的报告提交进来；
  审计工具本身与它的 `.meta` 属于长期资产，要入库。
- **MeshLod 类派生资源的搬迁必须以 rename 入库**（GUID 100% 保留）；删除+新增两套路径会断场景引用。

## 6. 合并与刷新流程

每次整合按以下顺序执行，顺序改变时要在主线提交说明中解释原因：

1. 各工作树确认 `git status --short` 只有自己的职责改动，运行 `git diff --check`，提交并记录提交哈希。
2. 在主工作区为各分支 tip 建立 `integration/pre-*` 备份标签。
3. 依次合并 `SolarSystem`（程序集和半径契约）、`interaction`（命名空间/输入边界）、
   `planet-definition`（资源和程序化实现）。冲突只按 ownership 和本文件解决，不用整文件覆盖。
4. 主线运行静态引用检查、EditMode 测试和 Unity 编译/场景验证；修复只在主线提交。
5. 主线提交通过后打 `integration/authority-YYYY-MM-DD` 标签，记录 `git rev-parse HEAD`。
6. 切到每个工作树，确认无未提交改动后，将对应分支快进或重置到权威提交；不要在刷新时运行
   `git clean -fdx`，除非已经确认工作树中没有需要保留的本地资产。

合并完成后产生的最终标签才是可供下一轮分叉的母点；`integration/pre-*` 标签只用于审计和回滚。

**三条反复踩到的流程经验**（现象与恢复方式见 [PLANET-PITFALLS.md](PLANET-PITFALLS.md) §8）：

1. **合并前先关掉 Unity 里打开的待合并场景。** 磁盘上的 `.unity` 被改写会触发模态框并卡死 MCP 桥接。
2. **按哈希合并，不要按分支名。** 工作树在调研途中会前进，按分支名操作会合到未预期的提交上。
3. **`.meta` 缺失会在合并时变成硬冲突。** 比对两侧 GUID，确认自生成的那份没有任何引用后删除，
   让工程提交版落地。

> 会话层禁止 `git merge` 时，可用**按哈希 cherry-pick** 完成等价整合。
> 双相机所有权（`freeRoam` 管输入地图，`HasPreciseSurfaceDistance` 管厘米真值）**必须显式合成**，
> 不要指望文本自动合并正确；精确模式的裁面也要过 1e5:1 消杀。

## 7. 合并验收清单

- 程序集依赖方向无环（清单见 §2），`Planet.Solar` 不引用 `Assembly-CSharp`，也不引用 `Unity.Mathematics`；
- `PlanetAtmosphere`、`PlanetCloudLayer` 和 `CelestialBody` 都通过半径接口协作；
- `viewHeight` / `clearanceOverView` 的口径与 §3 的常量来源一致，且阈值数字**只有 §3 那一处**；
- 尺度层没有引入第二处降 float 点：`RenderOrigin.ToRender` 仍是从宇宙坐标到 `Vector3` 的**唯一**出口；
- `Planet.Solar.asmdef` 的 `references` 列表没有被改动（仍是穷举的四项）；
- 生成资产的 GUID、场景对象和 Build Settings 引用未丢失；合并后跑一次
  `Tools ▸ Planet ▸ Validate Body Scales` 与一次全量 `Audit Space Scale`；
- 程序化 EditMode 回归测试存在且只覆盖可长期复用的确定性/接缝/形状/光学契约；
  每个测试程序集独占一个子目录；
- `Assets/TutorialInfo` 等模板遗留物、临时测试脚本和中间截图不进入权威提交；
- 每个工作树的分支指针与 `main` 的权威标签相同；除明确保留的本地验证截图外，
  工作区没有未提交的代码或源资源改动。

## 8. 文档维护协议

文档和代码一样有 ownership，同样会冲突。**写作规则与分工表在 [DOCS.md](DOCS.md) §1–§4**，
本节只定流程：

1. **一件事只有一个地方写。** 发现第二份拷贝就是 bug，删掉拷贝而不是同步两份。
2. **文件名的生命周期含义**：
   - `PLANET-*.md` / `DEVLOG.md` / `DEVELOPMENT-WORKFLOW.md` / `PROJECT-OVERVIEW.md` / `DOCS.md`：
     长期文档，随代码一起演进；
   - `REPORT-<branch>-<topic>.md`：**分支交接单**，内容是「这个分支还需要改什么」。
     它随该分支的合并而**删除**，不留在权威提交里；
   - `MERGE-*`：一次性合并记录，结论并入本文件 §6 与 [DEVLOG.md](DEVLOG.md) 后删除。
3. **合并类操作不得把过程性文档带进仓库。** 工作树自己的 `MERGE-REPORT-*.md` 属于工作树产物，
   合并时按第 2 条处理。
4. 新增文档必须同时在 [DOCS.md](DOCS.md) §3 登记职责、§4 登记它新拥有的主题，否则视为孤儿文档。
   ——`PLANET-TEXTURE-LOD.md` 曾长期游离在索引之外，是这条规则的反面教材。
5. 文档里的**数字与路径必须可核对**。写不进代码位置的常量就不要写数字，改为指向符号名。
