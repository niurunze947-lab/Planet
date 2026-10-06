# Planet — SolarSystem 天体材质

> 正式场景：`Assets/Scenes/SolarSystem.unity`。
> 21 个天体接入 [PLANET-PROCEDURAL.md](PLANET-PROCEDURAL.md) 的定义、纯函数采样和派生缓存链。
> 天体大小、扁率、自转和极点沿用 `SolarSystemCatalog`；可见光外观是程序化近似，不能作为探测地图。

## 1. 天体与外观

| 天体 | 可见光材质与结构 | 解释边界 |
|---|---|---|
| Sun 太阳 | 暖白发光光球、临边昏暗、颗粒纹理、示意黑子 | 颗粒属于统计纹理；没有日冕、日珥、黑子周期或真实辐亮度 |
| Mercury 水星 | 灰褐岩石、密集撞击坑、较亮坑缘 | 不使用夸张彩色矿物图；不复原卡洛里盆地地理位置 |
| Venus 金星 | 不透明浅乳黄硫酸云顶、低对比云纹、薄边缘散射 | 可见光看不到固体地表；不是雷达地形图，也不是紫外增强图 |
| Earth 地球 | 保留 Terra 的大陆、海洋、生态、冰帽、云和大气 | 程序化大陆，不是真实世界地图；垂直起伏是显示夸张，当前倍数见 [PLANET-PROCEDURAL.md](PLANET-PROCEDURAL.md) §5 |
| Moon 月球 | 暗玄武岩月海、亮高地、撞击坑 | 统计地貌，不复原真实月海地理 |
| Mars 火星 | 铁氧化物红褐尘土、暗玄武岩区、极区霜冻、薄尘大气 | 没有精确奥林帕斯山／水手谷、季节冰帽或沙尘暴 |
| Jupiter 木星 | 氨云带／区、涡纹、南纬约 22° 的示意大红斑、主尘环 | 云顶是可见表面；大红斑经度和形状不是当前观测预测 |
| Io 木卫一 | 黄／乳黄硫磺与二氧化硫沉积、红褐火山沉积环、暗火山口 | 不以撞击坑为主要地貌；没有喷发羽流、夜面熔岩亮度或浓大气 |
| Europa 木卫二 | 浅色水冰、红褐裂纹网、低撞击坑覆盖 | 地下海洋不作为表面海洋；裂纹不是实测地图 |
| Ganymede 木卫三 | 暗古老地形、亮冰质区域、沟槽网、撞击坑 | 不复原沟槽带的实际走向，不显示地下海洋 |
| Callisto 木卫四 | 暗冰／岩混合地壳、密集撞击坑与亮坑缘 | 多环盆地仅能粗略表现，没有瓦尔哈拉盆地重建 |
| Saturn 土星 | 浅黄低对比云带；D/C/B/A/F 环分层与径向纹理 | 保留卡西尼缝，分开约 325 km 恩克缝及约 42 km 开勒缝；没有极区六边形 |
| Titan 土卫六 | 橙褐不透明有机气溶胶雾霾与薄边缘散射 | 可见光不露出甲烷湖泊；没有红外窗口或完整延伸大气 |
| Uranus 天王星 | 淡青色甲烷吸收气层、微弱云带、代表性暗窄环 | “冰巨星”不意味着可见固体冰面；没有完整尘环集合 |
| Neptune 海王星 | 中等淡青蓝气层、白云纹、示意暗风暴、主要环 | 不照搬 Voyager 增强的深钴蓝；暗斑不是当前真实天气，未表达环弧 |
| Triton 海卫一 | 氮霜、浅粉褐冰地、斑驳裂纹与极区霜冻 | 没有季节挥发物迁移、氮喷泉或精确“哈密瓜”地形 |
| Pluto 冥王星 | 红褐 tholin 地形、亮氮冰盆地、薄蓝雾霾 | 亮盆地受 Sputnik Planitia 启发，未声称复原心形区域或氮冰对流 |
| Charon 冥卫一 | 灰色水冰／岩壳、裂纹、北极红色沉积 | 只在北极加示意红斑；没有虚构对称的南极红帽 |
| Haumea 妊神星 | 亮水冰、示意暗红斑、三轴形状、约 2287 km 半径的环 | 表面地理未分辨；红斑位置是示意。沿用 Catalog 的近似形状和极点 |
| Makemake 鸟神星 | 偏红甲烷／乙烷霜冻、温和斑驳 | 表面尚未被近距离分辨；斑块与坑是示意，质量采用不确定的名义估计 |
| Ceres 谷神星 | 暗水合矿物、撞击坑、示意碳酸盐／盐亮斑 | 亮斑受 Occator 启发，不是 Dawn 的实测地形；没有冰火山活动 |

