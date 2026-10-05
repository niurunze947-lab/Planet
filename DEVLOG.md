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

---

## 踩坑备忘

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

---

## 待补

- [ ] 
