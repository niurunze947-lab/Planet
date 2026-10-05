# REPORT：planet-definition 与 interaction 工作成果、合并须知与并入 main 调研

> **交接单（临时文档）**。按 [DOCS.md](DOCS.md) §6 / [DEVELOPMENT-WORKFLOW.md](DEVELOPMENT-WORKFLOW.md) §8.2，
> 随两条工作树并入 `main` 后删除。结论若要长期保留，并入 WORKFLOW §6.1。
>
> 探测时间：2026-10-05。探测方式：`git worktree list` / `git log` / `git diff` / `git status`
> 对照 main 与各工作树工作区；**按哈希与工作区内容认定，不按分支名**（WORKFLOW §1）。

---

## 0. 探测结论一览

| 工作树 | 路径 | 分支 | HEAD | 相对 main | 工作区 |
|---|---|---|---|---|---|
| 主线 | `D:/UnityProjects/Planet` | `main` | `1271847` | — | 仅未跟踪截图 |
| planet-definition | `D:/UnityProjects/Planet-worktrees/planet-definition` | `codex/planet-definition` | `1271847` | **0 / 0**（已重合） | **大量未提交** |
| interaction | `D:/UnityProjects/Planet-worktrees/interaction` | `mimo/interaction` | `9f7febd` | **+3 提交** | 干净 |

关键判断：

1. **`codex/planet-definition` 的提交面已经并入 main**（分支 tip 与 main 同为 `1271847`，
   历史上的 `3949919` / `7dab4d3` 已把 main 合入该分支并完成 `CelestialBody` / `SolarSystem.unity` 冲突合成）。
   工作树上真正「还欠 main」的，是**未提交的 WIP**，不是分支提交。
2. **`mimo/interaction` 的 C6 地表自由漫游尚未并入 main**，3 个提交、工作区干净，可直接按哈希合并。
3. 两边在 **相机所有权**（`SolarSystemCamera`）、**视距缩放**、**地表锚点** 上给出了两套平行方案，
   合并时必须**语义合成**，不能整文件覆盖。

---

## 1. planet-definition 工作成果

### 1.1 提交面（已在 main）

已合入的代表性提交（完整清单见 `git log --oneline --grep=planet-definition`）：

| 提交 | 内容 |
|---|---|
| `08962a7` | 纹理 LOD（Earth 四档）、离线 Mesh LOD（16/32/64/96）、恒星框架（StellarDefinition / Planck-CIE / 光球着色） |
| `3978d8f` | 地形细节参数：`PlanetDetailSampler` / `TerrainLattice` / `TerrainNodeKey` / `CubeFaceAdjacency` |
| `ff502fc` | 保留 `Terrain/` 目录元数据 |

文档面：`PLANET-TEXTURE-LOD.md`、`PLANET-STELLAR.md` 及相关章节。

### 1.2 未提交工作区（真正待并入的成果）

源码 diff（`*.cs` / `*.shader` / `*.hlsl` / `*.md`）：**19 个已跟踪文件，+443 / −80**；
另有未跟踪的新脚本与资源。按主题拆开：

#### A. 运行时地形区块（新子系统）

| 文件 | 作用 |
|---|---|
| `Procedural/Terrain/RuntimeTerrainChunk.cs` | 区块节点 |
| `Procedural/Terrain/RuntimeTerrainRenderer.cs` | 运行时区块渲染器（视距相关、有界生成） |
| `Procedural/Terrain/RuntimeTerrainSelection.cs` | 区块选择 |
| `Procedural/Terrain/RuntimeTerrainSource.cs` | 区块内容源 |
| `Shaders/PlanetSurfaceCommon.hlsl` | 表面着色共享 HLSL |
| `Tests/EditMode/RuntimeTerrainTests.cs` | 运行时地形回归 |
| `Tests/EditMode/PlanetTerrainClimateTests.cs` | 海陆／气候／云场回归 |

