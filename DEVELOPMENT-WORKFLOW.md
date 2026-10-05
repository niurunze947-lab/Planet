# Planet 隔离开发与合并协议

本文件是三条工作树共同遵守的工程协议。它描述的是代码边界、数据契约和合并顺序；
具体实现仍以源码、程序集定义和 `PROJECT-OVERVIEW.md` 为准。

## 1. 权威分支

- `main` 是唯一的整合基线。任何工作树都从 `main` 的已提交点创建，不从另一条工作树复制文件。
- 工作树必须保持独立的 Unity 工程根目录和 MCP 实例。开始改动前先确认
  `Application.dataPath` 指向自己的工作树，再执行编辑器操作。
- 本轮合并完成后，主线提交会打上 `integration/authority-2026-10-04` 标签；该标签是本轮的
  权威母分叉点。工作树刷新后，三条旧分支名仍保留，但分支指针统一指向这个提交。
- 新分支使用职责前缀：`codex/planet-definition/<topic>`、`codex/interaction/<topic>`、
  `codex/solar-system/<topic>`。不得在功能分支里合并另一条功能分支。

## 2. 程序集和目录 ownership

依赖方向必须保持单向：`Assembly-CSharp` / `Assembly-CSharp-Editor` 可以调用
`Planet.Solar`，`Planet.Solar` 可以调用 `Planet.Procedural`，反向依赖禁止。

| 工作树 | 长期 ownership | 可以修改的共享面 | 禁止直接修改 |
|---|---|---|---|
| `planet-definition` | `Assets/Scripts/Procedural/`、程序化着色器、`Assets/Editor/PlanetFrameworkBuilder.cs`、`PlanetSurfaceBaker.cs`、`SolarSystemMaterialBuilder.cs`、`Assets/Resources/Solar/BodyMaterials/`、程序化 EditMode 回归测试 | `PlanetSurfaceView` 与 `IPlanetRadiusProvider` 的兼容扩展；资源 GUID 只能保留，不能删除重建 | `SolarSystemCamera` 输入、轨道方程、场景焦点和交互状态机 |
| `interaction` | 新建的 `Assets/Scripts/Interaction/` 及其程序集；玩家输入、视距边界、近地 debug 事件和未来的 mode 切换 | `SolarSystemCamera` 的只读视距查询/设置接口；事件名和阈值表 | 程序化采样公式、材质烘焙、SolarSystem 目录数据 |
| `SolarSystem` | `Assets/Scripts/Solar/`、星系目录/模拟/浮动原点、`SolarSystem.unity` 的结构性接线 | `CelestialBody` 的半径发布接口、相机查询接口、场景构建菜单的兼容改动 | 交互输入算法、地表采样和材质缓存内部实现 |

当前稳定的跨程序集接口只有：

- `IPlanetRadiusProvider.RadiusUnits`：`CelestialBody` 发布当前场景半径，壳层订阅；
- `PlanetSurfaceView.definition`、`BuildSurface(float)`、`SetRadius(float)`、`SurfaceTransform`；
- `SolarSystemSimulation.FocusView` / `FocusRadiusUnits` 和 `SolarSystemCamera.CurrentViewHeightUnits`、
  `FocusSurfaceRadiusUnits`、`SurfaceClearanceUnits`、`SurfaceClearanceRatio`；
- 程序化资源的 `ContentFingerprint()`。它是缓存失效键，不是业务状态存储。
- **C1** `Planet.Procedural.Terrain.PlanetViewState`：双精度体固定（BF）米制视图状态，协调器每帧推送；
- **C2** `Planet.Procedural.Terrain.IPlanetSurfaceQuery`：全频地表高程/半径查询 + `PendingDetailWork`
  背压，经 `CelestialBody` 暴露，由地形系统实现；
- **C-ratio** `Planet.Procedural.Terrain.ViewHeightRatios`：两套命名比例，见 §3。

新增接口时先在 `main` 修改本文件和对应程序集文档，再在职责分支实现。禁止通过复制字段、
读取私有组件或增加反向 asmdef 绕过接口。

## 3. 当前视场高度契约

三条工作树使用同一个定义，单位统一为当前 Unity 场景单位：

```text
viewHeight            = 相机到当前焦点天体中心的距离
surfaceRadius         = SolarSystemSimulation.FocusRadiusUnits
surfaceClearance      = max(viewHeight - surfaceRadius, 0)

clearanceOverView     = surfaceClearance / max(viewHeight, 1e-12)    // 地表模式闸门
clearanceOverRadius   = surfaceClearance / max(surfaceRadius, 1e-6)  // 近地预载/调试滞回
```

