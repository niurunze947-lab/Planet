# Planet — 真实比例太阳系与程序化星球示例

`Assets/Scenes/SolarSystem.unity`：从**太阳**到**柯伊伯带**、按真实比例搭建的太阳系，
每个天体的**自转**与**公转**全部由代码按开普勒轨道根数推算。
21 个天体均挂载定义驱动的程序化 Cube Sphere 与独立材质；Earth 另有海洋、生态、云与大气。
外观、环和未实现的天文特征见 [PLANET-MATERIALS.md](PLANET-MATERIALS.md)。更新：2026-10-04。
Earth 纹理分级见 [PLANET-TEXTURE-LOD.md](PLANET-TEXTURE-LOD.md)；Sun 独立光球框架及其他焦点下的
纯色发光回退见 [PLANET-STELLAR.md](PLANET-STELLAR.md)。两者只读取公开的观察契约，不修改输入／轨道。
行星几何使用 [离线 Mesh LOD](PLANET-PROCEDURAL.md)：非焦点天体只保留 16 段参考网格，焦点按
离地比例切换 32／64／96 段；网格在编辑器中烘焙，Play 不重建几何。

- 场景文件：`Assets/Scenes/SolarSystem.unity`（已加入 Build Settings）
- 代码目录：`Assets/Scripts/Solar/`（独立程序集 **`Planet.Solar`**，见 §9）、`Assets/Scripts/Procedural/`
- 着色器：程序化地表／光球、环、云、大气、轨道和星空，均在 `Assets/Shaders/`
- 材质与定义：`Assets/Resources/Solar/`；天空与大气材质在 `Assets/Settings/`
- 程序化星球框架：见 **[PLANET-PROCEDURAL.md](PLANET-PROCEDURAL.md)**（Earth 地表 / 云 / 大气的实现）
- 编辑器工具：菜单 **Tools ▸ Planet**

---

## 1. 比例：为什么是 1 场景单位 = 1,000,000 km

Unity 的浮点坐标只有约 7 位有效数字。地球轨道 1 AU ≈ 1.496×10⁸ km，柯伊伯带外缘 50 AU
≈ 7.48×10⁹ km。若 1 单位 = 1 km，坐标会到 7.5×10⁹，float 在小数点后已无精度可言，抖动会
肉眼可见。

选定 `kmPerUnit = 1,000,000` 后：

| 天体 | 真实值 | 场景单位 |
|---|---|---|
| 太阳半径 | 696,340 km | 0.6963 |
| 地球半径 | 6,371 km | 0.006371 |
| 地球轨道 | 1 AU | 149.60 |
| 海王星轨道 | 30.07 AU | 4,498 |
| 柯伊伯带 | 30 – 50 AU | 4,488 – 7,480 |

**比例本身没有被压缩**：海王星就在真实的 30.07 AU 处，没有为了好看把它拉近。太阳在
地球上看起来的张角算出来是 **0.533°**，与真实的 0.532° 一致——这就是比例正确的直接证据。

代价是行星本身会变成亚像素：从 2 AU 外看，地球直径只有 0.002°（约 0.04 像素）。
这是真实比例必然的结果，不是 bug。应对手段有三条，都在场景里：

- **轨道线**：行星永远在自己轨道的椭圆上，这是找位置的主要依据。
- **`G` 键**：把天体半径放大 400×（HUD 会标注"非真实比例"），用于确认"确实有东西在那里"。
- **数字键 1–0 / `Tab`**：直接飞到天体上，近到能看清为止。

### 浮动原点（floating origin）

所有轨道计算都在 `double` 下以 **km** 为单位进行，只在提交给 `Transform` 的最后一步才转成
`float` 的场景单位，并减去当前焦点天体的位置：

```
世界坐标 = 焦点天体位置 + (天体位置 − 焦点天体位置) × unitsPerKm
```

焦点天体因此恒定在原点，相机始终在原点附近，float 精度全部用在"看得见的这一段"上。

