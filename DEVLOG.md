# 开发日志

> 工程创建于 2026-10-02，本篇截止 **2026-10-04**。
> 极简骨架，供删改扩充；细节以各文档为准。

---

## 2026-10-02

- 采集 NASA 真实资产：地球 / 月球色与高程、Gaia DR2 星空
- 贴图管线：高程转 16-bit、法线与海洋遮罩派生（`_assets_src/`）
- 产出文档：`PLANET-ASSETS.md`

---

## 2026-10-03

### 上午 · 地月演示

- 初始化 Unity 2022.3 + URP 工程（Force Text / Linear / 新输入系统）
- `PlanetDemo.unity`：高程径向位移成球，地球 25× / 月球 8× 垂直夸张
- 星空天空盒接入；演示说明 `PLANET-DEMO.md`
- 入库：初始提交

### 下午 · 仓库与材质

- git 健康检查，修正 `.gitignore` / LFS 规则
- 重写 `PROJECT-OVERVIEW.md`
- 修复 `Earth_Surface` 的 `_Smoothness` 静默复位

### 晚间 · 太阳系基线

- `SolarSystem.unity`：太阳 → 柯伊伯带，真实比例（1 单位 = 1e6 km）
- 轨道按 JPL/Standish 开普勒根数推算；浮动原点 + 焦点跟随
- 白模渲染、轨道线、HUD、天体飞往
- 代码：`Solar/`、`Procedural/`、`Shaders/`、`Editor/`
- 文档：`PLANET-SOLARSYSTEM.md`，同步更新总览 / 资产 / 演示
- 提交：星系浏览产品基线（附最新文档）

---

## 2026-10-04

### 程序化星球框架

- `Procedural/`：`PlanetDefinition → PlanetSurfaceSampler → CubeSphere → PlanetSurfaceView`
  统一链路；海洋 / 大气 / 云 / 生态 / 冰帽可分别开关
- 地表 Albedo 与云密度烘焙为 Cubemap，指纹存在 importer 元数据里（完整 11.48 s / 命中 0.93 s）
- 大气与云壳层改读统一半径接口，1× 与 `G` 的 400× 下比例一致
- 星空曝光改由焦点星球测光驱动；修掉星图经度接缝的错误 mip 黑竖线
- 文档：新增 `PLANET-PROCEDURAL.md`；总览与太阳系 / 资产 / 演示文档同步定位与交叉引用

### 星系程序集划分

- 新增 **`Planet.Solar`** 独立程序集（`Assets/Scripts/Solar/Planet.Solar.asmdef`），
  星系运行时代码迁出 `Assembly-CSharp`；引用列表穷举为
  `Planet.Procedural` + `Unity.InputSystem` + URP Core / Universal Runtime 四项
- 依赖单向化：`Planet.Solar → Planet.Procedural`，反向无引用。`CelestialBody` 不再反向推送
  大气 / 云壳半径（壳层位于 `Planet.Procedural`，Solar 只发布半径接口），
  改为壳层自己经 `IPlanetRadiusProvider` 反查——壳层本来就有这条路径，行为不变
- 编辑器工具**故意不另立 asmdef**：多一个 asmdef 就多一个合并冲突点
- 验证：编译零错误零警告；场景 21 个 `CelestialBody` / 12 `OrbitPath` / 2 `BeltField` 引用全部完好
  （GUID 不变）；Play 模式实测 `planetRadius == RadiusUnits`、大气壳比 1.012557、云壳与地表半径一致；
  `PlanetLightingController` 跨程序集调用正常（EV −0.82）
- 文档：`PLANET-SOLARSYSTEM.md` 新增 §9「程序集边界与对外接口」；总览补程序集表、
  多工作树 MCP 实例端口表与两条 asmdef 风险项；`PLANET-PROCEDURAL.md` 同步
### 全部天体材质

- SolarSystem 的 21 个天体均接入定义／统一采样／Cube Sphere／独立 Albedo 缓存；Earth 保留 Terra 原资产
- 添加表面类型：岩壳、冰裂纹、硫磺火山、气巨星／冰巨星云顶、不透明雾霾、太阳发光光球
- 保留扁率与 Haumea 三轴形状；程序化表面继续构建环，补木星主尘环、天王星窄环、海王星主要环、Haumea 环
- 土星环径向光学透明度、环影和天体影；修正约 325 km 恩克缝、增加约 42 km 开勒缝
- 新菜单 Build Solar System Materials 分批重建所有天体；生成器版本升至 4，保留 GUID、已有手工定义参数和独立资产路径
- 材质观测解释、示意范围与框架缺口见新增 `PLANET-MATERIALS.md`；采样／接缝／形状／指纹回归测试见 `Assets/Tests/EditMode/`