`PlanetSurfaceView` 新增 `useRuntimeChunks`（默认 true）：开启时全球离线网格让位，
由 `RuntimeTerrainRenderer` 接管；`meshLodSet` 降级为 “Legacy offline mesh cache”。

#### B. 显示起伏与位移公式改写（语义变更）

`PlanetSurfaceSampler` 新增统一入口：

```text
DisplacementMetres(height):
  有海：seaLevel + max(height − seaLevel, 0) × visualReliefScale + opticalHeight
  无海：height × visualReliefScale + opticalHeight
```

- `visualReliefScale` 范围 1–12 → **1–100**（Earth 示例 20×）。
- **物理高度、海平面、水深、海陆判定不乘显示倍率**；只改显示几何与法线。
- 海平面在夸张下保持固定，避免「放大后全球被淹」。
- 文档依据：工作树内 `PLANET-PROCEDURAL.md` §12 / §21.4。

> **硬约束（§21.4）**：globe 与 chunk 的可视位移必须统一走 `DisplacementMetres`。
> SolarSystem 工作树里同名方法仍是旧的「高度整体乘倍率」——合并时必须统一到本公式。

#### C. 云场与气候耦合

`PlanetDefinition.generation` 新增：

- `cloudTerrainInfluence` / `cloudOceanDensity` / `cloudLandDensity`
- `cloudMoistureInfluence` / `cloudAltitudeBias`
- `cloudOrographicInfluence` / `cloudUpwindDistanceMetres`

`PlanetSurfaceSampler.EvaluateCloudDensity`：类地表面读取同源高度／海陆／温湿度；
近海岸平滑过渡，迎风抬升＋雨影为静态统计近似。`cloudTerrainInfluence = 0` 恢复纯噪声场。

`GeneratorVersion` **4 → 5**；新增 `TerrainContentFingerprint()`（含 `detail` 与细节生成器版本）
供区块缓存失效。全球贴图仍用 `ContentFingerprint()`。

#### D. 定义编辑器与资源重烘

- 新增 `Editor/PlanetDefinitionEditor.cs`：Inspector + **Rebuild Selected Planet Surface**；
  Earth 同步刷新各档纹理与网格。
- Earth 云／法线／反照率 cubemap 与 `EarthTextureLods.asset` 已按 20× 重烘。
- `ProceduralTerra*.asset/cubemap` 有同步改动。

#### E. Mesh LOD 资源搬迁

- **删除** `Assets/Resources/Solar/BodyMaterials/MeshLod/**`（约 21 体 × 4 档网格 + 集合资产）。
- **未跟踪新目录** `Assets/Editor/LegacyPlanetMeshLod/` 存放旧资产。
- 另有 `Assets/Editor/LegacyPlanetMeshLod.meta`、`PlanetMeshLodBuilder.cs` 小改。

#### F. 相机／交互侧改动（跨 ownership，见 §3）

| 文件 | 摘要 |
|---|---|
| `Solar/SolarSystemCamera.cs` | **精确地表距离**：`HasPreciseSurfaceDistance` / `SetSurfaceClearanceMetres` / body-fixed 双精度位姿；近裁面跟 clearance，far 固定 40000 |
| `Solar/CelestialBody.cs` | `_proceduralSurface.SetRenderersVisible` |
| `Interaction/ViewDistanceController.cs` | 近地改走 `ApplySurfaceClearanceZoomMetres`；`terrainSafetyMetres` 2 → 0.01 |
| `Interaction/ViewDistanceBoundary.cs` | 新增 `ApplySurfaceClearanceZoomMetres`（本地高度不再叠中心距阻尼） |
| `Interaction/BodyFixedCameraAnchor.cs` | 精确地表距离模式下释放锚点 |
| `SolarSystemVisualCoordinator.cs` / `PlanetMeshLodController.cs` / shader | 小改接线 |
| `Tests/EditMode/Interaction/ViewDistanceBoundaryTests.cs` | 新增 2 个厘米缩放／加载减速测试 |

#### G. 文档

