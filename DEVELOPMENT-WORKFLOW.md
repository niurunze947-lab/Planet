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
| 观察者状态 | `Planet.Interaction` → `Planet.Solar` | `SetObserverWorldMetres` / `SetObserverBodyFrame` 提交双精度米制状态；`ObserverRelativeToBody` / `ObserverVelocityInBody` 只读投影，焦点与近邻分离 |
| `IPlanetCollisionSurfaceQuery` | `Planet.Procedural` → `Planet.Interaction` | 显示三角形半径、碰撞覆盖就绪、局部米制球扫掠；物理采样仍走 C2 |
| `PlanetSurfaceView.SetTerrainState` / `RuntimeTerrainRenderer.UpdateView` / `SetCollisionObserver` | 协调器 → 地形 | 预热/显示、精确观察位置与速度、碰撞覆盖需求，均由实际玩家位置决定 |
| **C-ratio** `Planet.Procedural.Terrain.ViewHeightRatios` | 纯函数 | 两套命名比例，定义见 §3 |
| **旧 C6 交互所有权（兼容工具路径，正式运行时已由 §3 替代）** | `SolarSystemCamera` 发布开关 | `freeRoam`（true 时轨道 rig 不写 transform、不接轨道输入），由 `SurfaceRoamController` 接管 body-fixed 米制位姿。**裁面硬顶 near/far ≤ 1e5:1**，精确模式与轨道路径都必须过消杀 |
| **旧 C6 精确地表高度（兼容工具路径）** | 只读真值 | `HasPreciseSurfaceDistance` / `SetSurfaceClearanceMetres` / `TargetSurfaceClearanceMetres` / `CameraBodyFixedMetres`。与 `freeRoam` 为**或**关系释放 `BodyFixedCameraAnchor` |
| **旧交互层对外（保留测试与兼容调用，正式运行时见 §3）** | `Planet.Interaction` 发布 | `ViewDistanceBoundary`（纯函数：比例、速率、钳制、闸门判定、`ApplySurfaceClearanceZoomMetres`）、`SurfaceModeController.EnteredSurfaceMode` / `ExitedSurfaceMode` 事件与 `[SurfaceMode]` 日志口径。预订这些事件的资源预加载由 `planet-definition` 实现 |
| **显示位移** | `PlanetSurfaceSampler.DisplacementMetres(height)` | globe 与 chunk 的唯一入口。有海时围绕固定海平面夸张陆地，物理高度不乘倍率 |
| **缓存键** | 纯函数 | 全球贴图/网格用 `ContentFingerprint()`；含 `PlanetDetailSampler` 的区块缓存用 `TerrainContentFingerprint()`（细节生成器版本 + 区块 `GeometryVersion` + `detail`） |

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

## 3. 统一观察者与近地契约

`UnifiedFlightController` 是运行时唯一的玩家运动所有者。自由飞行贯穿深空与地表；
聚焦是临时导航请求，近地是坐标参考系、流送和碰撞状态，不再交接给另一套相机。
`SolarSystemCamera.externalObserver` 启用时停止轨道输入、距离积分与 Transform 写入，
保留只读兼容读数。旧的 ViewDistance/SurfaceRoam/BodyFixed 控制器不再由启动器挂载。

### 3.1 位置、速度与帧顺序

玩家持有双精度米制位置与速度。深空使用 Unity 轴向的恒星系；近地使用体固定米制，
进入时转换已有位置、朝向和速度，离开时转换回去，均不清零或重新对准球心。
靠近天体时可跟随其公转平移，该参考系由实际距离选择，与导航目标独立；
自转跟随仅在近地体固定帧启用。运动按真实帧秒积分，天体公转和自转仍按模拟日期计算。

顺序为：模拟轨道与姿态 → 玩家输入/运动/碰撞 → 提交观察者原点并重投影天体 →
写相机 → 观察协调器 → 地形选择、上传和碰撞准备。物理查询读取上一帧已完成的覆盖，
缺失部分使用同源地形的保守屏障。double 域完成减法与旋转，最后才降 float；
禁止通过 Camera Transform 回读玩家真值。

`FocusView` 只表示 HUD/导航选中的天体；`ProximityView` 表示实际靠近的天体。
`SetObserverWorldMetres` 提交渲染原点，`ObserverRelativeToBody` 向消费者提供精确体固定观察位置，
`SetObserverBodyFrame` 保留近地局部真值以避免恒星坐标往返。

### 3.2 距离判据与输入

```text
viewHeight = 玩家到所观察天体中心的实际距离
clearanceOverView = (viewHeight - referenceRadius) / viewHeight
clearanceOverRadius = (viewHeight - referenceRadius) / referenceRadius
```

`FlightMotion` / `ViewDistanceBoundary` 的近地闸门为进入 5%、离开 8%。只用实际位置，
不看目标距离；进入还要求位于显示地表之外，允许真实谷地或扁球表面低于平均半径。
近地内上述距离仅供状态、LOD 和 HUD 查询，不能回写限位。实体接触由地形碰撞决定。

右键环视及 QWEASD 自由飞行始终可用。数字键/Tab/F 发起临时聚焦，中心距以对数曲线
变化，限制星球在画面里的相对增长率；手动移动、左键环绕和滚轮推进均可中断聚焦。
选中目标后左键围绕目标选择落点，仍由同一运动所有者更新位置，近地环绕受速度和碰撞约束。
Esc 取消导航目标。滚轮及 PgUp/PgDn（Z/X）始终推进/后退，有目标时沿目标方向，无目标时
沿视线方向，右键不改变其含义。小键盘 + / - 将速度倍率乘 2 / 除 2，默认 1，范围
1/32～128；主键盘 - / = 仍调整模拟时间。倍率统一作用于键盘飞行、滚轮和离地速度包络，
不直接重置速度，也不缩放引力。所有手动运动共用连续离地速度包络，跨阈值保留速度和滚轮惯性，
地面附近仍保留有限可用速度，不能形成永远接近不到地面的阻尼。

重力默认关闭，B 或 HUD 复选框切换。开启后读取附近天体的定义质量，以真实秒计算
`a = -G M r / |r|³`；重力产生的速度独立累积，不受飞行松手减速、模拟日期倍率或
手动速度包络抵消。开关切换不清零总速度，碰撞仍去掉内向分量。当前使用局部天体的
球对称中心引力，无大气阻力、行走或多天体 N-body 动力学。

### 3.3 渲染、流送与碰撞

远景使用固定全球网格和贴图 LOD，不常驻运行时地形。协调器按实际相对距离预热，
完整根覆盖到齐后才切换近地可见区块。实际运行时索引为 `TerrainLattice`；
`Solar/Space/ChunkStreamer` 仍是独立策略测试实现，不能用于配置正式地形。

`IPlanetSurfaceQuery` 保留未夸张物理高程；`IPlanetCollisionSurfaceQuery` 查询显示几何、
碰撞覆盖状态和连续球扫掠。碰撞顶点与当前区块相同，三角形排除遮缝裙边；
父区块保留直到四个子碰撞网格准备好。碰撞代理位于独立米制 PhysicsScene，
局部原点随玩家重定。非固体外观不创建地形碰撞。

`ObserverRenderRig` 的米制 Overlay 相机只绘制局部地形代理（保留层 31），
与远景相机共享观察者位置和方向，解决太阳系场景单位下近裁面吞掉贴地地形的问题。
400 倍放大只用于远景定位，接近时恢复真实比例，近地不接受重新放大。

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