### 星球缩放固化

- 星球半径不再每帧由 `SolarSystemSimulation` 从目录推算：`SolarSystemCatalog.RadiusKm ÷ kmPerUnit`
  改为在编辑器**烘焙一次**，存进 `CelestialBody.sceneRadiusUnits`（+ 出处 `bakedKmPerUnit`）
- `ApplyState(position, radiusUnits, spin)` → `ApplyState(position, spin)`；`RadiusUnits` 变成只读派生量，
  `FocusRadiusUnits` / `FrameDistanceFor` 改读星球自己发布的值
- 缩放写入只在**值变化**时发生（`G` 键或手改 Inspector），不再每帧写 21 次 `localScale`；
  `CelestialBody.Update` 负责拾取 Inspector 手改
- 新增菜单 `Bake Body Scales` / `Validate Body Scales`（`Assets/Editor/BodyScaleBaker.cs`，
  本分支 ownership；未改 planet-definition 的 `SolarSystemMaterialBuilder` / `PlanetFrameworkBuilder`）
- 场景改动是**纯新增** 42 行（21 × `sceneRadiusUnits` + 21 × `bakedKmPerUnit`），零删除，GUID 全保留
- 验证：21 个烘焙值对目录最大相对误差 4.5×10⁻⁸；1× 与 400× 下大气比仍为 **1.012557**、云比 **1.009**；
  EditMode **27/27** 通过
- **注意**：这**没有**加快初始化。实测半径换算 1000 帧 × 21 天体只需 **0.32 ms**（约 0.3 µs/帧）；
  启动耗时几乎全在程序化网格生成 —— Play 模式日志实测场景集成 **19.3 s**，
  因为 `PlanetSurfaceView` 的网格是 `HideFlags.DontSave`，每次进 Play 都重新生成。
  网格持久化属于 `planet-definition` 分支

### 宇宙尺度坐标阶梯与地表流式 LOD

- `Solar/Space/` 新增六个文件：`SpacePrecision` / `SpaceFrame` / `SurfaceChunkGrid` /
  `ChunkLocalGeometry` / `ChunkStreamer` / `FloatingOrigin`，加场景组件 `SolarSpaceContext`
- **两套坐标系**：宇宙侧 `double` 米存在四级架树（Galactic → Stellar → Planetary → Surface），
  地表侧 `float` 米相对**分块自己的原点**；唯一规则是「在 double 里减，最后一步才降到 float」。
  `RenderOrigin.ToRender` 是全局**唯一**的降 float 点
- **视场连续过渡**：地表架用 `Threshold` 模式（64 m），不存在坐标系切换断点；
  大气介质需要的 `viewHeight` 由既有 `surfaceClearanceRatio` 契约提供，着色器不属本分支
- **块接缝**：顶点方向用**全局整数格点索引**，同级相邻块逐位相同（L3 实测 0 ULP）；
  跨面邻接**不写 24 条边表**，改为翻过边界在父面 uv 方形里继续走一步（L2 实测 384/384）
- **LOD**：屏幕空间误差规则；`maxLevel = 20`（地球 9.544 m 块 / 59.65 cm 网格 / 953.7 nm 步长，
  比单位球表示细 524,288 倍）；`ChunkStreamer` 每帧硬预算 + 最近优先 + 逐出迟滞
- **远场策略**：按投影像素分 Full / Proxy / Marker / Skipped 四档，只切 `MeshRenderer`
  （焦点=地球时实测 1 全渲染 / 2 代理 / 1 标记 / 17 跳过）；轨道线静态，切焦点**不重算顶点**
- **碎带自适应节奏**：`interval = angularStalenessDegrees / (maxMeanMotion × daysPerSecond)`。
  实测 120 帧矩阵构建数 176,793（每帧解）→ 2,952（0.002 天/秒）→ **1,476（暂停）**，
  即相关区间 **2.047 → 约 0.03 ms/帧**；约 49.2% 的环粒子在相机背后，从不提交
- 新增只读审计菜单 **Tools ▸ Planet ▸ Audit Space Scale**（`Assets/Editor/SpaceScaleAuditor.cs`），
  报告七节写入 `Library/SpaceScaleAudit.txt`
- 新增测试程序集 **`Planet.Solar.Tests`**（`Assets/Tests/EditMode/Solar/`，**59/59 通过**）。
  必须放子目录：Unity 不允许同一目录下两个 asmdef
- **`Planet.Solar.asmdef` 一项引用都没加**（列表仍是穷举的四项）
- 验证：编译零错误零警告；新框架与原浮动原点变换**逐位一致**（21 天体，最大差 0.000×10⁰ 场景单位）
- 文档：新增 `PLANET-SPACE.md`；`PLANET-SOLARSYSTEM.md` §1 / §9 与总览同步