- `PLANET-PROCEDURAL.md`：位移公式、云场耦合、限制条目改写、**新增 §21 厘米尺度与区块接入评估**。
- `DOCS.md`：登记「地形关联云场、显示起伏倍率、厘米尺度…」唯一 owner。
- `DEVLOG.md` / `DEVELOPMENT-WORKFLOW.md`：状态改写为「反向并入 main 尚未执行」。

### 1.3 自报验证（工作树内文档记录）

> Unity 脚本编译通过、控制台无错误／警告；**133 项 EditMode 回归全部通过**；
> Earth 显示缓存按 20× 重烘。合并前应由主线复测，不能直接采信。

---

## 2. interaction 工作成果

工作区干净，成果全部在 3 个提交上（merge base `0f0c77d`）：

| 哈希 | 主题 | 要点 |
|---|---|---|
| `c28d2c3` | **C6 地表自由漫游** | RMB 视角 + RMB+QWEASD 移动；轮子视高缩放被抑制；HUD 显示离地米数；玩家真值为 **body-fixed 米制位姿**，经天体自转写回，星球不再绕相机转 |
| `9c22dcb` | 局部 ENU 自由视角 | 偏航绕**地表法线**（不再绕极轴）；俯仰对齐轨道拖拽惯例；轮子与 ± 调节飞行速度档（`10 m/s × 2^level`） |
| `9f7febd` | 裁面与速度键位修复 | 近 1e-6 / far 40000 的 4e10:1 视锥使 IMGUI 抛 `Screen position out of view frustum`；近远比钳到 **1e5:1**；漫游速度改为**仅滚轮**，避免与模拟时间倍率抢 `+/-` |

### 2.1 新增源码

| 文件 | 作用 |
|---|---|
| `Interaction/SurfaceRoamController.cs` | C6 控制器（~494 行）：接管相机、body-fixed 真值、视觉连续进入 |
| `Interaction/SurfaceRoamInput.cs` | 输入映射 |
| `Interaction/SurfaceRoamSpeed.cs` | 速度档与响应 |
| `Interaction/SurfaceLocalSpace.cs` | 局部 ENU / 离地距离 |
| `Interaction/SurfaceCameraClip.cs` | 裁面消杀（1e5:1） |
| `Solar/SurfaceRoamHudState.cs` | HUD 状态 |
| 对应 `.meta` 与 EditMode 测试（Clip / LocalSpace / Contract / Input / Speed / Mode 扩展） | |

### 2.2 修改共享面

| 文件 | 摘要 |
|---|---|
| `SolarSystemCamera.cs` | 新增 `freeRoam`；`ApplyTransform` / 轨道输入在 freeRoam 下直接返回；裁面公式重写 + 消杀 |
| `ViewDistanceController.cs` | `zoomEnabled`；freeRoam 时关闭视高缩放并自动挂 `SurfaceRoamController` |
| `ViewDistanceBoundary.cs` | 进入/离开比 1.524%/3.0% → **5%/8%**（Earth 约 335 km 高度可进入）；近地阻尼只在进入带内生效，避免「永远进不了地表模式」 |
| `BodyFixedCameraAnchor.cs` | freeRoam 时不抢相机 |
| `CelestialBody.cs` | 未烘焙半径 fallback `1f` → **`0f`**（避免假半径关掉近地闸门） |
| `SolarSystemHud.cs` / `SolarSystemSimulation.cs` | HUD 显示离地距离与飞行速度 |

测试增量约 **+1100 行**（含 `SurfaceRoamContractTests` 300 行）。

---

## 3. 合并须知（必须先读）

### 3.1 两套「近地相机所有权」互斥，必须合成