主带和柯伊伯带的数千粒子仍是恒定张角的浏览标记，其真实尺寸在当前视角不能形成可分辨表面。
Sun 与 Earth 另接专门链路——Sun 的焦点内光球与温度场见 [PLANET-STELLAR.md](PLANET-STELLAR.md)，Earth 的近景纹理档位见 [PLANET-TEXTURE-LOD.md](PLANET-TEXTURE-LOD.md)；本表只保留两者的可见光解释。

## 2. 权威与派生资源

**权威**是 `Assets/Resources/Solar/BodyMaterials/<Name>.asset`（`PlanetDefinition`）：Earth 沿用原 Terra 的 `ProceduralTerra*` 资产路径与 GUID，Sun 的光球定义是 `Stellar/SunStellar.asset`。**派生**是同级目录的 `<Name>Surface.mat` 与 `<Name>_Albedo.cubemap`，以及按定义字段决定是否生成的 `<Name>Atmosphere.mat`、`<Name>Ring.mat` 和 `<Name>_RingProfile.asset`；Earth 的档位在 `EarthLod/`。Albedo 为 256²×6 线性 RGBA32 Cubemap、Earth 云密度为 128²×6；环纹理为 2048×1、线性 RGBA32、无 mip，同一径向颜色／透明度纹理供环渲染与表面环影共用。气态行星的“表面”就是云顶光学层，因此不生成云层特征、不额外叠一层地球式白云。
`SolarSystemAppearanceCatalog` **只创建首次预设**：已有定义资产是外观的唯一权威，后续构建只同步 Catalog 的半径、扁率、三轴形状、自转周期与极点，使轨道系统与表面共用同一个物理半径，人工调整过的外观不被覆盖；质量不参与外观。地形网格 Earth 每面 96 段、其余天体每面 64 段。
`appearance` 与 `physical`／`generation`／`palette`／`features` 一起进入 `PlanetDefinition.ContentFingerprint()`：改序列化参数即改指纹、旧缓存自动失效；但只改采样数学（噪声、阈值、公式）而参数不变时，必须提升 `PlanetDefinition.GeneratorVersion`。派生资源一律用 `EditorUtility.CopySerialized` **覆盖**以保留 GUID，写完指纹元数据后必须**重新导入该派生资源**，否则紧接着再构建会读到旧指纹。构建不监听资产字段变化，改完定义要手动跑构建菜单。

## 3. 采样与着色

`PlanetSurfaceKind` 共九类：类地地表、撞击岩壳、干燥岩壳、硫磺火山、裂纹冰壳、气巨星、冰巨星、不透明雾霾与发光光球。Terrestrial 保留原 Earth 采样数学，其余由 blittable 的 `PlanetAppearanceSampler` 在体固单位方向上纯函数求值——没有图像生成、外部贴图或共享随机状态。
形态场共用：撞击坑的凹陷、坑缘与颜色来自同一个三维 cellular 场，裂纹取 cellular 边界距离，气带以地心纬度叠加连续三维湍流，区域与大斑按球面方向投影以避免经度 UV 接缝。这些是可复现的统计场，不能表达撞击年代、喷发过程、流体动力学或地理测绘信息。
三轴形状参与 `SurfacePosition` 与参考法线，进入程序化路径后不再丢失 Haumea 的拉伸。`opticalSurfaceHeightMetres`（金星云顶、Titan 雾霾等）只抬高**可见壳层**半径，不当作岩石地形高度；光学壳层（气巨星、冰巨星、不透明雾霾、光球）的高度查询恒返回参考层上的零起伏，不代表固体地面。
`Planet/ProceduralSurface` 承担发光与临边昏暗分支、参考形状法线、环遮光和可选经纬网；`Planet/ProceduralRing` 用径向颜色／光学透明度加解析球体遮挡，不读大尺度 URP 阴影图。环的位置与大小仍由 Catalog 提供，`CelestialBody` 的程序化分支继续构建环并随 1×／400× 同比缩放。`L` 控制全局经纬参考网（浏览材质默认不叠网格），发光太阳也参与星空测光，减少近景亮太阳旁过亮的星图。