> **踩过的坑**：最初把原点写成"向焦点位置做指数平滑"，这在 5 天/秒时只有 4 个单位的稳态
> 误差，看不出问题；但误差 = 速度 × 时间常数，一旦把时间流速提到 36,500 天/秒，误差涨到
> 30,000 单位，行星直接飞出画面。正确写法是把原点拆成"刚性跟随 + 衰减残差"
> （`SolarSystemSimulation.UpdateOrigin`）：原点严格等于焦点位置加一个衰减到 0 的偏移，
> 切换焦点时偏移从当前差值起步，于是既不跳变也不滞后。

---

## 2. 轨道力学

`Assets/Scripts/Solar/Astro.cs` 与 `SolarSystemCatalog.cs`。

轨道根数取 **JPL/Standish《Keplerian Elements for Approximate Positions of the Major Planets》
Table 1（1800–2050 适用）**，含 6 个根数各自的长期变化率：

```
a, e, i, L, ϖ, Ω   +   ȧ, ė, i̇, L̇, ϖ̇, Ω̇
```

求解流程（`KeplerianElements.PositionAu`）：

1. `T = (JD − 2451545.0) / 36525`，用变化率把根数推到目标时刻；
2. `M = L − ϖ`，牛顿迭代解 **开普勒方程** `E − e·sin E = M`；
3. 轨道平面内坐标 `x' = a(cos E − e)`，`y' = a√(1−e²)·sin E`；
4. 用 `ω = ϖ − Ω`、`i`、`Ω` 三次旋转转到黄道坐标。

月球用 **Meeus《Astronomical Algorithms》** 的简化根数（a = 384,400 km，i = 5.145°，
e = 0.0549，周期 27.3216 天）。卫星（木卫一至四、土卫六、海卫一、冥卫一）用圆轨道近似，
周期取实测值。

### 数值验证（场景中实测）

| 检验 | 结果 |
|---|---|
| 地球 J2000 日心距 | 0.9833 AU（真实近日点附近，✓） |
| 地球一个周期后的闭合误差 | 2.9×10⁻⁶ AU（约 437 km，来自长期变化率的线性化） |
| 地球黄经每 1/4 年推进 | 100.38° → 192.38° → 280.55° → 8.55° → 100.38° |
| 每季度推进角度不均匀 | 92.00° / 88.17° / 88.00° / 91.83°，近日点快、远日点慢（✓ 开普勒第二定律） |
| 太阳在地球处的视直径 | 计算值 0.533°，真实 0.532° |
| 地球自转周期 | 23.9344696 h，自转 360.000° 后 mod 360 回到 0.00° |
| 木星、土星、天王星、海王星 J2000 日心距 | 4.966 / 9.172 / 19.922 / 30.117 AU，与逐项手算一致 |

### 转轴倾角（不是"相对黄道"）

HUD 里显示的 **转轴倾角** 是自转轴与**该天体自身轨道面法线**的夹角，而不是与黄道北极的夹角。
两者差别很大：火星轨道倾角 1.85°，按黄道算会得到 26.71°，按自身轨道面算才是教科书上的
25.19°。这里有个真实的坑：IAU 极点并不总是自转矢量本身。

- **金星、天王星**：公布的极点落在黄道北侧，但两者都是逆向自转，所以极点其实是自转矢量的
  **反向**，必须翻转：金星 2.64° → **177.36°**，天王星 82.24° → **97.76°**。
- **冥王星**：公布的极点本身就指向黄道**南**侧（黄纬 −22.8°），它就是自转矢量，再翻一次会得到
  荒谬的 60°，而正确答案是 **122.5°**。

所以规则是「逆向自转 **且** 极点位于黄道北侧」才翻转。用同一个公式就同时对上了金星、天王星、
冥王星三个方向各异的特例。

实测倾角（与教科书值对照）：

| 天体 | 本场景（相对自身轨道面） | 相对黄道面 | 教科书值 |
|---|---|---|---|
| 水星 | 0.01° | 7.01° | 0.034° |
| 金星 | 177.36° | 178.76° | 177.36° |
| 地球 | 23.44° | 23.44° | 23.44° |
| 火星 | **25.19°** | 26.71° | 25.19° |
| 木星 | 3.12° | 2.21° | 3.13° |
| 土星 | **26.73°** | 28.05° | 26.73° |
| 天王星 | 97.76° | 97.72° | 97.77° |
| 海王星 | 27.85° | 28.03° | 28.32° |
| 冥王星 | 119.61° | 67.19° | 122.53° |

