# Planet — 踩坑总表

> 全工程**唯一**的「现象 → 结论」清单。按子系统分组，不按文档归属分组。
> 上游：[DOCS.md](DOCS.md)。机制解释在各专题文档，本表只负责「见到这个现象时该想到什么」。
>
> 加条目时只写三件事：**现象**、**根因或正确做法**、**去哪看细节**。
> 不写"某年某月某分支发生过"——那是 [DEVLOG.md](DEVLOG.md) 的事。

---

## 1. 数值、坐标与原点

| 现象 | 结论 | 详见 |
|---|---|---|
| 远处地形悄悄变成一个大平面，没有任何报错 | **先降 float 再相减**是错的。50 AU 处 float32 步长 524 km，1 m 会被整个抹掉。宇宙坐标必须在 `double` 里、在**拥有该点的坐标架**里相减；`RenderOrigin.ToRender` 是全局唯一降 float 点 | [PLANET-SPACE.md](PLANET-SPACE.md) §2 |
| 时间流速调高后行星飞出画面 | 浮动原点写成「向焦点位置指数平滑」会留下 `速度 × 时间常数` 的稳态误差：5 天/秒时只有 4 个单位看不出问题，36,500 天/秒时涨到 30,000 单位。正确写法是**刚性跟随 + 只衰减不反馈的残差** | [PLANET-SPACE.md](PLANET-SPACE.md) §2.2 |
| 切换焦点永远是硬切，"平滑跟随"像没生效 | `RigidFollow` 的残差从没被播种：`_lag` 只有 `_lag * exp(...)` 一处赋值，衰减逻辑是死代码。必须由 `Reanchor` 把当前偏移塞进残差 | [PLANET-SPACE.md](PLANET-SPACE.md) §2.2 |
| 「1 m 以内免费」这句话不成立 | `DriftPenalty` 用了 `Math.Max(drift, 1e-9)`，把基准从 1 m 挪到了 1 nm，零漂移处返回 2⁻³⁰。应为 `Math.Max(drift, 1.0)` | [PLANET-SPACE.md](PLANET-SPACE.md) §2.2 |
| 相机离得越远，网格反而越贵 | `SelectLevel` 取反了：规则写成「满足容差的**最细**一层」且循环带 `break`，几何误差随层数变小使判据单调，`break` 第一次就触发——10 m 到 10⁹ m 全返回 L0，LOD 阶梯等于不存在。正确是取**满足容差的最粗一层**。回归测试把方向钉死 | [PLANET-SPACE.md](PLANET-SPACE.md) §4 |
| 换了网格段数之后光照"变了" | 不该发生。法线由**固定物理步长**的中心差分给出，与网格密度无关，网格变密只是把同一法线场采样得更细。若真变了，改的是 `slopeSampleDistanceMetres` | [PLANET-PROCEDURAL.md](PLANET-PROCEDURAL.md) §7 |
| 改了脚本里的默认值，运行值没变 | `SolarSpaceContext.maxChunkLevel` 这类字段是**序列化**的：场景里存的值会覆盖代码默认值，改脚本毫无效果。改完必须**存场景**（Play 模式存不了），再用 **Audit Space Scale** 从报告里核对实际生效值 | [PLANET-SPACE.md](PLANET-SPACE.md) §9 |

## 2. 分块与接缝

| 现象 | 结论 | 详见 |
|---|---|---|
| 接缝检验报出 `4601025967313136703 ULP` 的"裂缝" | **误报**，是测量方法本身错了：按索引配对同一条边，而跨面时共享边是**反向**遍历的。必须比**集合相等**。工具里现在显式统计"反向遍历"的条数 | [PLANET-SPACE.md](PLANET-SPACE.md) §3.2 |
| 某条缝上有裂缝，其它缝都正常 | 手写 24 条边的邻接表几乎不可能一次写对。改为**翻过边界、在父面的 uv 方形里继续走一步**，让投影公式自己给出结果 | [PLANET-SPACE.md](PLANET-SPACE.md) §3.2 |
| 换个焦点后网格跑到 6,000 km 之外 | `ChunkLocalPoint` 必须把**块的地址**和**块内的 float 偏移**放在同一个值类型里。分开存，调用方迟早留下 float 而丢掉原点 | [PLANET-SPACE.md](PLANET-SPACE.md) §8 |

