# 开发日志

> 工程创建于 2026-10-02。
> 本文档是**唯一**的历史线：只记「哪一天发生了什么、为什么这么定」。
> 当前状态、契约、参数在 [DOCS.md](DOCS.md) 列出的各 owner 文档；踩坑结论在
> [PLANET-PITFALLS.md](PLANET-PITFALLS.md)。本文档一律用链接指过去，不复述。

---

## 2026-10-02

- 采集 NASA 真实资产：地球 / 月球色与高程、Gaia DR2 星空
- 贴图管线：高程转 16-bit、法线与海洋遮罩派生（`_assets_src/`）
- 产出文档：`PLANET-ASSETS.md`

---

## 2026-10-03

### 上午 · 地月演示

- 初始化 Unity 2022.3 + URP 工程（Force Text / Linear / 新输入系统）
- `PlanetDemo.unity`：高程径向位移成球
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
- 地表 Albedo 与云密度烘焙为 Cubemap，指纹存在 importer 元数据里
- 大气与云壳层改读统一半径接口，1× 与 `G` 的 400× 下比例一致
- 星空曝光改由焦点星球测光驱动；修掉星图经度接缝的错误 mip 黑竖线
- 文档：新增 `PLANET-PROCEDURAL.md`；总览与太阳系 / 资产 / 演示文档同步定位与交叉引用

### 星系程序集划分

- 新增 **`Planet.Solar`** 独立程序集，星系运行时代码迁出 `Assembly-CSharp`；
  引用列表穷举为 `Planet.Procedural` + `Unity.InputSystem` + URP Core / Universal Runtime 四项
- 依赖单向化 `Planet.Solar → Planet.Procedural`：`CelestialBody` 不再反向推送大气 / 云壳半径，
  改为壳层自己经 `IPlanetRadiusProvider` 反查——壳层本来就有这条路径，行为不变
- 编辑器工具**故意不另立 asmdef**：多一个 asmdef 就多一个合并冲突点
- 验证：编译零错误零警告；场景 21 个 `CelestialBody` / 12 `OrbitPath` / 2 `BeltField` 引用完好
  （GUID 不变）；Play 模式实测壳层比例一致、`PlanetLightingController` 跨程序集调用正常
- 文档：`PLANET-SOLARSYSTEM.md` 新增程序集边界一节；总览补程序集表、多工作树 MCP 实例端口表与
  两条 asmdef 风险项；`PLANET-PROCEDURAL.md` 同步

### 全部天体材质

- SolarSystem 的 21 个天体均接入定义／统一采样／Cube Sphere／独立 Albedo 缓存；Earth 保留 Terra 原资产
- 添加表面类型：岩壳、冰裂纹、硫磺火山、气巨星／冰巨星云顶、不透明雾霾、太阳发光光球
- 保留扁率与 Haumea 三轴形状；程序化表面继续构建环，补木星主尘环、天王星窄环、海王星主要环、Haumea 环
- 土星环径向光学透明度、环影和天体影；修正约 325 km 恩克缝、增加约 42 km 开勒缝
- 新菜单 Build Solar System Materials 分批重建所有天体；生成器版本提升，保留 GUID、
  已有手工定义参数和独立资产路径
- 文档：新增 `PLANET-MATERIALS.md`

### 星球缩放固化

- 星球半径不再每帧由 `SolarSystemSimulation` 从目录推算：改为在编辑器**烘焙一次**，
  存进 `CelestialBody.sceneRadiusUnits`（+ 出处 `bakedKmPerUnit`）
- `ApplyState(position, radiusUnits, spin)` → `ApplyState(position, spin)`；`RadiusUnits` 变成只读派生量
- 缩放写入只在**值变化**时发生，不再每帧写 21 次 `localScale`；`CelestialBody.Update` 拾取 Inspector 手改
- 新增菜单 `Bake Body Scales` / `Validate Body Scales`
- 场景改动是**纯新增**，零删除，GUID 全保留
- **注意**：这**没有**加快初始化。实测半径换算 1000 帧 × 21 天体只需 0.32 ms；
  启动耗时几乎全在程序化网格生成 —— 当时 `PlanetSurfaceView` 的网格是 `HideFlags.DontSave`，
  每次进 Play 都重新生成。这条后来由离线 Mesh LOD 与再后来的运行时分块地形各自解决

### 宇宙尺度坐标阶梯与地表流式 LOD

- `Solar/Space/` 新增六个文件：`SpacePrecision` / `SpaceFrame` / `SurfaceChunkGrid` /
  `ChunkLocalGeometry` / `ChunkStreamer` / `FloatingOrigin`，加场景组件 `SolarSpaceContext`
