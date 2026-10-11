# Planet — 独立程序化恒星框架

> 正式场景为 SolarSystem：聚焦 Sun 时启用独立光球渲染，其他焦点只显示纯色自发光体。
> 上游：[doc.md](../doc.md) §1–§2；「现象 → 结论」见 [PLANET-PITFALLS.md](PLANET-PITFALLS.md) §5。

## 1. 权威与职责

`CelestialDefinition` 是天体定义基类，持有 `displayName` 与 `PlanetPhysicalParameters physical`；`PlanetDefinition` 与 `StellarDefinition` 分别继承它，共享质量、半径、极点与参考形状，恒星没有行星的海洋、生物群系或矿物 albedo。基类化后基础字段仍以原序列化字段名存储，旧 `PlanetDefinition` 脚本与资源 GUID 保留，已有行星定义无需重建。

恒星定义在 `Assets/Resources/Solar/BodyMaterials/Stellar/SunStellar.asset`：Sun 的半径／极点同步 SolarSystemCatalog，质量取自已存在的 Sun 物理定义，新增恒星应从目录基础数据填充。旧的 `Sun.asset`、静态 Cubemap 与材质只作兼容／回滚资源保留；正式 Sun 已改用 `StellarSurfaceView`，它与 `PlanetSurfaceView` 是并列的消费者，不用行星采样器绘制恒星。

观察状态由上层 Assembly-CSharp 的 `SolarSystemVisualCoordinator` 单向发布给地球与恒星（读取公开的视距／焦点／日期接口，依赖方向见 [DEVELOPMENT-WORKFLOW.md](DEVELOPMENT-WORKFLOW.md) §2）；相机输入、轨道计算与焦点状态机不属于本框架。

## 2. 光球几何与对流

聚焦太阳用 subdivision 6 的 Icosphere（40,962 顶点／81,920 三角形，无 UV，没有 UV 球极点挤压），其他焦点用 subdivision 3（642 顶点／1,280 三角形，纯色发光着色器不执行表面噪声）；两档共用目录半径，选择状态不进入 ContentFingerprint，也不改变物理大小。

米粒组织是体固方向上的三维 Voronoi 场（亮中心、暗边界），低频对流由 FBM 控制：默认米粒直径约 900 km、超米粒约 30,000 km，随时间平移／旋转并改变边缘亮度，寿命时标分别约 600 秒与 1 天。全部在 fragment shader 中计算，不烘焙静态太阳 albedo；可配置的正常扰动物理高度约 100 m（相对半径约 1.44×10⁻⁷），只走法线梯度，不夸大几何轮廓。

着色器用采样足迹的屏幕导数判断米粒是否已被分辨，未分辨时压低其温度起伏（`filteredResolved`）。全盘判据：512 像素下一个像素覆盖数千 km，本来就显示不了每个 900 km 米粒，要看细胞必须放大局部光球，本框架不把真实粒度改成全盘大斑（对应现象见 [PLANET-PITFALLS.md](PLANET-PITFALLS.md) §5）。详细焦点下 Voronoi 仍逐像素计算；更大的采样成本留给将来的分支／频带 LOD。

## 3. 黑体与辐射

使用 SI Planck 光谱辐亮度：

\[
B_\lambda(T)=\frac{2hc^2}{\lambda^5}\frac{1}{\exp(hc/\lambda kT)-1}.
\]

CPU 将 380–780 nm 光谱按 5 nm 步长积分、乘以 CIE 1931 2° XYZ 匹配函数，再转线性 sRGB，生成 512×1、RGBAHalf、2500–9000 K 的 LUT；匹配函数用 Wyman / Sloan / Shirley 2013 的解析拟合，属于可量化近似，**不是**官方 CIE 表格。查表给出单位光度亮度的色度，总辐射另按 σT⁴ 缩放；LUT 按 `StellarRadiation.SpectrumVersion` 失效并保留 GUID，改光谱或 CIE 数学必须提升该版本。

平均温度默认 5778 K，米粒温差约 ±260 K，本影约 3800 K、半影约 4800 K；黑子区抑制米粒温度对比、半影叠径向丝状扰动，亮度按 `(T / 5778)^4` 随温度变化。输出只有辐射项，没有外部太阳灯光、反射、金属度或材质 albedo；σ5778⁴ 约为 6.32×10⁷ W/m²。

临边采用二次经验模型：

\[
I(\mu)/I_0=1-u(1-\mu)-v(1-\mu)^2,
\quad \mu=\max(\mathbf n\cdot\mathbf v,0).
\]

默认 u=0.48、v=0.18，边缘约为中心 0.34 倍，边缘光谱温度另下降约 380 K，形成较暗、较暖的边缘；它是灰体经验光度衰减与温度场的组合，不声称完整 LTE 辐射输运。

为让现有 HDR 观察窗口可读，shader 把辐射除以参考光球辐射、再乘固定显示归一化（`displayRadianceScale`）；自动曝光尚未实现，**不能**把这个归一化值当绝对辐亮度。LUT 来自光谱计算、没有硬编码黄色，观察者在太空也不叠地球大气的黄色散射。

## 4. 两个时钟与较差自转

- 表面自转、黑子寿命／诞生消亡使用模拟日 `julianDate - J2000`，暂停公转时这部分一并暂停。
- 对流纹理使用实时时钟：现有 5 天/秒甚至更高时间倍率下仍能看清短寿命细胞，不会几百个对流周期一帧掠过；它是观察演示时钟，暂停模拟日期后仍可观察对流。