## 3. 流式加载

| 现象 | 结论 | 详见 |
|---|---|---|
| `stats.pending` 比队列里实际的数量少一个 | 游标在 `TryAcquire` **之前**自增，那个刚失败、明明还在队列里的请求被漏算。只有**成功**的请求才推进游标 | [PLANET-SPACE.md](PLANET-SPACE.md) §5 |
| 调 `ChunkStreamer` 的参数对画面毫无影响 | 正常：没有任何运行时路径构造 `ChunkStreamer`（只有测试用）。当前真正在跑的分块地表是 `Planet.Procedural.Terrain.RuntimeTerrainRenderer`，它自带一套 `TerrainLattice` 选择与构建，**不经过** `IChunkContentProvider` | [PLANET-SPACE.md](PLANET-SPACE.md) §11、[PLANET-PROCEDURAL.md](PLANET-PROCEDURAL.md) §16 |

## 4. 程序化采样与缓存

| 现象 | 结论 | 详见 |
|---|---|---|
| 改了噪声 / 阈值 / 公式，地形纹丝不动 | 网格与 Cubemap 的缓存判定只看内容指纹；序列化参数没变则指纹不变。必须提升 `PlanetDefinition.GeneratorVersion`。失效方式是"旧地形套着新参数的颜色"，且不会有任何报错 | [PLANET-PROCEDURAL.md](PLANET-PROCEDURAL.md) §5 |
| 重烘一次，场景与材质里的引用全部变 Missing | 派生资产必须用 `EditorUtility.CopySerialized` **覆盖**旧资产，不能删除重建 | [PLANET-PROCEDURAL.md](PLANET-PROCEDURAL.md) §10 |
| 紧接着再构建一次，读到的还是旧指纹 | 写完指纹元数据后必须重新导入该派生资源，否则 importer 的内存状态没同步 | [PLANET-MATERIALS.md](PLANET-MATERIALS.md) §2 |
| 烘焙中途改了源定义，产物被标上了新定义 | 应停止写入并要求重新构建——给旧采样结果标新指纹比失败更坏 | [PLANET-TEXTURE-LOD.md](PLANET-TEXTURE-LOD.md) §2 |
| 400× 下大气 / 云与地表的比例对不上 | 早期版本给壳层强制了最小直径 0.1。现在**不设固定最小直径**，半径统一由 `IPlanetRadiusProvider` 提供 | [PLANET-PROCEDURAL.md](PLANET-PROCEDURAL.md) §13 |
| 场景刚打开时看不到大气 | 刻意行为：半径由模拟写入，第一帧之前壳层是隐藏的，不是丢失 | [PLANET-PROCEDURAL.md](PLANET-PROCEDURAL.md) §13 |
| 地表出现闪烁的半圆盘与直线阴影边界 | URP 级联阴影在天体尺度下失效。渲染器彻底关闭阴影投射与接收，晨昏改用**扁球面法线**判定 | [PLANET-PROCEDURAL.md](PLANET-PROCEDURAL.md) §11 |
| 云壳投出半圆形的直线阴影 | 云壳 ShadowCaster 关闭；云影改走共享密度 Cubemap 的偏移采样 | [PLANET-PROCEDURAL.md](PLANET-PROCEDURAL.md) §12 |
| 调云壳材质的 `_InnerRadius` 没有任何变化 | 着色器未使用该属性；壳层厚度实际由网格缩放决定 | [PLANET-PROCEDURAL.md](PLANET-PROCEDURAL.md) §12 |
| 调大气材质的 `_Density` / `_RimPower` 没有任何变化 | 组件用 `MaterialPropertyBlock` 逐实例覆盖这些属性；实际值来自**组件字段**，改材质上的同名属性无效 | [PLANET-PROCEDURAL.md](PLANET-PROCEDURAL.md) §12 |
| 同一片云同时显示为透明云壳和一层不随地球转动的暗纹 | 同一个密度场被地表云影和云壳各画了一次。正式场景关闭地表 `_CloudShadowEnabled` 的可见叠加，Earth 只保留一个透明壳 | [PLANET-TEXTURE-LOD.md](PLANET-TEXTURE-LOD.md) §5 |
| 山脉长在海岸线上 / 沙漠出现在奇怪的纬度 | 这是刻意设计：`interior` 遮罩把山地限制在大陆内部，`\|y\| = 0.43` 的干燥带对应地球副热带干旱带。先读机制再改 | [PLANET-PROCEDURAL.md](PLANET-PROCEDURAL.md) §6–§7 |
| 以为提高贴图精度就能当近景地图用 | 贴图精度**不能**替代几何 LOD：网格仍是每面 96 段，1024 Cubemap 每纹素仍是十 km 量级 | [PLANET-TEXTURE-LOD.md](PLANET-TEXTURE-LOD.md) §3 |