冥王星那 2.9° 的差来自 IAU 极点与轨道根数各自的历元不同，不是公式的问题；木星、海王星同量级。
**火星那一行是这套算法的意义所在**：按黄道算会得到 26.71°，只有按它自己的轨道面算才是教科书上
的 25.19°。

### 自转与公转是怎么驱动的

```csharp
// 公转
Vec3d au = def.Orbit.PositionAu(julianDate);      // 日心黄道坐标，AU
_posKm[i] = au * OrbitalMechanics.AuKm;

// 自转：绕自身极轴，按恒星日推进
double spinDeg = 360.0 * (julianDate - J2000) / (RotationHours / 24.0);
transform.rotation = poleRotation * Quaternion.AngleAxis((float)spinDeg, Vector3.up);
```

自转角度是**时间的一次函数**（不是每帧累加），所以拖时间轴不会积累误差，暂停/恢复/跳到任意
日期都能得到同一个姿态。

### 坐标系：右手黄道 → 左手 Unity

```
(X, Y, Z)_黄道  →  (x, z, −y)_Unity
```

右手系转左手系必须镜像一个轴。选最后一个轴取反，行星从黄道北极看下去就是**逆时针**公转，
与真实太阳系一致。

---

## 3. 场景内容

| 分组 | 内容 |
|---|---|
| `Bodies/` | 21 个 `CelestialBody`：太阳、八大行星、月球、木卫一至四、土卫六、海卫一、冥卫一、谷神星、冥王星、妊神星、鸟神星 |
| `Orbits/` | 12 条 `OrbitPath`（九大行星 + 3 颗矮行星的完整椭圆）+ 2 条 `CircleLine`（30 AU / 50 AU 柯伊伯带边界） |
| `Asteroid Belt 2.1-3.3 AU` | `BeltField`，2200 个粒子，真实轨道根数 |
| `Kuiper Belt 30-50 AU` | `BeltField`，5000 个粒子，真实轨道根数 |
| `Sun Light` | 一盏平行光，从太阳指向当前焦点天体 |
| `Main Camera` | `SolarSystemCamera` + `SolarSystemHud` + URP `UniversalAdditionalCameraData` |
| `Global Volume` | `Assets/Settings/SolarSystemPost.asset`（Bloom 0.85 / ACES / Vignette 0.22） |

### Earth：统一程序化星球尺度框架

Earth 使用 `ProceduralTerra.asset`：地表、海洋、生态、云与大气全部由程序化采样生成，没有贴图。
**框架本身的实现说明见 [PLANET-PROCEDURAL.md](PLANET-PROCEDURAL.md)**，这里只列本场景当前的取值。

| 项目 | 当前示例 |
|---|---|
| 物理半径 / 扁率 | 6,371,000 m / 1÷298.257 |
| 种子 | 271828 |
| 网格 | 6 面，每面 96 段；56,454 顶点，110,592 三角形 |
| 几何起伏 | 高度函数真实位移；星球尺度显示夸张为 4×，查询仍返回米制高度 |
| 地形粗采样范围 | −7,200 m … +5,802.79 m |
| 海平面 | −438.55 m，`seaLevelFraction = 0.52` |
| 采样水覆盖率 | 约 56.4%，按粗网格采样统计，不是面积积分的精确值 |
| 大气高度 | 80,000 m；壳层赤道半径比 1.012557 |
| 云壳半径比 | 外层 1.009；使用密度 Cubemap 与视角光程近似 |

`features` 分别提供 `hasOcean`、`hasAtmosphere`、`hasClouds`、`hasEcology`、`hasIceCaps`；
水位与地形函数分离，改水位不会移动山脉。水体、生物群系与配色的完整规则见框架文档 §7 / §8。