| | interaction（C6） | planet-definition（未提交） |
|---|---|---|
| 所有权开关 | `SolarSystemCamera.freeRoam` | `SolarSystemCamera.HasPreciseSurfaceDistance` |
| 位姿真值 | `SurfaceRoamController` 的 body-fixed 米制玩家位姿 | `SolarSystemCamera` 内 `_precisionBody` + `_preciseClearance` |
| 进入方式 | 地表闸门打开后切自由飞 | 精确高度 API 一旦被调用 |
| 裁面 | 全局消杀 1e5:1，far ≥ 40000（distance>1） | 精确模式下 near = clearance×0.1×unitsPerMetre，far=40000 |
| 缩放 | 轮子改速度档，视高缩放关闭 | 轮子仍改高度，走 `ApplySurfaceClearanceZoomMetres` |

**建议合成方向（调研意见，非已决）**：

1. 保留 **C6 `freeRoam` 作为交互所有权总开关**（输入地图切换）；
2. 保留 **精确高度作为度量真值**（厘米离地、地形感知 floor），由 `SurfaceRoamController`
   或 `ViewDistanceController` 驱动 `SetSurfaceClearanceMetres`；
3. 裁面：**先走 interaction 的 1e5:1 消杀**（修 IMGUI 崩溃），精确模式下允许更紧的 near，
   但仍必须过消杀；禁止出现无上限的 near/far 比；
4. `BodyFixedCameraAnchor`：freeRoam **或** 精确高度接管时都释放锚点（两条件取或）。

### 3.2 文本冲突热点（`git merge` 预期）

| 文件 | 冲突性质 | 解法原则 |
|---|---|---|
| `SolarSystemCamera.cs` | **双侧大段改写** | 按 §3.1 合成；输入抑制走 `freeRoam`，高度真值走 precision API |
| `ViewDistanceController.cs` | 双侧重写缩放路径 | freeRoam 时禁视高缩放；非 freeRoam 近地走 `ApplySurfaceClearanceZoomMetres`；`terrainSafetyMetres` 用 0.01（厘米语义） |
| `ViewDistanceBoundary.cs` | 阈值常量 + 新方法 | **阈值以 interaction 的 5%/8% 为准**（闸门可达）；新方法保留；同步改 WORKFLOW §3 阈值表（唯一真相） |
| `BodyFixedCameraAnchor.cs` | 同一行两套 early-return | 合成条件 |
| `ViewDistanceBoundaryTests.cs` | 双侧追加测试 | 两套都留；阈值断言改用新常量 |
| `CelestialBody.cs` | 不同方法 | 预期可自动合并；核对两侧 hunk 都在 |

### 3.3 Ownership 违规（合并时要补契约，不要悄悄改）

| 改动 | 协议条文 | 处理 |
|---|---|---|
| planet-definition 改 `Assets/Scripts/Interaction/*` | interaction 长期 ownership | 允许「共享面兼容扩展」的边界内改动；`ViewDistanceController` 大改需 interaction 侧确认或合并后由主线收编 |
| planet-definition 改 `SolarSystemCamera` 输入/状态机 | WORKFLOW §2 明文禁止 | 精确高度 API 可视为「相机查询/设置接口」扩展，但 `ApplyTransform` 主路径改动越界；主线合并时收编到契约 §2 |
| interaction 给 `SolarSystemCamera` 加 `freeRoam` | 共享面「只读视距查询/设置」 | 越出只读；作为 C6 契约补充写入 WORKFLOW §2 |
| 双侧都动地表模式阈值 | WORKFLOW §3「常量只有一处」 | 合并后只保留一套，并回写 §3 |

### 3.4 资源与 GUID

1. **MeshLod 搬迁方式危险**：工作树是「删 `Resources/.../MeshLod` + 未跟踪出现 `Editor/LegacyPlanetMeshLod`」，
   不是 `git mv`。若两边 GUID 不一致，场景/预制引用会丢。合并前必须：
   - 确认 `LegacyPlanetMeshLod` 内 `.meta` GUID 与原 `MeshLod` 一致；
   - 用 `git add -A` 成对入库（删除 + 新路径），或改为 `git mv` 后提交；
   - 跑场景引用校验（WORKFLOW §7）。