## 5. 恒星与光照

| 现象 | 结论 | 详见 |
|---|---|---|
| 远景太阳表面闪烁 | 用屏幕导数判断米粒是否可分辨，过滤未分辨的温度起伏。512 px 全盘下**一个像素覆盖数千 km**，本来就不可能显示每个 900 km 米粒——放大局部才能看清，不要为了"看得见"把真实粒度改成全盘大斑 | [PLANET-STELLAR.md](PLANET-STELLAR.md) §2 |
| 按 IAU 公布的极点算转轴倾角，得到荒谬的值 | IAU 极点**不总是**自转矢量。规则是「逆向自转**且**极点位于黄道北侧」才翻转：金星 2.64°→177.36°、天王星 82.24°→97.76°；冥王星极点本身在南侧，它**就是**自转矢量，再翻一次会得到荒谬的 60°，正确答案是 122.5° | [PLANET-SOLARSYSTEM.md](PLANET-SOLARSYSTEM.md) §2 |
| 火星倾角算成 26.71°，教科书是 25.19° | HUD 的转轴倾角是自转轴与**该天体自身轨道面法线**的夹角，不是与黄道北极的夹角 | [PLANET-SOLARSYSTEM.md](PLANET-SOLARSYSTEM.md) §2 |
| 把光球显示的红色当成太阳真实的可见光颜色 | `focusedExposureEV` 是**观测响应**，模拟窄带/高动态范围观感；物理黑体温度与 `T⁴` 计算仍保留在 HDR 缓冲里。自动曝光与镜头光学尚未实现 | [PLANET-STELLAR.md](PLANET-STELLAR.md) §5 |
| 直接改 `SunPhotosphere.mat` 上的曝光属性，数值和文档对不上、改完也没反应 | 运行时由 `StellarSurfaceView` 通过 `MaterialPropertyBlock` 覆盖，真值来自 `StellarDefinition`；材质的初值又是构建器按定义写进去的。所以**定义才是权威**，手改材质上的同名属性无效。着色器自身的属性默认值与定义默认值并不相同，不要拿它当参考 | [PLANET-STELLAR.md](PLANET-STELLAR.md) §5 |

## 6. 资产与贴图导入