`CelestialBody` 在编辑器初始化时就计算实际场景半径，再通过 `IPlanetRadiusProvider` 供壳层读取；
因而 1× 和 `G` 的 400× 模式下，壳层与表面比例相同。

其余 20 个天体分别具有独立 `PlanetDefinition`、Albedo Cubemap 和材质，位于
`Assets/Resources/Solar/BodyMaterials/`。默认每面 64 段，25,350 顶点／49,152 三角形；
岩石、撞击坑、冰裂纹、气带、雾霾与太阳光球通过不同表面类型采样。气态行星不套用生态或表面海洋。
Venus/Titan 以不透明可见层隐藏地表；Haumea 保留三轴形状。太阳、巨行星与冰质卫星的解释范围见材质文档。

### 两条碎带是真的在转

每个粒子都带真实的 `a`、`e`、`i`、`Ω`、`ϖ`、`M₀`，用**和行星同一套**开普勒求解器推进，
周期由开普勒第三定律 `P(年) = a(AU)^1.5` 给出。于是内圈会比外圈跑得快，碎带会自己剪切开——
这是手摆的装饰粒子做不到的。

实测分布：主带 a ∈ [2.06, 3.27] AU（均值 2.68，e ≤ 0.22）；柯伊伯带 a ∈ [30.00, 50.00] AU
（均值 40.95，e ≤ 0.20，**26 AU 以内一个粒子都没有**）。

**唯一刻意的夸张是标记点的大小**，不是它们的位置。100 km 的小行星在真实比例下是 1×10⁻⁴
场景单位，永远不可能被看见。所以标记点按"相对相机的固定张角"缩放（默认 0.0012 rad ≈ 2.5 像素），
并带 ±55% 的随机尺寸抖动，免得密集成一片死白的贴图。位置仍然严格按比例。

### 材质与经纬参考网

所有天体使用 `Planet/ProceduralSurface`；太阳走自发光与临边昏暗分支，环使用 `Planet/ProceduralRing`。
`Planet/ClayGrid` 与旧白模材质保留作为缺少程序化定义时的回退资源。
浏览默认关闭经纬网；按 `L` 切换程序化表面的全局角度经纬参考网。
网格使用 `fwidth` 抗锯齿——这个场景从 0.006 单位的天体跨到 7,480 单位的柯伊伯带，
固定世界宽度的线在某一端必然是错的。

### 土星环的卡西尼缝

土星环不是一整片。`RingSet` 存的是分段的内外半径（单位 km），`SolarMeshFactory.Rings` 按
这些分段生成多个同心圆环：

| 环 | 半径 (km) |
|---|---|
| D | 66,900 – 74,500 |
| C | 74,500 – 92,000 |
| B | 92,000 – 117,580 |
| *卡西尼缝* | *117,580 – 122,170*（宽 4,590 km） |
| A | 122,170 – 136,775 |
| *恩克缝（A 环内）* | *133,426.5 – 133,751.5*（约 325 km） |
| *开勒缝（A 环内）* | *136,484 – 136,526*（约 42 km） |
| *罗希缝* | *136,775 – 140,180* |
| F | 140,180 – 140,580 |

只画一个 74,500–140,220 的实心圆环，土星看起来就是个盘子。

---

## 4. 操作