2. Earth / ProceduralTerra cubemap 为大二进制改动，合并时按文件取一边即可，但 **必须与 `GeneratorVersion=5` 的采样结果一致**，否则云场/法线和代码口径漂移。
3. 所有新增 `.cs` / `.hlsl` 必须带已提交 `.meta`；缺 meta 会触发 WORKFLOW §6.1 经验 3 的硬冲突。

### 3.5 与第三条工作树的耦合（本轮不合并，但不能不知道）

`SolarSystem` 工作树（`dsh/SolarSystem` @ `0f936ea`）亦有未提交分块网格工作
（`PlanetChunkMesh` / `ChunkTerrainRenderer` / `SurfaceChunkSelector` 等）。
planet-definition 文档明确点名：

- 两套层级（`TerrainLattice` 32格×25层 vs `SurfaceChunkGrid` 最大 20 层）**必须统一**；
- SolarSystem 侧 `DisplacementMetres` 仍是旧公式；
- `IChunkContentProvider` 尚未接帧循环（DEVLOG 待补项）。

**本轮只合 planet-definition + interaction 时，不要顺手改 SolarSystem 的 `Space/`。**
分块统一放到下一轮 `integration/authority`。

### 3.6 提交与刷新纪律

- 合并**按哈希**：`9f7febd`（interaction tip）。planet-definition 必须先把 WIP **提交成明确提交**
  （建议拆成：运行时地形 / 云场气候 / 显示位移公式 / MeshLod 搬迁 / 相机精确高度 / 文档），再合并。
- 合并前关闭 Unity 中打开的 `SolarSystem.unity`（模态框会卡死 MCP，见 WORKFLOW §6.1）。
- 不要 `git clean -fdx`；planet-definition 的 `LegacyPlanetMeshLod/` 与本地重烘资产可能是唯一副本。
- 合并顺序按协议：**interaction → planet-definition**（SolarSystem 已在前一轮合过提交面；
  本轮没有 solar 新提交，可跳过，但验收仍要跑尺度审计）。

---

## 4. 并入 main 的合并调研

### 4.1 目标状态

```text
main (1271847)
  ├─ merge 9f7febd          ← interaction C6（3 提交）
  └─ merge <pd-commit>      ← planet-definition WIP 提交后的 tip
```

完成后打 `integration/authority-2026-10-05-R2`（或当日实际日期），各工作树快进到该提交。

### 4.2 步骤清单（建议执行序）

| 步 | 动作 | 验收 |
|---|---|---|
| 0 | 备份标签：`integration/pre-2026-10-05-R2-{interaction,planet-definition,main}` | `git tag -l` 可见 |
| 1 | interaction：确认 `git -C .../interaction status` 干净；记录 tip `9f7febd` | 与本文一致 |
| 2 | planet-definition：整理 WIP 为 1–N 个提交（见 §3.4 资源成对入库）；记录 tip 哈希 | `status` 干净；`.meta` 齐全 |
| 3 | main 合并 interaction：`git merge 9f7febd` | 预期冲突少；跑 Interaction EditMode |
| 4 | main 合并 planet-definition：`git merge <pd-tip>` | 必现 §3.2 冲突；按 §3.1 合成 |
| 5 | 回写 WORKFLOW §2（新增 API）与 §3（阈值表） | 阈值只有一处 |
| 6 | 静态检查 + 全量 EditMode + 编译/场景 | 见 §4.4 |
| 7 | 标签 + 各工作树刷新到权威提交 | 分支指针重合 |

### 4.3 冲突裁决表（预填）