较差自转作用于噪声采样坐标而非整体旋转球：角速度随 `sin²(latitude)` 连续混合，赤道默认 27 天、极区默认 37 天。目录中的刚性 Sun 自转由渲染子对象反向抵消，轨道／姿态系统仍正常提供极点（极点与自转矢量的方向关系不属于本框架，见 [PLANET-PITFALLS.md](PLANET-PITFALLS.md) §5）。

27 天是常用的地球视角近似会合周期，太空恒星的惯性系赤道周期约 25 天；本轮提供可独立配置的 27／37 天默认值，后续科学周期模式必须明确用的是恒星时还是会合周期，不能无说明混用。这些统计黑子不是实际观测活动区，也不是太阳磁流体模拟。

## 5. 色球、日冕与观察光学边界

`StellarOuterLayerOptions` 保留色球、日冕、耀斑／日珥开关与尺度字段以及观察者大气开关，但本轮没有外层 renderer，打开这些占位字段不会凭空出现效果。耀斑和日珥不写入光球地形，日后分别由色球活动场与外层体积／透明几何实现；白光普通观察中日冕极暗，需配合日冕仪遮盘／日食或相应曝光模式才可见。

### 自动曝光设计（本轮只设计）

未来所有自发光输出先保留线性 HDR，统一在 URP 相机管线做曝光：

1. 在 tonemapping 前估计 log-luminance 直方图，结合太阳投影面积与光球专用测光权重，避免只有几个太阳像素时把整幅星空压黑、或观察光球时被黑背景拉升曝光导致全白。
2. 区分系统浏览、光球检查、日冕仪／日食观察三个测光目标，用状态与参数配置表达，不在表面 shader 中加入曝光分支或黄色 LDR 压缩。
3. 对高亮做百分位裁剪／亮部保护，设 EV 上下限与不同增亮／压暗时常，并用平滑过渡防焦点切换闪烁。
4. 先曝光，再 Bloom／镜头光学，再 tone mapping；温度色度和 T⁴ 比例必须在 HDR 缓冲中可验证。
5. 现有 `PlanetLightingController` 只调整星空整体 EV，是表面测光近似，不能视为相机自动曝光。

### 程序化镜头眩光设计（本轮只设计）

未来使用独立 URP Renderer Feature：从曝光后的 HDR 高亮和太阳屏幕位置生成耀斑源，结合深度遮挡／采样历史稳定可见性，再用光阑形状、叶片数和衍射 PSF 参数生成星芒、鬼影和光晕。视角／镜头变化改变该屏幕空间效果，恒星定义不带星芒几何，太阳被天体遮挡时眩光必须减弱；色散本轮不做，地面观察的大气散射沿未来观察者路径 integrator 接口实现，本轮保留占位字段。

### 聚焦太阳的实际观测响应

`PlanetLightingController` 将“太阳聚焦天空曝光”和“光球显示曝光”分开处理：

- `sunFocusSkyExposureEV = -8` 只写入 `PlanetStarfield.shader` 的运行时全局 EV；
- 太阳屏占比达到 `sunFocusSkyCoverageThreshold`（0.35）后，标准 `2x–5x` 半径取景保持稳定的黑背景，不因轻微后退重新点亮星空；
- `StellarSurfaceView` 通过 `MaterialPropertyBlock` 将 `focusedExposureEV = -0.45` 传给光球，共享 `SunPhotosphere.mat` 资产不被运行时改写；
- 聚焦态使用暖红观测响应、额外的中频对流纹理和轻微 limb glow，物理黑体温度与 `T^4` 计算仍保留；
- 非太阳焦点继续使用 `Planet/StellarDistant` 纯色发光 shader，不执行光球噪声和曝光分支。

该响应模拟参考图的窄带／高动态范围观测观感；显示的红色**不是**太阳真实可见光色，也不是绝对辐亮度（见 [PLANET-PITFALLS.md](PLANET-PITFALLS.md) §5）。

## 6. 重建与验证

运行 **Tools → Planet → Build Stellar Framework** 生成独立定义、温度 LUT 和两个材质并迁移 Sun 接线（**Build Solar System Materials** 的 Sun 路径也调用它）；迁移只替换太阳的表面组件（`PlanetSurfaceView` → `StellarSurfaceView`），资产 GUID 不变。

验证集中在 `Assets/Tests/EditMode/StellarFrameworkTests.cs`（5 个 `[Test]` 方法，Icosphere 一例按 subdivision 0/3/6 展开），每个用例钉住一条不变量：SI Planck 光谱积分与 σT⁴ 的相对误差在 1e-4 以内、温度色度的方向与单位光度亮度、黑子亮度比例、`sin²` 纬向周期的 27／37 天端点、二次临边 law、Icosphere 的单位半径／外向面／闭合边／无 UV。两个代表值：3800 K 本影辐射约为光球的 0.19 倍；固定日期局部对流在 0 与 120 秒之间 RGB 平均绝对差约 1.3–5.3（0–255），全盘未分辨米粒时该差值很小，与 §2 的足迹过滤一致。Play 模式核实：Sun 焦点为 subdivision 6 光球与光球 shader，其他焦点为 subdivision 3 纯色 shader，星空 EV 与光球聚焦 EV 按 §5 的字段生效，脚本与三个相关 shader 编译无错误／警告；实际相机自动曝光、外层和镜头光学仍未实现。

参考：Planck／Stefan–Boltzmann 定律、[CIE 1931](https://cie.co.at/datatable/cie-1931-colour-matching-functions-2-degree-observer)、[Wyman et al. 2013](https://jcgt.org/published/0002/02/01/)、[NASA Sun](https://science.nasa.gov/sun/)。