| 按键 | 作用 |
|---|---|
| `1` … `9`, `0` | 依次飞向太阳、水星、金星、地球、火星、木星、土星、天王星、海王星、冥王星 |
| `Tab` / `Shift+Tab` | 下一个 / 上一个天体（21 个全在这条链上，含卫星与矮行星） |
| `F` | 重新取景 |
| 滚轮 / `PgUp` `PgDn` | 缩放（对数） |
| 左键拖动 | 环绕 |
| `空格` | 暂停 / 继续 |
| `-` / `=` | 时间流速减半 / 加倍（0.002 – 2,000,000 天每秒） |
| `\` | 时间流速复位到 5 天/秒 |
| `N` | 跳到当前真实日期 |
| `R` | 回到 J2000 历元并继续 |
| `G` | 天体半径放大 400×（仅为可见，非真实比例） |
| `L` | 经纬网格开 / 关 |
| `H` | 显示 / 隐藏 HUD |

HUD 显示：模拟日期、时间流速、焦点天体的半径/扁率/自转周期/转轴倾角/轨道半长轴/偏心率/
轨道倾角/公转周期，以及比例尺和当前视距；右上角常驻柯伊伯带面板。

---

## 5. 光照的已知取舍

场景只有**一盏**平行光，方向从太阳指向当前焦点天体。

- 焦点天体上的光照是**准确的**：太阳在任何比水星更远的地方张角都小于 1°，平行光完全够用，
  昼夜分界线、极昼极夜、土星环的受光面都对。
- **广角俯视时不对**：整个太阳系同框时，海王星和地球收到的是同一个方向的光。真实情况是光
  从中心向外辐射。
- 阴影被**关闭**（`LightShadows.None`）：1×10¹⁰ km 的场景里，级联阴影贴图没有意义。因此
  没有行星落在卫星上的食。环投在天体表面、天体遮住环的太阳光改由解析求交计算，
  不读取级联阴影图；当前遮挡球体忽略扁率／三轴形状，环影忽略细小地形。

这些都是为了"每个天体单独看的时候是对的"而做的取舍，并已在场景中注释说明。

程序化地表也不读取太阳系的 URP 级联阴影贴图；云层 ShadowCaster 关闭，避免大场景范围与
天体半径不匹配造成闪烁和半圆直线分割。云影由共享密度 Cubemap 沿主光方向偏移采样。
地形法线影响光照，但地形遮挡式自阴影、食和完整体积云尚未实现。

### 星空曝光和经度接缝

`PlanetLightingController` 缓存 96 个球面方向样本，读取统一地表采样器的反照率与法线，按
可见投影权重、太阳颜色和强度估计地表亮度，再结合星球屏幕大小平滑更新 `_PlanetSkyExposureEV`。
相机移动、暂停后的环绕和焦点变化都会继续更新。该 EV 只控制星图整体曝光，是表面测光近似，
不是 URP 全画面的自动曝光。运行时不会写回共享天空材质。

星空没有半天空灰色覆盖或方向性底色。采样在 `atan2` 的 ±180° 接缝处先修正 UV 导数到最短
周期差，再使用 `tex2Dgrad`；仍保持贴图 `U=Repeat / V=Clamp`，避免接缝错误 mip 黑线。

---

## 6. 编辑器工具（菜单 Tools ▸ Planet）

| 菜单项 | 作用 |
|---|---|
| **Rebuild Solar System Scene** | 从 `SolarSystemCatalog` 重新生成整个场景并保存 |
| **Rebuild White-Model Materials** | 重新生成 `Assets/Resources/Solar/` 里的五个材质 |
| **Build Solar System Materials** | 为 21 个天体生成或复用独立程序化定义与缓存、材质，并保存正式场景 |
| **Open Solar System Scene** | 打开场景 |
| **Capture Solar System Poses** | 按 `SolarSystemPoses` 的 11 个机位出图到 `Assets/Screenshots/`（需在 Play 模式） |
| **Reimport Solar Screenshots** | 出图后手动触发资源导入 |
| **Build Procedural Planet Example** | 重算 Earth 地形统计，生成或复用地表 / 云 Cubemap，更新材质和场景 |
| **Refresh Procedural Planet Visuals** | 复用已生成 Cubemap，刷新壳层、光照与材质并保存；在编辑模式使用 |

场景是**代码生成**的，不是手工摆的。改比例、加天体、调分辨率都改 `SolarSystemCatalog` /
`SolarSystemFactory` 里的数字，然后重跑菜单，不会出现"场景和 HUD 数字对不上"的情况。

> **踩过的坑 1**：`CapturePoses` 里最初在一个循环里同步跑完 11 个机位。碎带标记走的是
> `Graphics.DrawMeshInstanced`，它的提交会**保留到帧末**——于是 11 个机位、11 个不同浮动原点
> 的同一片碎带全部叠在一帧里画出来，画面变成一圈圈同心甜甜圈。现在改为每个编辑器 tick 只处理
> 一个机位（`EditorApplication.update`）。
>
> **踩过的坑 2**：`CapturePoses` 末尾原本调了 `AssetDatabase.Refresh()`，它会重载域并把编辑器
> **踢出 Play 模式**，于是后续所有 `Instance` 都变成 null。现在刷新拆成独立的菜单项。
>
> **踩过的坑 3**：`OrbitPath` 起初把线宽存在字段里、等自己的 `Update()` 抄给 `LineRenderer`。
> 而编辑器的 **Run In Background 默认关闭**——Unity 窗口失去焦点时主循环根本不跑
> （实测 Play 模式开了 9 分钟只跑了 **2 帧**），`Update()` 从没执行，所有轨道线停留在第一帧
> 算出的宽度（相机还在原点时的 1×10⁻⁵），也就是**完全看不见**。现在线宽由模拟直接写进
> `LineRenderer`（`OrbitPath.SetWidth`），并且项目已开启 **Run In Background**。

---

## 7. 截图

`Assets/Screenshots/`：

| 文件 | 内容 |
|---|---|
| `solar_01-earth.png` | 地球近景（昼半球、终止线、经纬网格） |
| `solar_02-earth-moon.png` | 地月系（真实 384,400 km 间距） |
| `solar_03-sun.png` | 太阳近景 |
| `solar_04-jupiter.png` | 木星系全景 |
| `solar_05-saturn.png` | 土星与环（卡西尼缝、26.73° 转轴倾角） |
| `solar_06-uranus.png` | 天王星（97.8° 躺倒自转） |
| `solar_07-pluto-charon.png` | 冥王星–冥卫一 |
| `solar_08-inner-system.png` | 内太阳系（水星→火星轨道） |
| `solar_09-full-system.png` | 全系至柯伊伯带（55.9 AU 视距） |
| `solar_10-system-top.png` | 黄道俯视 |
| `solar_11-inner-halved.png` | 内太阳系，日期 +半年（用于对照公转） |
| `solar_hud_saturn.png` | 带 HUD 的土星，验证面板数值 |
| `procedural_earth_shells_fixed-1.png` | 程序化昼面：正确比例的大气 / 云层、连续地表光照和整体天空曝光 |
| `skybox_seam_fixed.png` | 相机正对 ±180° 经度接缝的纯天空验证 |

2026-10-04 验证：1× / 400× 下大气比均为 1.012557、云比均为 1.009；同一地形姿态下昼面
约 −2.39 EV，夜面约 +1.33 EV。四个相关 Shader 和脚本编译没有错误。
24 段验证网格的 292 组重复边界顶点，位置 / 法线差均为零；坐标往返最大误差约 1.3×10⁻⁹ m。
海洋 / 云 / 生态关闭与水位不改变地形的检查通过。本机完整构建 11.48 秒，缓存构建 0.93 秒。
固定暂停姿态连续三次渲染最大 RGB 差为 0/255，未复现阴影闪烁。

---

## 8. 与 `PlanetDemo.unity` 的关系

| | `PlanetDemo.unity` | `SolarSystem.unity` |
|---|---|---|
| 定位 | **渲染精度参考**：星系视角下星球应当精细到什么程度 | **正式工程场景**：功能开发与验证都以此为准 |
| 内容 | 贴图地球 + 月球特写 | 21 个程序化天体与五组环 |
| 视觉 | 8K 贴图、法线、地形起伏 | 独立地表、冰壳、气层、光球和大气；Earth 的海洋／云；可选经纬参考网 |
| 尺度 | 局部（趣味比例） | 太阳 → 柯伊伯带（真实比例） |
| 说明 | [PLANET-DEMO.md](PLANET-DEMO.md) | 本文件 |

两个场景共用星空天空盒与 URP 设置，互不依赖；参考场景**不接入程序化框架**。

---

## 9. 程序集边界与对外接口

星系运行时（`Assets/Scripts/Solar/`）编译进**独立程序集 `Planet.Solar`**
（`Assets/Scripts/Solar/Planet.Solar.asmdef`），不再与 `Assembly-CSharp` 混编。
目的只有一个：**让不同工作树合并时，不会因为新增的引用或被引用而互相绊住。**

### 依赖方向（单向，无环）

```
Assembly-CSharp / Assembly-CSharp-Editor   （胶水层：PlanetLightingController、编辑器工具）
        │                        │
        ▼                        ▼
   Planet.Solar  ──────────►  Planet.Procedural
        （只经由 IPlanetRadiusProvider 与 PlanetSurfaceView 接触）