| 现象 | 结论 | 详见 |
|---|---|---|
| 天空盒只有一半有星星，`-Z`/`-X`/`-Y` 全黑 | 贴图本身是完整全天球等距圆柱投影，**问题一定在采样端**，不要重新下载或裁剪贴图。老材质用内置 `Skybox/Panoramic`，它的 `_ImageType` 被设成 180° 就会这样；现在换成工程自有的 `Planet/StarfieldPhysical`（直接用视线方向求经纬度），结构上不可能再半包围 | [PLANET-SOLARSYSTEM.md](PLANET-SOLARSYSTEM.md) §5 |
| 改单个 `wrapMode` 字段"没生效" | 单值字段在重新导入时会被 Unity 覆盖回默认值。必须**逐轴**设 `wrapModeU/V/W`（U=Repeat、V=Clamp、W=Clamp）并调 `SaveAndReimport()` | [PLANET-SOLARSYSTEM.md](PLANET-SOLARSYSTEM.md) §5 |
| 天极出现接缝 | V 轴（纬度）必须 `Clamp`，环绕到极点就错了 | [PLANET-SOLARSYSTEM.md](PLANET-SOLARSYSTEM.md) §5 |
| 经度 ±180° 处一条竖向暗线 | `atan2` 在此跳过一个整周期，普通 `tex2D` 的隐式求导把它当成巨大纹理足迹，选到最模糊的 mip。需要在 `frac` 包装前求导、对经度导数取最短周期差（`delta -= round(delta)`）后用 `tex2Dgrad` | [PLANET-SOLARSYSTEM.md](PLANET-SOLARSYSTEM.md) §5 |

## 7. 编辑器与工具

| 现象 | 结论 | 详见 |
|---|---|---|
| 失去焦点后 Play 模式几乎不推进（实测 9 分钟只跑 2 帧） | **Run In Background 必须保持开启**。Unity 编辑器默认关闭它，失焦时主循环整个停止：`Update()` / `LateUpdate()` 从不执行，一切"每帧做一次"的逻辑全部失效，而画面停在第一帧看起来还算正常。已设为 `true` 并保存，请勿改回 | [PROJECT-OVERVIEW.md](PROJECT-OVERVIEW.md) §2、§7 |
| 11 个机位出图时画面变成一圈圈同心甜甜圈 | `CapturePoses` 原本在一个循环里同步跑完 11 个机位；碎带标记走 `Graphics.DrawMeshInstanced`，提交会**保留到帧末**，于是 11 个不同浮动原点的同一片碎带叠在一帧里画出来。必须**一个编辑器 tick 只处理一个机位** | [PLANET-SOLARSYSTEM.md](PLANET-SOLARSYSTEM.md) §6 |
| 出图后编辑器被踢出 Play 模式，所有 `Instance` 变 null | `CapturePoses` 末尾不能调 `AssetDatabase.Refresh()`——它会重载域。刷新已拆成独立菜单项 | [PLANET-SOLARSYSTEM.md](PLANET-SOLARSYSTEM.md) §6 |
| 新建的 asmdef / 脚本，Unity 说找不到 | Unity 窗口**失去焦点时不会自动刷新资源库**，显式调一次 `AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate)` 即可 | [PROJECT-OVERVIEW.md](PROJECT-OVERVIEW.md) §7 |
| 改 `SolarSpaceContext.maxChunkLevel` 这类序列化字段，重启就丢 | **Play 模式下改不了场景**：`MarkSceneDirty` / `SaveScene` 会抛 "This cannot be used during play mode"。先停止播放 | [PLANET-SPACE.md](PLANET-SPACE.md) §9 |
| 把测试脚本和 `Planet.Procedural.Tests` 放同一目录，导入直接失败 | **Unity 不允许同一目录下存在两个 asmdef**，报 `contains multiple assembly definition files`。每个测试程序集必须各占一个子目录 | [DEVELOPMENT-WORKFLOW.md](DEVELOPMENT-WORKFLOW.md) §2 |

## 8. MCP 与多工作树

