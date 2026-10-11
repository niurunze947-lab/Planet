# Planet — 地球按视场高度分级的派生贴图

> 代码：`Assets/Scripts/Procedural/PlanetTextureLodSet.cs`、`PlanetTextureLodController.cs`（程序集 `Planet.Procedural`）、烘焙菜单 `Assets/Editor/EarthTextureLodBuilder.cs`，档位资产与派生资源 `Assets/Resources/Solar/BodyMaterials/EarthLod/`。
> 上游：[doc.md](../doc.md)；档位判据的比例由 [DEVELOPMENT-WORKFLOW.md](DEVELOPMENT-WORKFLOW.md) §3 定义，本文只给符号名与阈值、不复述契约；采样与云密度场见 [PLANET-PROCEDURAL.md](PLANET-PROCEDURAL.md) §6–§8、§12，「现象 → 结论」见 [PLANET-PITFALLS.md](PLANET-PITFALLS.md) §4。

## 1. 高度与档位

判据是 [DEVELOPMENT-WORKFLOW.md](DEVELOPMENT-WORKFLOW.md) §3 契约里的 `clearanceOverRadius`——**以天体半径为单位**的离地比例，与地表模式闸门用的 `clearanceOverView` 是两套不同比例，不要互换。
相机侧符号是 `SolarSystemCamera.SurfaceClearanceRatio`；`UpdateView` 的唯一调用方是 `SolarSystemVisualCoordinator.Refresh()`（运行时由 `LateUpdate` 驱动）：`earthTextures.UpdateView(cameraRig.SurfaceClearanceRatio, focusBody == "Earth")`，只传比例与焦点；同一比例也喂给各天体的 `PlanetMeshLodController.UpdateView`（几何档位）。这套阈值**有消费者**，不是预留项。

| 档位 | 进入（`clearanceOverRadius`） | 退出／降档 | Albedo 每面 | 云密度每面 | 法线每面 |
|---|---|---|---|---|---|
| System | 焦点离开 Earth，或比例 ≥ 4.8（`orbitExit`） | — | 128 | 64 | 无 |
| Orbit | ≤ 4（`orbitEnter`） | ≥ 4.8（`orbitExit`） | 256 | 128 | 无 |
| Near | ≤ 1（`nearEnter`） | ≥ 1.2（`nearExit`） | 512 | 256 | 无 |
| Surface | ≤ 0.25（`surfaceEnter`） | ≥ 0.35（`surfaceExit`） | 1024 | 512 | 无 |

阈值是 LOD Set 资产上的序列化字段，与 `PlanetMeshLodSet` 共用同一套焦点与滞回判据：`SelectTier(ratio, focused, previous)` 单次调用最多升降**一档**，越档靠连续几帧；未聚焦 Earth 或比例为 `NaN`／`±Inf` 时直接回 System 档、不参与滞回；比例与显示尺度无关（1× 与 400× 同比例同档）。
焦点切走立刻请求 System 档，高档异步加载途中焦点又变了就丢弃该结果，而不是先应用一帧。
这里只换贴图预算：不发布 mode 已切换、不改时间流速、不建立近地玩家坐标——闸门与状态机见 [DEVELOPMENT-WORKFLOW.md](DEVELOPMENT-WORKFLOW.md) §3.1–§3.2。

## 2. 烘焙与缓存

四档都出自同一个 `PlanetDefinition` 与纯函数采样器，提高精度不改大陆、水位与种子。
Orbit 档直接引用基线两张 Cubemap（`PlanetFrameworkBuilder.AlbedoPath` / `CloudDensityPath`，256 / 128），与 [PLANET-PROCEDURAL.md](PLANET-PROCEDURAL.md) §10 共用缓存键 `PlanetBake:<ContentFingerprint>`，因此不另烘一份，历史 GUID 与场景／材质引用保持不变；其余派生 Cubemap 的键是 `PlanetLod:1:<指纹>:<地图类型>:<尺寸>`。`EarthTextureLods.asset` 只存尺寸与 Resources 路径，避免把全套大纹理序列化成场景常驻引用。

重建判据是可核对的：资产缺失、宽度不等于档位尺寸、或 `AssetImporter.userData` 不等于上面的键（改采样参数改的是指纹；键里的版本号一动则全部重烘），命中就逐个跳过；资产已存在时先烘到临时对象再 `EditorUtility.CopySerialized` 覆盖，**不删除重建**，GUID 因此保留。
像素缓冲由 `PlanetSurfaceBaker.BakeFacesAsync` 在 worker 线程按面并行产出，Texture 创建、`Apply` 与资产写入留在编辑器主线程，一次 1024 烘焙不会长期占满主线程；`SessionState` 标记让中途域重载后重跑菜单即可接着走。
烘焙期间源定义指纹变了就停止写入并要求重跑——给旧采样结果标新指纹比失败更坏。