## 4. 框架缺口与对策

| 特征 | 当前对策 | 仍需的扩展 |
|---|---|---|
| 太阳发光、临边昏暗 | 在共用表面 shader 内增加光球发光分支，避免出现受外部太阳光照的半黑太阳 | 日冕／日珥需要体积或透明几何与独立动态场；绝对辐亮度需要曝光系统重标定 |
| 气巨星条带、风暴 | 独立气层类型，连续纬向场与球面大斑 | 差异自转、云超旋与流体演化需要时间相关采样、云密度与阴影同步演化，并改变静态缓存接口 |
| 可见光下遮蔽地表 | Venus/Titan 将不透明光学壳作为可见层 | 地表／多层云／雾霾需要分离几何和谱段控制；红外或雷达不能共用当前可见光材质假装实现 |
| 撞击坑、冰裂纹、矿物 | 新的统计形态场，共享高度与颜色数据 | 多环盆地、线状山脊、精确地理需要可序列化球面 feature 集合或观测数据采样器；细裂纹宜增加法线 Cubemap |
| 环及其遮光 | 参数化主环几何、径向光学 profile、环影与球体影 | 遮挡天体目前按球体近似；扁率和三轴遮挡应改解析椭球；环弧需方位参数，尘环／日冕需体积散射 |
| 地下海洋与喷发 | 保持冰壳不显示表面液态海洋；不绘制未经实现的喷泉／熔岩自发光 | 剖面／内部层级、局部发光 mask、粒子或体积羽流、热输运模型 |
| 实测表面与未分辨天体 | 每个定义保存 `interpretation`，区分观测特征、示意位置和未知地理 | 如果要求地图级复原，需要接入 NASA/USGS 数据而非继续调噪声；未知区域保留不确定性 |
| 大气光学 | 沿用物理高度壳层与视角路径近似，按天体调颜色和密度 | 波长相关 Rayleigh/Mie、多层散射、云自阴影和正确透射需要大气积分／查表模型 |

大气厚度、颜色与 opacity 是外观近似，不是由压强、粒径与吸收截面推导的辐射传输；光照仍是朝焦点的平行光加固定强度，日食、多天体分别照明与太阳距离平方衰减都不在本轮实现。环影按参考面求交、忽略细小地形起伏，太阳黑子与已命名特征的位置不随观测日期更新。

## 5. 构建与验证

打开 SolarSystem、退出 Play 模式（Play 下构建直接抛异常），执行 **Tools → Planet → Build Solar System Materials**；菜单按编辑器更新分批构建天体，完成后保存场景与资产。**Rebuild Solar System Scene** 同样接入全部天体材质；Earth 的 Terra 示例仍走 **Build Procedural Planet Example**／**Refresh Procedural Planet Visuals**，菜单全表见 [PLANET-SOLARSYSTEM.md](PLANET-SOLARSYSTEM.md) §6。
代码入口 `Assets/Editor/SolarSystemMaterialBuilder.cs`，运行状态为 `IsBuilding`、`BuildStatus`、`LastError`。
验证在 Test Runner 的 EditMode 下跑 `Assets/Tests/EditMode/Planet.Procedural.Tests.asmdef` 里的 `PlanetAppearanceTests`：逐 surface kind 校验采样有限且确定（含两极）、立方体面共享边的位置／法线／颜色一致、光学壳层无虚构地形起伏、三轴形状与椭球法线，以及改动 `appearance` 必然改变内容指纹。

## 6. 特征依据

外观解释参考 NASA 的 [Solar System](https://science.nasa.gov/solar-system/) 与各天体事实页，及妊神星环掩星论文 [Ortiz et al., Nature 550, 219–223 (2017)](https://doi.org/10.1038/nature24051)。
这些来源描述天体类别、光谱成分和特征；本轮生成的贴图没有来自探测器的地理坐标数据。
