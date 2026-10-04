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

### 全部天体材质

- SolarSystem 的 21 个天体均接入定义／统一采样／Cube Sphere／独立 Albedo 缓存；Earth 保留 Terra 原资产
- 添加表面类型：岩壳、冰裂纹、硫磺火山、气巨星／冰巨星云顶、不透明雾霾、太阳发光光球
- 保留扁率与 Haumea 三轴形状；程序化表面继续构建环，补木星主尘环、天王星窄环、海王星主要环、Haumea 环
- 土星环径向光学透明度、环影和天体影；修正约 325 km 恩克缝、增加约 42 km 开勒缝
- 新菜单 Build Solar System Materials 分批重建所有天体；生成器版本升至 4，保留 GUID、已有手工定义参数和独立资产路径
- 材质观测解释、示意范围与框架缺口见新增 `PLANET-MATERIALS.md`；采样／接缝／形状／指纹回归测试见 `Assets/Tests/EditMode/`

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

---

## 待补

- [ ] 