派生 Cubemap 保留 mip、上传后不保留可读 CPU 镜像（`Apply(true, true)`），都在线性空间；四档的 `normalSize` 当前都是 0，球面法线由几何与运行时区块提供，绑一张全球法线贴图反而会覆盖它，目录里早期烘焙的法线 Cubemap 已是残留。
颜色高频仍受地图分辨率限制，没有编造真实地理数据。

## 3. 运行时绑定与预算

controller 用 `Resources.LoadAsync` 请求当前档位的 albedo 与云密度，就绪后一次性 `Apply` 原子替换；绑定经 `PlanetSurfaceView.SetVisualTextures` 走 `MaterialPropertyBlock`（`_AlbedoCube`、`_CloudDensityCube`、`_NormalEnabled`），**不写**共享材质与定义资产。云壳同步换成同一份密度（`PlanetCloudLayer.densityCubemap` + `Refresh()`）。
旧档位解除引用后 `Resources.UnloadAsset`，但基线两张与视图自身字段引用的贴图永不卸载，所以峰值不是单档而是「当前档 + 上一档 + 基线」短暂共存；首个异步档位就绪前仍绑序列化的基线（即 Orbit 档那两张）。

每张 RGBA32 含 mip = 尺寸² × 6 面 × 4 B × 4/3，逐档相加（尺寸见 §1）：System ≈ 0.625 MiB、Orbit ≈ 2.5 MiB、Near ≈ 10 MiB、Surface ≈ 40 MiB。
System 档的尺寸刻意低于基线，是远距离下拿画质换显存的取舍。

贴图精度**不能**替代几何 LOD：几何档位表 `PlanetMeshLodSet` 最高档仍是每面 96 段，1024 Cubemap 每纹素覆盖仍是十 km 量级，不代表米级地表地图。
贴地漫游还需要分块地形、流式 mesh、碰撞与局部坐标，不在本档位表的职责内。

## 4. 操作与验证

编辑模式打开 `SolarSystem.unity`，跑 `Tools ▸ Planet ▸ Build Earth Texture LODs`（Earth 定义 Inspector 上的 `Refresh Earth Cloud and Overview Textures` 是同一入口）。改了 Terra 就重跑，命中的资产逐个跳过。
该菜单写 LOD Set、给 Earth 挂 controller 并存场景；`Tools ▸ Planet ▸ Build Solar System Materials` 在 LOD Set 仍在时也会重新挂接，场景重建不必手工接线。

Play 模式只需核这四条不变量（不记数字）：比例进档时绑定的 albedo 与云密度尺寸等于 §1 表；共享材质与定义资产里的贴图引用不被改写；拉远或切走焦点后回到基线档、高档缓存被卸载且无残留；新派生 Cubemap 的 `isReadable` 为 `false`。

覆盖面：`Assets/Tests/EditMode/PlanetTextureLodTests.cs` 钉死四档选择、Surface 与 Near 的滞回不抖动、未聚焦／`NaN`／`Inf`／超大比例一律回 System 档、1× 与 400× 同比例同档，以及网格档位表共用同一套焦点与滞回；异步加载与释放**没有**自动化覆盖，只能靠 Play 模式观察。
近景分块地形由 `RuntimeTerrainTests.cs` 覆盖，是另一条通路；烘焙耗时随机器变化，本机首次全量是数百秒量级（最贵的是最高档 albedo）。

## 5. 云层唯一性

不变量：Earth 的可见透明云层**只有一层**——`PlanetCloudLayer` 只保留第一个 `CloudVolume` renderer，同一 Earth 下额外的 `CloudVolume` 一律 `SetActive(false)`（`DisableDuplicateCloudVolumes`）。
同一份密度场本来有两个消费者：云壳（`_DensityCube`）与地表着色器的云影（`_CloudShadowEnabled`）；同时可见就会把同一个场画两遍——一层透明云壳，加一层看起来不随地球转动的暗纹。因此运行时绑定把地表 `_CloudShadowEnabled` 恒置 0（`SetVisualTextures`），正式场景的 `ProceduralTerraSurface.mat` 也是 0；密度资源保留给未来云影模式与分析用。
云壳是 Earth 的子物体、按 body-local 方向采样，所以跟随天体姿态旋转。判断现象见 [PLANET-PITFALLS.md](PLANET-PITFALLS.md) §4。