---

## 踩坑备忘

新增开发：Earth 四档纹理 LOD 由公开中心距／离地比例驱动，近景配法线与更精细云密度，
Resources 异步按档加载；Sun 迁移至 CelestialDefinition 派生 StellarDefinition 与独立光球 renderer，
Planck/CIE 温度 LUT、T⁴ 辐射、临边昏暗、动态米粒／黑子和较差自转，非太阳焦点使用纯色发光体。
曝光、镜头星芒、外层等的设计边界见 `PLANET-STELLAR.md`；工作流依据 `DEVELOPMENT-WORKFLOW.md`。
- 聚焦太阳观测优化：天空盒专用 EV 固定压到 -8，光球独立使用 -0.45 EV；暖红窄带响应、
  中频米粒增强和紧凑黑子遮罩参考用户提供的高动态范围太阳图，标准 4.5x 取景保持黑背景。
- 修复 Earth 双云层：关闭地表云影的可见叠加，只保留一个跟随 Earth 局部姿态的 CloudVolume，并禁用同名重复壳。
- 行星网格改为离线 Mesh LOD：20 个行星各生成 16／32／64／96 四档资产，运行时仅按焦点和视场高度切换，
  正式场景禁用 runtime CubeSphere fallback。
- 离线网格验证：20 个行星均指向 `MeshLod/*.asset`，运行时 `allowRuntimeMeshGeneration=0`；
  Earth 一活动云壳、地表云影关闭，Mesh LOD 和云层唯一性 Play 检查通过；42 项 EditMode 测试全部通过。

| 现象 | 结论 |
|---|---|
| 高程图在 Inspector 显示为红色 | R16 单通道正常，勿改 RGBA32 |
| 浮动原点用指数平滑 | 高倍时间流速下误差爆炸，改为刚性跟随 + 衰减残差 |
| Earth_Surface 光滑度丢失 | 属性被静默重置，需修材质与记录 |
| 编辑器后台不跑 | Run In Background 需手动开启 |
| 改云壳 `_InnerRadius` 没反应 | 着色器未使用该属性 |
| 改大气材质的 `_Density` / `_RimPower` 没反应 | 组件用 MaterialPropertyBlock 覆盖 |
| 改了采样公式但地形没变 | 需提升 `GeneratorVersion` 让内容指纹失效 |
| 重烘贴图后场景引用丢失 | 用 `CopySerialized` 覆盖旧资产，勿删除重建 |
| MCP 调用改的其实是另一个工作树 | 多实例各有端口，先查 `Application.dataPath` 再 `set_active_instance` |
| MCP 客户端会中途**换**实例 | 端口表只在启动时可靠；`execute_code` 里加一道 `Application.dataPath` 断言，并核对程序集 `asm.Location` 落在哪个工作树 |
| 星球半径与启动耗时无关 | 慢的是程序化网格生成（`HideFlags.DontSave`，每次 Play 重算），不是半径换算 |
| 新建 asmdef 后 Unity 说找不到 | 窗口失焦时资源库不自动刷新，显式 `AssetDatabase.Refresh` |
| asmdef 程序集不应直接驱动壳层 | `Planet.Solar` 只发布 `IPlanetRadiusProvider`；大气 / 云在 `Planet.Procedural` 中自行读取半径 |
| 同一目录放两个 asmdef | 直接报 `contains multiple assembly definition files`，测试程序集必须有自己的子目录 |
| LOD 取"满足容差的最细一层" | 几何误差随层数变小，判据单调，`break` 立刻触发 → 全距离都返回 L0；必须取**最粗**的合格层 |
| 接缝检验按索引配对两块的边 | 跨面时共享边反向遍历，会误报 ULP 级"裂缝"；必须比**点集** |
| Play 模式下改不了场景 | `MarkSceneDirty` / `SaveScene` 抛 "This cannot be used during play mode"；先 `manage_editor stop` |
| 审计报告里出现 1.0×10¹⁶ px | 相机与天体位置重合时 `2r/d` 爆炸；测不准的量要打印成"测不准"，不要放进表里 |

---

## 待补

- [ ] `ChunkStreamer` 接入帧循环：需要在 `Planet.Procedural` 侧实现一个按
      `SurfaceChunkKey` 生产 `ChunkGeometryBuffers` 的 `IChunkContentProvider`
- [ ] 用分块网格替换 `PlanetSurfaceView` 的单位球网格，并接上大气散射的高程连续过渡
- [ ] 银河系架 `solInGalaxyMetres` 的数据源（当前为 0，架构上已支持）
