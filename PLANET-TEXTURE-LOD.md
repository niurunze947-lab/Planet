# Planet — 地球按视场高度分级的派生贴图

> 更新：2026-10-04。遵守 DEVELOPMENT-WORKFLOW 的中心距和离地比例契约；相机输入、焦点、模式不由本模块修改。

## 1. 高度与档位

`CurrentViewHeightUnits` 是相机到焦点中心的距离；`SurfaceClearanceRatio` 是
`max(viewHeight - surfaceRadius, 0) / surfaceRadius`。这里没有把 HUD 显示的视锥截面高度、
网格地形顶点距或大气壳半径代入。比例在 1× 与 400× 显示尺度下不变。

| 档位 | 进入条件（焦点为 Earth） | 退出／降档条件 | Albedo 每面 | 云密度每面 | 法线每面 |
|---|---|---|---|---|---|
| System | 离开 Earth 或离地比例很大 | 离地比例 ≤ 4 | 128 | 64 | 无 |
| Orbit | 离地比例 ≤ 4 | 离地比例 ≥ 4.8 | 256 | 128 | 无 |
| Near | 离地比例 ≤ 1 | 离地比例 ≥ 1.2 | 512 | 256 | 256 |
| Surface | 离地比例 ≤ 0.25 | 离地比例 ≥ 0.35 | 1024 | 512 | 512 |

Surface 的 0.25／0.35 与 interaction 的 debug 待加载边界一致，但这里仅切换贴图预算，
不发布 mode 已切换、不改变时间流速或建立近地玩家坐标。各档阈值保存在可编辑的 LOD Set 资产。
焦点切到其他天体立即请求 System；如果高档尚在异步加载，完成后丢弃过期请求。

## 2. 烘焙与缓存

所有档位仍来自同一个 ProceduralTerra 和纯函数采样器，增加精度不改大陆／水位／种子。
Orbit 复用 `ProceduralTerra_Albedo.cubemap`（256）和 `ProceduralTerra_CloudDensity.cubemap`（128），
保留历史 GUID。其他资源放在 `Assets/Resources/Solar/BodyMaterials/EarthLod/`。
`EarthTextureLods.asset` 持有尺寸和 Resources 路径，避免把全套大纹理序列化为场景引用导致全部常驻。

Albedo 和云密度按面并行生成，近景法线使用同一采样器的固定米制差分，
编码为 body-space RGB，运行时采样后转世界法线。法线不是颜色贴图，所有 Cubemap 都使用线性空间。
新派生 Cubemap 保留 mip，并释放可读 CPU 镜像。颜色高频仍受地图分辨率限制，没有编造真实地理数据。

CPU worker 只产生像素缓冲；Unity Texture 上传与资产写入留在主线程。编辑器更新轮询任务完成，
不让一次 1024 烘焙长期占满主线程。烘焙时避免另行触发脚本重载，中途重载可以再次运行菜单恢复，
已完成资源会按指纹跳过。Fingerprint 由源定义、LOD 烘焙版本、地图类型与尺寸组成。
编辑器 SessionState 标记支持域重载后自动继续；若烘焙期间源定义被修改，停止写入并要求重新构建，
避免给旧采样结果标上新定义的指纹。
成功后用 CopySerialized 覆盖已有资产，写入并重新导入 importer 元数据以保留 GUID。

## 3. 运行时绑定与预算

`SolarSystemVisualCoordinator` 从公开相机契约取得离地比例，调用 `PlanetTextureLodController.UpdateView`。
controller 使用 `Resources.LoadAsync` 请求当前档位，三个纹理准备好后原子替换。
`PlanetSurfaceView.SetVisualTextures` 通过 MaterialPropertyBlock 绑定，不修改共享材质或定义资产。
地表云影和云壳同步使用选中的同一份密度；旧档位被解除引用后 `Resources.UnloadAsset` 释放。
首个异步档位加载前继续使用原 Orbit 贴图，避免空白地球。

RGBA32 含 mip 的预算：System 约 0.625 MiB；Orbit 约 2.5 MiB；Near 含法线约 12 MiB；
Surface 含法线约 48 MiB。旧 Orbit 基线是序列化的兼容资源，仍可能保留约 2.5 MiB；
加载时旧档与新档短暂共存。资源库总尺寸与运行时常驻显存要分别看待。

提高贴图精度不能替代几何 LOD：地球网格仍每面 96 段，近景仍可能看见面片。
法线可以补回未被网格承载的坡度；完整贴地模式还需要分块地形、流式 mesh、碰撞和局部坐标，
这些在本轮没有实现。1024 Cubemap 每纹素覆盖仍是十 km 量级，不代表米级地表地图。

## 4. 操作与验证

在编辑模式打开 SolarSystem，运行 **Tools → Planet → Build Earth Texture LODs**。
修改 Terra 后重新运行；缓存命中跳过烘焙。完整场景重建时，有已生成 LOD Set 则重新挂接 controller。
长期测试覆盖四档选择、滞回、离开 Earth、无效视距和比例一致性；运行时将核查异步完成与纹理释放。

2026-10-04 验证：41 项程序化 EditMode 测试通过；原 Terra 两张缓存 GUID 保留，
四档共 10 张地图引用可加载、尺寸正确，8 张新增缓存 `isReadable=false`。
Play 模式在离地比例约 0.15 下实际绑定 1024／512／512，云壳与地表云影使用同一密度，
共享地表材质的 Albedo 始终是原 256。拉远后 System 档为 128／64，扫描已加载对象未发现残留高档 Earth 缓存。
本机首次增量烘焙耗时约 948.7 秒，包含最高档法线的固定米制差分；缓存命中耗时另见核查记录。

## 5. 云层唯一性

Earth 的可见云层只有 `PlanetCloudLayer/CloudVolume` 一个透明壳。地表材质仍可绑定云密度，
但正式场景已关闭 `_CloudShadowEnabled` 的可见叠加，避免把同一密度场同时显示为透明云壳和一层
看起来不随地球转动的暗纹。该密度资源仍保留给未来云影模式和云层分析使用。
`PlanetCloudLayer` 会禁用同一 Earth 下额外的 `CloudVolume` renderer；Play 验证结果为
`PlanetCloudLayer=1`、活动 `Planet/CloudVolume` renderer=1、地表 `_CloudShadowEnabled=0`。
云壳保持 Earth 的子层级并以 body-local direction 采样，因此跟随天体姿态旋转。