| 现象 | 结论 | 详见 |
|---|---|---|
| 以为自己改的是本工程，其实改的是另一个工作树 | MCP 客户端会**记住上次连接的实例**。四个工作树的场景与脚本在同一基线下，读出来的结构完全一样，**不专门查 `Application.dataPath` 根本发现不了**。驱动前先查 `Application.dataPath`，再 `set_active_instance "<projectName>@<hash>"` | [PROJECT-OVERVIEW.md](PROJECT-OVERVIEW.md) §7 |
| 端口表里查到的实例，中途换了 | MCP 客户端会在会话中途**换**实例，端口表只在启动时可靠。在 `execute_code` 里加一道 `Application.dataPath` 断言，并核对程序集 `asm.Location` 落在哪个工作树 | [PROJECT-OVERVIEW.md](PROJECT-OVERVIEW.md) §7 |
| `read_console` 超时、`refresh_unity` 后无响应 | 多半是 Unity 弹了「open scene(s) have been modified externally」**模态框**（`git merge` 在磁盘上重写了正开着的场景）。点 Reload 即可恢复；合并前先关掉待合并的场景更稳 | [DEVELOPMENT-WORKFLOW.md](DEVELOPMENT-WORKFLOW.md) §6 |
| Unity 给某文件补了一份 GUID 不同的 `.meta` | 自生成的 GUID 与工程提交版**不同**。合并带入 `.meta` 前，先比对两侧并删掉自生成的那份 | [DEVELOPMENT-WORKFLOW.md](DEVELOPMENT-WORKFLOW.md) §6 |
| `Enum.GetValues<T>()` / 表达式体成员 / 插值字符串报错 | MCP 的 `execute_code` 走 CodeDom（C# 6）。测试代码走正常编译，不受此限 | [PROJECT-OVERVIEW.md](PROJECT-OVERVIEW.md) §7 |
| 审计报告里出现 `1.0×10¹⁶ px` 这种荒唐数字 | 编辑器相机正好落在原点上时 `2r/d` 会算出这个值。现在距离小于 1 m 就打印 `on the body`——把"测不准"和"测出来是这样"分开写，比在表里放一个荒唐数字好 | [PLANET-SPACE.md](PLANET-SPACE.md) §9 |
| `import error Code:(4)` / `Refresh recovered after Unity disconnect` | 用 `refresh_unity(scope: "all", wait_for_ready: true)` 重试，不是代码问题 | [PLANET-SPACE.md](PLANET-SPACE.md) §9 |

## 9. 程序集与仓库

| 现象 | 结论 | 详见 |
|---|---|---|
| 加一条 asmdef 引用后立刻循环依赖，或和其它工作树逐行冲突 | 依赖方向必须单向 `Assembly-CSharp → Planet.Solar → Planet.Procedural`。`Planet.Solar.asmdef` 的 `references` 是**穷举**的，加任何一条都会和同名 asmdef 的改动逐行冲突。需要新数据源时先把接口定下来 | [DEVELOPMENT-WORKFLOW.md](DEVELOPMENT-WORKFLOW.md) §2 |
| `Planet.Solar` 里任何用 `double3` 的调用都报 `CS0012` | 该程序集**不得引用 `Unity.Mathematics`**。`CubeFaceMath.ReferenceRadiusMetres` 之所以在星系侧重新实现了一遍参考半径，就是为了不依赖 `Planet.Procedural` 里那个吃 `double3` 的版本 | [DEVELOPMENT-WORKFLOW.md](DEVELOPMENT-WORKFLOW.md) §2.1 |
| 想让 `Planet.Solar` 直接调用 `PlanetAtmosphere` | asmdef 程序集在结构上引用不到 `Assembly-CSharp`（方向是反的）。所以 `CelestialBody` 不直接碰壳层，而是实现 `IPlanetRadiusProvider` 让壳层自己来取 | [DEVELOPMENT-WORKFLOW.md](DEVELOPMENT-WORKFLOW.md) §2 |
| 以为星球半径换算是启动慢的原因 | 不是。实测半径换算 1000 帧 × 21 天体只需 0.32 ms。慢的是程序化网格生成（`HideFlags.DontSave`，每次进 Play 重算） | [PLANET-PROCEDURAL.md](PLANET-PROCEDURAL.md) §16 |
| 调试输出、一次性截图探针、临时场景混进了提交 | 只允许带统一事件名和可关闭开关的调试输出进入工作提交；一次性脚本、临时材质、`Library/` 都不进权威提交。审计报告写入 `Library/`（已被 `.gitignore` 排除），但审计**工具本身**与其 `.meta` 要入库 | [DEVELOPMENT-WORKFLOW.md](DEVELOPMENT-WORKFLOW.md) §5 |