- **两套坐标系**：宇宙侧 `double` 米存在四级架树（Galactic → Stellar → Planetary → Surface），
  地表侧 `float` 米相对**分块自己的原点**；唯一规则是「在 double 里减，最后一步才降到 float」。
  `RenderOrigin.ToRender` 是全局**唯一**的降 float 点
- **视场连续过渡**：地表架用 `Threshold` 模式，不存在坐标系切换断点
- **块接缝**：顶点方向用**全局整数格点索引**，同级相邻块逐位相同；
  跨面邻接**不写 24 条边表**，改为翻过边界在父面 uv 方形里继续走一步
- **LOD**：屏幕空间误差规则，取满足容差的**最粗**一层；`ChunkStreamer` 每帧硬预算 + 最近优先 + 逐出迟滞
- **远场策略**：按投影像素分 Full / Proxy / Marker / Skipped 四档，只切 `MeshRenderer`；
  轨道线静态，切焦点**不重算顶点**
- **碎带自适应节奏**：`interval = angularStalenessDegrees / (maxMeanMotion × daysPerSecond)`，
  把求解从每帧降到按方向陈旧度推导的间隔
- 新增只读审计菜单 **Audit Space Scale**，报告七节写入 `Library/SpaceScaleAudit.txt`
- 新增测试程序集 **`Planet.Solar.Tests`**（必须放子目录：Unity 不允许同一目录下两个 asmdef）
- **`Planet.Solar.asmdef` 一项引用都没加**（列表仍是穷举的四项）
- 验证：编译零错误零警告；新框架与原浮动原点变换**逐位一致**
- 文档：新增 `PLANET-SPACE.md`

---

## 2026-10-05

### 合并周期：solar + interaction 并入 main

- 母分叉点 `integration/authority-2026-10-04` 起，三支并行：
  `dsh/SolarSystem`（尺度层）、`mimo/interaction`（交互层）、`codex/planet-definition`（LOD / 恒星）
- `main` 先独立落了 C1/C2 契约。**该提交与 interaction 分支中的同名文件逐字节相同**，
  是「先定契约再分工实现」的正常产物；合并后自然消解
- 合并顺序按协议：**solar → interaction → planet-definition**
- solar 与 interaction 均**零冲突**；interaction 与另两支的接触面已被 ownership 表预先划清
- 顺带修掉一个协议缺口：`main` 的 C1/C2 提交**漏了 4 个 `.meta`**，磁盘上不存在。
  Unity 在刷新时会自己补一份**新 GUID** 的 `.meta`，必须删掉自生成的那份、让提交版落地
- `CelestialBody` 保留尺度缓存与恒星表面接线，场景同时保留 `SolarSpaceContext` 与
  `SolarSystemVisualCoordinator`，后者使用独立 YAML fileID

### 文档重整

- 根目录文档按功能分工重划，新增 [DOCS.md](DOCS.md) 作为索引与引用图
- 合并类报告（`MERGE-PLAN-*` / `MERGE-REPORT-*`）不是长期文档：已处理完的删除，
  仍需执行的降级为 `REPORT-*`（分支交接单）
- 重复内容收敛：踩坑表、视场高度契约、显存预算、程序集边界各归一处，其余文档只留链接

### 地表漫游稳定性

- 进入漫游时保留完整 body-fixed 相机四元数，避免系统视角在 ENU 欧拉角往返中产生旋转抽搐；
  增量俯仰限制在可用视角内，退出时按焦点天体径向恢复轨道视角
- 漫游的高度同步只更新轨道相机距离状态，不触发相机 Transform 重算；高速飞行按当前 body-fixed
  径向地表高度更新裁剪面，并在积分后阻止穿入星体；near clip 增加 Unity 可用下限，
  保留 far/near 比率上限，避免长期贴地运行产生退化投影

### 运行时地形与恒星框架并入

- `Planet.Procedural.Terrain` 并入运行时分块地形：`TerrainLattice` / `PlanetDetailSampler` /
  `RuntimeTerrainSource` / `RuntimeTerrainSelection` / `RuntimeTerrainChunk` / `RuntimeTerrainRenderer`
- 与 `Solar/Space/` 的分块网格是**两套独立实现**：前者按体固双精度米制域自建 32 格点 × 25 层
  的格点阶梯并已接入帧循环，后者是 6 面 × 2^level（上限 20 层）的审计用地图，`ChunkStreamer`
  至今没有运行时消费者。二者尚未统一
- 恒星框架并入：`CelestialDefinition` 成为基类，`PlanetDefinition` / `StellarDefinition` 分别继承；
  太阳改用 `StellarSurfaceView` + 动态温度辐射，行星采样链不受影响。旧 `Sun.asset`、
  静态 Cubemap 与材质保留为兼容／回滚资源