| 议题 | 裁决建议 | 依据 |
|---|---|---|
| 地表模式进入/离开比 | **0.05 / 0.08** | interaction 修复「闸门不可达」；原 100/6562 作历史注释保留 |
| 近地阻尼范围 | **仅进入带内** | interaction 注释与可玩性 |
| 视高缩放 vs 自由飞速度 | freeRoam 时轮子=速度，否则=缩放 | C6；`9f7febd` |
| `+/-` 键 | 速度不绑 `+/-`，让给模拟时间 | `9f7febd` |
| 近远裁面比 | **硬顶 1e5:1** + 模式相关 near | IMGUI 崩溃为 P0 |
| 未烘焙半径 fallback | **0** | interaction；防假地表闸门 |
| 显示位移 | **`DisplacementMetres` 新公式** | PLANET-PROCEDURAL §21.4 |
| `visualReliefScale` | **1–100** | 同上 |
| `terrainSafetyMetres` | **0.01**（厘米语义） | planet-definition；与 `minClearanceMetres` 同量级 |
| 区块缓存指纹 | **`TerrainContentFingerprint()`** | PLANET-PROCEDURAL §21.4 |
| Mesh LOD 离线资产 | 入库搬迁对，GUID 不变 | §3.4 |

### 4.4 验收（WORKFLOW §7 超集）

- [ ] `Planet.Interaction.Tests` 全绿（含 C6 合同测试、裁面 1e5:1、厘米缩放）
- [ ] `Planet.Procedural.Tests` 全绿（含 `RuntimeTerrainTests`、`PlanetTerrainClimateTests`）
- [ ] `Planet.Solar.Tests` 全绿
- [ ] `Planet.Solar.asmdef` references 仍为穷举四项
- [ ] `Planet.Solar` 不引用 `Unity.Mathematics`
- [ ] WORKFLOW §3 阈值表与 `ViewDistanceBoundary` 常量一致
- [ ] 场景中 `SolarSpaceContext` 与 `SolarSystemVisualCoordinator` 双组件仍在（上一轮冲突合成结果）
- [ ] `Tools ▸ Planet ▸ Validate Body Scales` + `Audit Space Scale` 通过
- [ ] Earth 重烘资源与 `GeneratorVersion=5` 口径一致
- [ ] 无缺 `.meta`、无意外 GUID 变更

### 4.5 风险与回滚

| 风险 | 等级 | 缓解 |
|---|---|---|
| 相机双所有权合成不当导致近地抖动/失控 | 高 | 合成后专项手测：进入地表→自由飞→拉高退出→焦点切换 |
| MeshLod GUID 断裂 | 高 | 合并前 GUID 比对；场景引用校验 |
| 云场/ cubemap 与代码版本漂移 | 中 | 以 `GeneratorVersion` 为准，必要时重烘 |
| IMGUI 裁面回归 | 中 | `SurfaceCameraClipTests` + 实机拉近到 1 cm |
| 与 SolarSystem 未提交分块工作冲突放大 | 中 | 本轮不碰 `Space/`；下一轮统一层级 |
| WIP 体量大、一次合并难审 | 中 | planet-definition 提交前按主题拆分 |

回滚点：`integration/pre-2026-10-05-R2-*` 三标签 + 合并前 `main` 快照。
冲突合成失败时 `git merge --abort`，不要半成品提交。

### 4.6 本轮不做

- 不合并 `dsh/SolarSystem` 未提交分块网格；
- 不实现 `IChunkContentProvider` 帧循环接入（DEVLOG 待补项，属 SolarSystem/planet-definition 下一轮）；
- 不删工作树；不 `git clean`；
- 不把本 REPORT 和过程性 `MERGE-*` 留进权威提交（合并完成后删除本文件并清 DOCS 登记）。

---

## 5. 附：探测命令备忘

```bash
git worktree list
git log --oneline main..mimo/interaction
git log --oneline main..codex/planet-definition   # 空：已重合
git diff --stat main...mimo/interaction
git -C D:/UnityProjects/Planet-worktrees/planet-definition status --short
git -C D:/UnityProjects/Planet-worktrees/planet-definition diff --stat -- '*.cs' '*.md' '*.shader' '*.hlsl'
```

*本文件只描述 2026-10-05 探测到的工作区与提交状态；工作树是活的，执行合并前请重跑上表命令核对哈希。*