```

`Planet.Solar.asmdef` 的引用列表是**穷举**的，只有四项：

| 引用 | 谁在用 |
|---|---|
| `Planet.Procedural` | `CelestialBody` 实现 `IPlanetRadiusProvider`，并驱动 `PlanetSurfaceView` |
| `Unity.InputSystem` | `SolarSystemCamera`、`SolarSystemSimulation` 的时间控制 |
| `Unity.RenderPipelines.Core.Runtime` | `SolarSystemFactory` 的 `Volume` / `VolumeProfile` |
| `Unity.RenderPipelines.Universal.Runtime` | `SolarSystemFactory` 的相机 / 灯光 URP 附加数据 |

`autoReferenced: true`，所以 `Assembly-CSharp` 与 `Assembly-CSharp-Editor` **不需要改任何 asmdef**
就能继续调用星系程序集——这是刻意的：**新增的 asmdef 越少，工作树合并时的冲突点越少。**

### 对外契约：其他程序只应调用这些

| 调用方 | 允许使用的成员 |
|---|---|
| `PlanetLightingController`（`Assembly-CSharp`，**唯一的运行时外部调用方**） | `SolarSystemSimulation.Instance` / `.FocusDefinition` / `.FocusView` / `.FocusRadiusUnits` / `.ViewCamera`、`Body.IsSun` |
| 编辑器工具（`Assembly-CSharp-Editor`） | `SolarSystemFactory.BuildAll`、`SolarMaterials`、`SolarSystemPoses`、`CelestialBody` 的 `bodyName` / `BuildVisuals` / `RadiusUnits` |
| 交互层 | `SolarSystemCamera` 的视距查询与设置、`SolarSystemSimulation` 的焦点查询；不得读取网格或材质缓存 |

`SolarMeshFactory` 是 `internal`，仅 `Planet.Solar` 内部使用。其他未列入契约的类型即使目前为
`public`，也视为程序集内部实现；需要新的数据源时，先在主线确定接口，再由调用方注入或把最小
接口放入 `Planet.Procedural`，不要在 `Planet.Solar` 中直接引用下游程序集。

编辑器工具（`Assets/Editor/`）**故意留在 `Assembly-CSharp-Editor`**，不另立 asmdef：它们不参与
构建产物，而多一个 asmdef 就多一个合并冲突点。

### 与星球框架的接口只有两处

`CelestialBody` 是星系程序集与程序化星球框架之间**唯一**的接触点，它只用到：

- `Planet.Procedural.IPlanetRadiusProvider` —— 由 `CelestialBody` **实现**，供壳层反查半径；
- `PlanetSurfaceView` 的 `definition` / `BuildSurface(float)` / `SetRadius(float)` / `SurfaceTransform`。

半径**只朝一个方向流动**：`CelestialBody` 发布 `RadiusUnits`，大气壳与云壳各自在
`ApplyProperties()` 里自取。`CelestialBody.ApplyState` **不再**反向推送
`PlanetAtmosphere` / `PlanetCloudLayer` —— 那会要求星系程序集引用壳层组件，而 `PlanetAtmosphere`
位于 `Planet.Procedural` 程序集，Solar 程序集不需要引用具体渲染实现。

> **给其他工作树的提醒**：只要不动上面这两个接口，`Planet.Procedural` 内部怎么改都不会波及
> 星系程序集。若确实需要改动 `IPlanetRadiusProvider`，或 `PlanetSurfaceView` 这四个成员，
> 或者星系程序集需要**新的数据源**，请先在主工作树把接口定下来再动分支。