两套比例**都只在行星外**（`viewHeight >= surfaceRadius`，相对地表正距离）参与进入判定；
行星内部/球心附近不得打开地表模式。渲染 LOD 连续性不依赖模式阈值。

| 用途 | 比例 | 进入 | 离开 |
|---|---|---|---|
| 地表模式（interaction） | `clearanceOverView` | ≤ 1.524%（地球 100 km / 6562 km） | ≥ 3.0% |
| 近地资源预载 debug | `clearanceOverRadius` | ≤ 0.25 | ≥ 0.35 |

`viewHeight` 不是大气半径、云层半径或网格顶点到相机的距离。浮动原点把焦点天体放在世界原点，
因此当前相机轨道距离就是唯一的中心距来源。未来若相机改为局部地表坐标，必须继续对外提供
等价的中心距和半径，不能让下游重新猜测。

### 3.1 交互输入边界

交互层必须在应用缩放输入后再施加边界：

1. 目标距离和当前距离都要钳制为 `viewHeight >= surfaceRadius`，允许等于半径，不允许进入实体地表。
2. 缩放使用对数距离；靠近地表时只降低距离变化率，不改变距离单位。推荐初始参数为：
   远距速率倍率 `1.0`、近地速率倍率 `0.08`、过渡区间 `clearanceOverView 0..1`，
   并用平滑函数避免在阈值处跳变。近地后改为对数高程缩放 `h' = h · exp(δ · rate)`，
   并接受 `IPlanetSurfaceQuery.PendingDetailWork` 背压降速。
3. 每帧的边界顺序固定为「读取半径 → 处理输入 → 限制目标距离 → 平滑到当前距离 → 发布事件」。
   禁止先移动相机再用碰撞体回弹；那会在浮动原点和极小天体上产生抖动。

这部分在 `interaction` 工作树暂不实现真实输入改动；本轮只建立 debug 触发和日志口径，
不加载地表资源、不切换坐标系、不改变时间流速。

### 3.2 近地转场 debug 契约

先采用带滞回的比例阈值，避免在边界来回抖动：

| 状态 | 条件 | 本轮行为 | 未来行为 |
|---|---|---|---|
| `SystemView` → `SurfaceDetailPending` | `clearanceOverRadius <= 0.25` | 记录一次 debug 事件，包含天体名、`viewHeight`、半径和比例 | 预加载地表 LOD、植被和碰撞资源 |
| `SurfaceDetailPending` → `SystemView` | `clearanceOverRadius >= 0.35` | 记录一次离开事件 | 卸载近地专属资源并恢复星系层预算 |

事件只表达「是否应加载星球表面细节」，不等于 mode 已经切换。事件名和字段先固定，
实际资源加载由 `planet-definition` 后续实现；这样 interaction 可以先接入日志和测试，
而不会把两套坐标系或场景生命周期提前耦合。

未来进入近地 mode 时，计划切换为以地表 down direction 为基准的玩家相对坐标，降低时间流速，
并使用右键 + `QWEASD` 漫游。退出 mode 必须经过同一个滞回阈值，且由状态机统一发布，
不能由滚轮、相机脚本和资源加载器各自判断。

## 4. SolarSystem 数据规则

`SolarSystemCatalog` 是物理半径、质量、轨道和环的来源；程序运行时不得把星球缩放值写死为
另一份常数。`SolarSystemBuilder`、`SolarSystemMaterialBuilder` 只能生成或接线派生资产，
不能成为运行时真相。新增天体或修改比例时按以下顺序：

1. 先改目录数据和对应的外观预设/定义；
2. 再运行编辑器菜单生成场景和缓存；
3. 检查场景引用、GUID、`RadiusUnits` 和文档；
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

本轮采用的实际合并提交为 `4325161`（SolarSystem 边界）和 `116aa34`（程序化定义与材质）。
协议提交完成后产生的最终标签才是可供下一轮分叉的母点；之前的三个 `integration/pre-*` 标签
只用于审计和回滚。

## 7. 合并验收清单

- 三个程序集的依赖方向无环，`Planet.Solar` 不引用 `Assembly-CSharp`；
- `PlanetAtmosphere`、`PlanetCloudLayer` 和 `CelestialBody` 都通过半径接口协作；
- `viewHeight`、`surfaceClearanceRatio` 和 0.25/0.35 滞回阈值在 debug 日志中口径一致；
- 生成资产的 GUID、场景对象和 Build Settings 引用未丢失；
- 程序化 EditMode 回归测试存在且只覆盖可长期复用的确定性/接缝/形状/光学契约；
- `Assets/TutorialInfo` 等模板遗留物、临时测试脚本和中间截图不进入权威提交；
- 每个工作树的分支指针与 `main` 的权威标签相同；除明确保留的本地验证截图外，工作区没有未提交的代码或源资源改动。