- Earth 增加按视场高度分级的四档纹理 LOD。近景法线 Cubemap 在同一次整合中被**撤掉**：
  运行时分块地形自带几何法线，再绑一张全球法线贴图反而会覆盖它，
  于是 `normalSizes` 四档全置 0（早期烘出的两张法线 Cubemap 成为未引用残留）；
  新增 `PLANET-TEXTURE-LOD.md`
- 正式场景改为 `useRuntimeChunks = 1`：21 个天体全部走运行时分块地形，
  离线 `PlanetMeshLodSet` 路径退化为 `useRuntimeChunks = false` 时的回退；场景里的 `meshLodSet`
  引用已置空，`Assets/Editor/LegacyPlanetMeshLod/` 下的派生资产不再被正式场景引用

### 文档再收敛（抽象重排）

- 新增 [PLANET-PITFALLS.md](PLANET-PITFALLS.md)：把原先散在三张分表与总览「风险」一节里的
  「现象 → 结论」条目合并为**一份**、按子系统分组；专题文档只讲机制，不再重述现象
- [DOCS.md](DOCS.md) 从「枚举唯一真相」升级为**写作规则**：每篇只写「为什么 / 契约 / 入口」三层，
  可再导出的实例值（文件数、测试数、体积、清单、哈希）不再写进文档；踩坑统一收口
- 据此删除总览的「风险与注意事项」一节与「磁盘占用」一节：前者的每一条都已有 owner，
  后者是可随时重新测量的实例值
- 登记此前**从未进入索引**的 `PLANET-TEXTURE-LOD.md`，并修正索引里一处指向不存在表格的 owner 声明
  （「编辑器菜单全表」原先指向总览的代码一节，实际表格在 `PLANET-SOLARSYSTEM.md`）
- 修正与实现不符的陈述：地表模式闸门阈值在协议文档内自相矛盾（同一文档 §3 写 5%/8%、
  §3.2 写 1.524%/3.0%，代码为 0.05/0.08）；场景 `maxChunkLevel` 已不是 18；
  「`ChunkStreamer` 未接入帧循环」需限定为「未被 `ChunkStreamer` 接入」，因为分块地表实际已在渲染

---

## 2026-10-06

### 删除演示线与 NASA 贴图资产

- 判定「参考场景」这条线价值不足，整体移除：`PlanetDemo.unity`、`SampleScene.unity`
  以及只服务于它们的 `Earth_Surface.mat`、`Moon_Surface.mat`、`DemoPost.asset`、
  `PlanetMeshGenerator`／`PlanetSpin`／`OrbitCamera` 三个脚本
- 连带删除 `Assets/Textures/Planets/`（11 张 NASA 地球／月球贴图，约 183 MB）、
  `_assets_src/`（1043 个文件，约 384 MB）与 9 张 `planet_demo_*` 截图。
  **`starmap_2020_4k.exr` 与 `Atmosphere_Earth.mat` 保留**——前者被场景天空盒引用，
  后者被 `SolarSystem.unity` 自己引用
- `EditorBuildSettings` 只剩 `SolarSystem.unity`（index 0）；文档侧删除 `PLANET-ASSETS.md`
  与 `PLANET-DEMO.md`，星空天空盒的**逐轴 wrap 与经度接缝契约**迁入
  [PLANET-SOLARSYSTEM.md](PLANET-SOLARSYSTEM.md) §5，不再有第二处
- 一个后果值得记下来：工程从「贴图地球 + 程序化地球」两条线收敛为**纯程序化一条线**，
  磁盘上的位图资产只剩星空这一张
- 清掉两处遗留引用：`SolarMeshFactory` 的注释、`SolarSystemAppearanceCatalog` 里 Moon 的
  `interpretation`（"not the LRO map used in PlanetDemo"）。后者在 `appearance` 里，
  **会改 `ContentFingerprint()`**，所以 `Moon.asset` 一并改了——
  下次 **Build Solar System Materials** 会重烘 Moon 的 Albedo，其它 20 个天体不受影响

### 协议文档瘦身

- [DEVELOPMENT-WORKFLOW.md](DEVELOPMENT-WORKFLOW.md) §2 的「程序集与目录 ownership」
  与七行程序集清单长期维护成本高于收益，改为**工作树分区**简述（三棵树各管什么、
  共享面是唯一冲突来源）；保留的是真正的契约：单向依赖方向、两条程序集硬约束、
  §2.1 跨程序集接口清单与 §2.2 尺度层接口

### 文档总量核查

- 现状实测：11 篇根文档 + `MCP-Setup/` 报告，合计约 **4.7 万 token**（按 CJK 1.4 字/token、
  ASCII 3.5 字/token 估算），远低于 128k 上限。**注意"字符数"不是"token 数"**：
  中文 UTF-8 每字 3 字节，按字节或字符数估算会把总量高估一倍以上
- 因此本轮**没有**为了凑指标继续删内容；压缩只发生在合并重复与删除已失去消费者的段落


### 统一观察者、导航与近地碰撞

- 自由飞行成为常驻运动系统，聚焦改为临时导航；近地交接转换已有位姿与速度，
  不再在轨道相机和地表相机之间清零/回写。契约见 [DEVELOPMENT-WORKFLOW.md](DEVELOPMENT-WORKFLOW.md) §3。
- 渲染原点与玩家绑定，选中目标和实际近邻分离；双精度旋转避免行星半径放大 float 换算误差。
- 远景固定全球网格，近地才流送区块；区块显示三角形派生局部米制碰撞，父子覆盖原子替换，
  米制近景相机补足太阳系投影的近裁面限制。机制见 [PLANET-PROCEDURAL.md](PLANET-PROCEDURAL.md) §16。
- 加入真实场景的导航、速度交接、地形碰撞、自转稳定与资源释放 PlayMode 回归。

---

## 2026-10-07

- 聚焦改为对数距离缓动，恢复选中目标的左键环绕；滚轮统一推进，小键盘独立调整速度倍率，
  避免近地切换输入含义。操作见 [PLANET-SOLARSYSTEM.md](PLANET-SOLARSYSTEM.md) §6。
- 增加可切换的中心引力，其速度累积与飞行输入阻尼分离；位姿与碰撞仍由同一控制器处理，
  契约见 [DEVELOPMENT-WORKFLOW.md](DEVELOPMENT-WORKFLOW.md) §3。
- 近地地形先满足不依赖朝向的区域精度基准，再细化脚下和视野，兼顾远景一致性与流送预算；
  策略见 [PLANET-PROCEDURAL.md](PLANET-PROCEDURAL.md) §16。
- 地球显示起伏回归真实高度；玩家进入大气层时，星空按高度和太阳位置连续过渡到
  昼夜天空，夜间保留星光。渲染契约见 [PLANET-SOLARSYSTEM.md](PLANET-SOLARSYSTEM.md) §5。
- 地形与海面拆为独立物体，移除海床压平，海岸线来自水面与实体地形相交；地球物理
  高程校准到约 20 km 总落差。海面与四叉树覆盖一起流送，碰撞只取地形；机制见
  [PLANET-PROCEDURAL.md](PLANET-PROCEDURAL.md) §8。

---

## 2026-10-07

- 以 SpaceEngine 体验为目标整理商业级渲染路线与独立 Player 验收方法，明确当前能力和
  后续资源、材质、光学与地貌阶段，见 [PLANET-RENDERING.md](PLANET-RENDERING.md)。
- 地形热点与全球网格接入 Burst 直接调用；修正子块只退回未混合父频段造成的边界偏差，
  区块几何版本进入缓存键，见 [PLANET-PROCEDURAL.md](PLANET-PROCEDURAL.md) §16.4。
- 近地材质改为解析梯度和屏幕带宽过滤，保留已解析地形法线；增加上传准入、Profiler 标记
  与整机性能采集，避免把采样加速或编辑器平均 FPS 当作最终性能验收。
- 材质采样保留整数高位格点，径向偏移提前在 double 域相减；性能采集检查实际相机渲染帧，
  排除隐藏窗口的未渲染循环。清理正式场景 Earth 上已不存在的脚本引用以通过构建检查。

---

## 待补

- [ ] **统一两套分块层级**：`Solar/Space/SurfaceChunkGrid`（6 面 × 2^level，上限 20）与
      `Procedural/Terrain/TerrainLattice`（32 格点 × 25 层）目前各有一套格点与层数定义。
      实际在跑的是后者；前者的 `ChunkStreamer` 只有策略与测试，没有运行时消费者。
      合并前不要以为改 `ChunkStreamer` 的参数会影响画面
- [ ] **决定离线 `PlanetMeshLodSet` 路径的存废**：正式场景已全部走运行时分块地形
      （`useRuntimeChunks = 1`、`meshLodSet` 为空），该路径只在原型里作为回退。
      保留就要有消费者，弃用就要连同菜单与派生资产一起清掉
- [ ] **大气散射的高程连续过渡**：介质随相机高程连续计算已由视场高度契约提供，
      但渲染侧仍未接上运行时分块地形的地表高度
- [ ] **银河系架 `solInGalaxyMetres` 的数据源**（当前为 0，架构上已支持）
- [ ] **相机自动曝光与镜头光学**：目前只有表面测光近似，设计与边界见
      [PLANET-STELLAR.md](PLANET-STELLAR.md) §5
