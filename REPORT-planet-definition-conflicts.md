# `codex/planet-definition` 分支交接单

> **给 `planet-definition` 工作树的行动清单。** 本文件在 `main` 的当前提交上生成，
> 描述"你这支要并入 `main` 之前必须改什么"。
> 按本文件调整完成后，请通知主工作树重跑合并；**合并完成后本文件删除**
> （规则见 [DEVELOPMENT-WORKFLOW.md](DEVELOPMENT-WORKFLOW.md) §8）。
>
> 生成时间：2026-10-05 ｜ 对照基线：`main` = `0f936ea` ｜ 你的分支 tip：`08962a7`
> 冲突测量方式：`git merge-tree --write-tree --messages`（三方合并预言机，未改动任何工作树）

---

## 0. 结论先行

**只有 2 个文件、3 个冲突块**，其中：

| # | 文件 | 冲突块 | 严重度 | 谁来做 |
|---|---|---|---|---|
| **C1** | `Assets/Scripts/Solar/CelestialBody.cs` | 2 块 | ⚠️ **会编译失败** | **你**（见 §2） |
| **C2** | `Assets/Scenes/SolarSystem.unity` | 1 块 | 🔴 **会静默丢组件** | 建议主工作树做（见 §3），你也可以先改 |
| **D1** | `DEVLOG.md` / `PLANET-SOLARSYSTEM.md` / `PROJECT-OVERVIEW.md` | 自动合并，无冲突 | 🟡 内容会过期 | **你**（见 §4） |

`main` 已经并入 `dsh/SolarSystem`（尺度层）与 `mimo/interaction`（交互层）。
你的冲突**全部来自 solar 分支**——interaction 与 `main` 的新增内容都不与你冲突。

---

## 1. 背景：`main` 在你分叉之后发生了什么

你从 `a1697b4`（`integration/authority-2026-10-04`）分叉。之后 `main` 多了这些**与你重叠**的变化：

| 变化 | 提交 | 对你影响 |
|---|---|---|
| **C1/C2/C-ratio 契约落地**（`Procedural/Terrain/`） | `78cfb15` | 与你**无**冲突（不同文件） |
| 尺度层 `Solar/Space/` 并入 | `4176e1a` | `CelestialBody.cs` **签名变更** → **C1 冲突** |
| 场景里新增 `SolarSpaceContext` 组件 | 同上 | 与你的 `SolarSystemVisualCoordinator` **撞 fileID** → **C2 冲突** |
| 交互层并入 | `0f936ea` | 与你**无**冲突 |

一句话：**`CelestialBody.ApplyState` 的签名被 solar 改过了，你的代码还在用旧签名。**

---

## 2. C1 · `Assets/Scripts/Solar/CelestialBody.cs`（必须修，否则不编译）

### 2.1 块 A — 字段声明区（行 53–96）

两边都是**纯新增**，不冲突语义，直接两块都留：

```csharp
private SolarSystemSimulation _simulation;      // ← solar 新增，保留
private float _resolvedSceneRadius;
private bool _scaleResolved;
private float _appliedRadius = -1f;
private bool _warnedScaleDrift;
private float _inspectedSceneRadius = float.NaN;
private bool _renderersVisible = true;
// ... SetRenderersVisible / RenderersVisible 成员（solar 新增，保留）...

private StellarSurfaceView _stellarSurface;     // ← 你的，保留
```

**行动**：删掉 `<<<<<<<` / `=======` / `>>>>>>>` 三行标记，两侧内容**全部保留**。无算法改动。

### 2.2 块 B — `ApplyState` 尾部（行 341–352）—— ⚠️ **这里不能照抄你自己那侧**

`main` 侧只有一行：

```csharp
ApplyRadius(RadiusUnits);
```

你那一侧是：

```csharp
if (_stellarSurface != null && _stellarSurface.definition != null)
{
    _stellarSurface.SetRadius(radiusUnits);   // ❌ radiusUnits 已不存在
    _stellarSurface.SetRigidSpin(angle);
}
else if (_proceduralSurface != null && _proceduralSurface.definition != null)
    _proceduralSurface.SetRadius(radiusUnits);   // ❌ 同一个问题
else if (_surface != null) _surface.localScale = Vector3.one * radiusUnits;   // ❌
```

**`radiusUnits` 这个参数已经被 solar 删掉了。** `ApplyState` 的签名从
`ApplyState(Vector3 worldPosition, float radiusUnits, double spinDegrees)`
简化成了 `ApplyState(Vector3 worldPosition, double spinDegrees)`——
半径现在是**星球自己持有的只读派生量** `RadiusUnits`（= `sceneRadiusUnits × bodySizeExaggeration`），
不再由 `SolarSystemSimulation` 每帧推送。原因与设计见
[PLANET-SOLARSYSTEM.md](PLANET-SOLARSYSTEM.md) §9。

所以**照抄你那侧会直接编译不过**（`CS0103: 当前上下文中不存在名称"radiusUnits"`）。

**推荐的合成写法**（把 stellar 分支折进 `ApplyRadius`，`ApplyState` 保持一行）：

`ApplyState` 尾部保持 `main` 的写法：

```csharp
            ApplyRadius(RadiusUnits);

            // 自转是每帧量，且不受半径缓存的影响，所以放在 ApplyRadius 之外。
            if (_stellarSurface != null && _stellarSurface.definition != null)
                _stellarSurface.SetRigidSpin(angle);
```

然后给 `ApplyRadius`（同文件，行 368）**加一个 stellar 分支**：

```csharp
        private void ApplyRadius(float radiusUnits)
        {
            if (Mathf.Approximately(radiusUnits, _appliedRadius)) return;
            _appliedRadius = radiusUnits;

            if (_stellarSurface != null && _stellarSurface.definition != null)
                _stellarSurface.SetRadius(radiusUnits);            // ← 新增这一支
            else if (_proceduralSurface != null && _proceduralSurface.definition != null)
                _proceduralSurface.SetRadius(radiusUnits);
            else if (_surface != null) _surface.localScale = Vector3.one * radiusUnits;

            // ... 环缩放部分不变 ...
        }
```

**为什么必须这么拆**（不是风格问题）：

- `ApplyRadius` 开头有 `if (Mathf.Approximately(radiusUnits, _appliedRadius)) return;` 的
  **早退缓存**——半径不变就整段跳过。而 `SetRigidSpin(angle)` 是**每帧都变**的量，
  放进 `ApplyRadius` 会被这个早退吃掉，恒星就不转了。所以自转必须留在 `ApplyRadius` 之外。
- `SetRadius` 本身是幂等的（`radius = Mathf.Max(radiusUnits, 1e-9f)` 后设 `localScale`），
  放进缓存内没有副作用。
- 参考实现（你分支的 `Assets/Scripts/Procedural/StellarSurfaceView.cs`）：

  ```csharp
  public void SetRadius(float radiusUnits) { radius = Mathf.Max(radiusUnits, 1e-9f); if (surface != null) { surface.localPosition = Vector3.zero; surface.localScale = Vector3.one * radius; } }
  public void SetRigidSpin(float degrees)   { rigidSpin = degrees; if (surface != null) surface.localRotation = Quaternion.AngleAxis(-rigidSpin, Vector3.up); }
  ```

- `_proceduralSurface` 是 `PlanetSurfaceView`、`_stellarSurface` 是 `StellarSurfaceView`，
  两者**类型不同**，`else if` 链是安全的：太阳只挂 stellar、行星只挂 procedural。

### 2.3 已经自动合好、**不要动**的部分

`BuildVisuals()` 里 solar 的复位（`_simulation = null; _scaleResolved = false; _appliedRadius = -1f;
float radiusUnits = RadiusUnits;`）与你的 stellar 接线（`_stellarSurface = GetComponent<StellarSurfaceView>();
if (_stellarSurface != null && _stellarSurface.definition != null) { ... _stellarSurface.BuildSurface(RadiusUnits);
_surface = _stellarSurface.SurfaceTransform; }`）**已经正确共存，没有静默丢失**。
合并后请复核这一段，但**不需要**手工干预。

### 2.4 自查

改完在你自己的 Unity 实例里**编译一次**（`Assets/Scripts/Solar/` 与 `Assets/Scripts/Procedural/`
都会参与编译）。编译错误是本条冲突唯一的验收标准——它是唯一会挡住合并的项。

---

## 3. C2 · `Assets/Scenes/SolarSystem.unity`（🔴 会静默丢一个组件）

### 3.1 现象与根因

冲突块在行 14850–14893，落在 GameObject **`Solar System`**（`&1365860515`）上。

**两边各自新增了一个 MonoBehaviour，并且分配到了同一个 `fileID: 1365860518`：**

| 侧 | 脚本 | 脚本 GUID |
|---|---|---|
| `main`（solar） | `SolarSpaceContext` | `bf9f478efeec57245b32590337b6c265` |
| 你 | `SolarSystemVisualCoordinator` | `6c981512a15ff8940ac1b67ff9c05caa` |

**这个冲突比 `git status` 报的"1 file"严重得多。** 原因是 `m_Component` 列表里那一行
在两边**逐字节相同**：

```yaml
    - component: {fileID: 1365860518}
```

git 认为这一行"两边一样"，**把两个条目静默合并成了一条**，只把脚本块本身标成了冲突。
实测合并结果：该 GameObject 的 `m_Component` 只剩 3 条，`1365860518` **只有一个**，
却有**两份** `SolarSpaceContext` / `SolarSystemVisualCoordinator` 脚本块在争这个 id。

**后果：无论你选哪一侧，都会丢掉另一侧那个组件。**

- 选 `--ours`（你的）→ 丢 `SolarSpaceContext` → **尺度层整层失效**：分块网格、远点重定基、
  远场分档全没了，`Tools ▸ Planet ▸ Audit Space Scale` 会警告"没有 SolarSpaceContext"。
- 选 `--theirs`（main 的）→ 丢 `SolarSystemVisualCoordinator` → 你的 22 个
  `planetMeshes` 引用、`star` / `cameraRig` / `earthTextures` 接线全丢。

### 3.2 好消息：**两个组件都是代码生成的，不需要手工修 fileID**

这是我实测后最重要的结论，请照它做，**不要**手工去改 YAML 里的 `fileID`：

- `SolarSpaceContext` 由 `Tools ▸ Planet ▸ Rebuild Solar System Scene`
  （`Assets/Editor/SolarSystemBuilder.cs`）接线；
- `SolarSystemVisualCoordinator` 由 `EnsureInScene()` 创建，而它是**幂等**的：

  ```csharp
  public static SolarSystemVisualCoordinator EnsureInScene()
  {
      SolarSystemVisualCoordinator existing = FindObjectOfType<SolarSystemVisualCoordinator>();
      if (existing != null) return existing;                       // ← 已存在就复用
      SolarSystemSimulation simulation = FindObjectOfType<SolarSystemSimulation>();
      if (simulation == null) throw new System.InvalidOperationException("SolarSystem simulation is missing.");
      return simulation.gameObject.AddComponent<SolarSystemVisualCoordinator>();
  }
  ```

  而它在 `Assets/Editor/{EarthTextureLodBuilder,PlanetMeshLodBuilder,SolarSystemMaterialBuilder,
  StellarFrameworkBuilder}.cs` 里共 **6 处**被调用——你现有的任何构建菜单跑一次就会把它加回来。

- 更关键的是：**它的 5 个序列化字段本来就会被 `Refresh()` 全部自愈**（`LateUpdate()` 每帧调
  `Refresh()`）：

  | 字段 | 自愈方式 |
  |---|---|
  | `simulation` | `FindObjectOfType<SolarSystemSimulation>()` |
  | `cameraRig` | `simulation.ViewCamera.GetComponent<SolarSystemCamera>()` |
  | `earthTextures` | `FindObjectOfType<PlanetTextureLodController>()` |
  | `star` | `FindObjectOfType<StellarSurfaceView>()` |
  | `planetMeshes` | `FindObjectsOfType<PlanetMeshLodController>()`（**每 1 秒**重扫） |

  所以场景里那份序列化赋值只是"起跑线"，丢了会被重新算出来。

### 3.3 修复：**优先手工补组件，不要重跑 Rebuild Scene**

**先看清楚 `Rebuild Solar System Scene` 到底做了什么**（`Assets/Editor/SolarSystemBuilder.cs`）：

```csharp
EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);   // L52
...
EditorSceneManager.SaveScene(scene, ScenePath);                                // L58 → 覆盖 SolarSystem.unity
```

它是**从空场景重建再整文件覆盖**，不是增量修补。所以它会把场景里所有手工调整一起清掉——
包括你自己灌进场景的东西。**因此默认方案不是重跑构建，而是手工把丢掉的组件补回来。**

#### 方案 A（推荐）：手工补 `SolarSystemVisualCoordinator`

前提：合并时该冲突块取 `main` 侧（保留 `SolarSpaceContext`，因为它还有一堆序列化字段
`maxChunkLevel` / `chunkResolution` / `maxResidentChunks` / `lodPixelTolerance` / `reliefRatio` /
`surfaceRebaseThresholdMetres` 等，丢了要重配）。

1. 退出 Play 模式（Play 下 `MarkSceneDirty` / `SaveScene` 会抛
   "This cannot be used during play mode"）；
2. Hierarchy 选中 **`Solar System`** → Inspector → **Add Component** →
   搜 `SolarSystemVisualCoordinator` → 添加；
3. **不需要手工填任何字段**——`Refresh()`（`LateUpdate()` 每帧调用）会把 5 个引用全部自愈：

   | 字段 | 自愈方式 |
   |---|---|
   | `simulation` | `FindObjectOfType<SolarSystemSimulation>()` |
   | `cameraRig` | `simulation.ViewCamera.GetComponent<SolarSystemCamera>()` |
   | `earthTextures` | `FindObjectOfType<PlanetTextureLodController>()` |
   | `star` | `FindObjectOfType<StellarSurfaceView>()` |
   | `planetMeshes` | `FindObjectsOfType<PlanetMeshLodController>()`（**每 1 秒**重扫） |

   而且这 5 个字段**全是引用**，没有任何数值调参——所以"丢了"没有信息损失。
4. 进 Play 一次，确认 Inspector 里 5 个字段都被填上、LOD 分层随视距变化。

> 你现有的编辑器工具也会自动把它加回来：`EnsureInScene()` 在
> `Assets/Editor/{EarthTextureLodBuilder,PlanetMeshLodBuilder,SolarSystemMaterialBuilder,
> StellarFrameworkBuilder}.cs` 共 **6 处**被调用，而它是幂等的
> （`FindObjectOfType` 有就复用，没有才 `AddComponent`）。所以只要合并后跑过其中任何一个菜单，
> coordinator 就回来了。

#### 方案 B：确实需要重建场景时

只有在场景确实已经损坏、方案 A 做不到时才用，并且**按这个顺序**：

1. 退出 Play；
2. **Tools ▸ Planet ▸ Rebuild Solar System Scene**（接回 `SolarSpaceContext`；
   主分支的这一步内部会调 `SolarSystemMaterialBuilder.ConfigureSolarSystem(root)`）；
3. **Tools ▸ Planet ▸ Build Solar System Materials**（经 `EnsureInScene()` 加回 coordinator；
   并按编辑器 tick 分批构建，不要在一个循环里同步跑完）；
4. **Tools ▸ Planet ▸ Build Stellar Framework**（恒星侧）；
5. 复核 §3.4。

**注意**：`CapturePoses` 之类的截图工具**不要**在末尾调用 `AssetDatabase.Refresh()`——
会把 Unity 踢出 Play 模式（见 [PROJECT-OVERVIEW.md](PROJECT-OVERVIEW.md) §12 第 9 条，
`CapturePoses` 必须一个编辑器 tick 只处理一个机位）。

### 3.4 复核清单：哪些会被重建、哪些不会

我逐个查过两侧的场景脚本，结论如下：

| 数据 | 在场景里？ | 重建后会不会丢 | 说明 |
|---|---|---|---|
| `SolarSystemVisualCoordinator` 的 5 个字段 | 是 | **不会实质丢** | 全是引用，`Refresh()` 自愈（§3.3） |
| `SolarSpaceContext` 的序列化字段 | 是 | 取 `main` 侧即保留 | 方案 B 重建会回默认值，需重新调 |
| `PlanetMeshLodController` / `PlanetTextureLodController` | 是 | **无调参可丢** | 只有 `ActiveTier` / `RequestedTier` 两个**只读运行时**属性，无 `[SerializeField]` 数值 |
| `StellarSurfaceView` | 是 | **无调参可丢** | 只有 `IsDetailed` 只读属性 |
| **`SunStellar.asset`（`StellarDefinition`）** | 否，是资产 | **不会丢** | `StellarFrameworkBuilder` 先 `LoadAssetAtPath`，**存在就复用**，不会 `CreateAsset` 覆盖；只同步 `physical` 里的半径与极点 |
| 光照 / 后处理 / 星空曝光 | 是 | 重建会丢 | 方案 A 不触碰场景，不受影响；方案 B 需重新调 |
| `CelestialBody.sceneRadiusUnits` / `bakedKmPerUnit` | 是 | 重建会重烘 | `Rebuild Solar System Scene` 内部会烘焙；跑完用 **Validate Body Scales** 核对 |

**一句话**：C2 这个冲突**没有不可恢复的数据**——丢的是能自动重建的引用。
这也是为什么它虽然会静默丢组件，却不需要你重做外观调参。

### 3.5 如果改了场景，请顺带自查一遍 fileID

同一个"两边各加一个组件、撞同一个 id"的风险**只在这一处**发生（我扫过合并结果的
**310 个 YAML 锚点，无重复 id**），但你后续再往这个场景加组件时还可能重演。自查方法：

```powershell
# 合并后的场景不应有重复的 YAML 锚点
Select-String -Path Assets/Scenes/SolarSystem.unity -Pattern '^--- !u!\d+ &(\d+)' |
  ForEach-Object { $_.Matches[0].Groups[1].Value } |
  Group-Object | Where-Object Count -gt 1
# 无输出 = 正常
```

---

## 4. D1 · 文档（不冲突，但你得跟着改）

`DEVLOG.md`、`PLANET-SOLARSYSTEM.md`、`PROJECT-OVERVIEW.md` 三者**自动合并无冲突**，但它们现在
**内容已经过期**——`main` 在 2026-10-05 重整了文档体系（新增 [DOCS.md](DOCS.md) 作为索引与引用图，
删除了两个已处理完的合并报告，并把契约数字收敛到一处）。合并后请在你的分支上补齐：

| 事项 | 说明 |
|---|---|
| **新增 `PLANET-STELLAR.md` 要登记** | 你带了新文档 `PLANET-STELLAR.md`。`main` 新增了 [DOCS.md](DOCS.md) 作为文档索引与引用图，**新文档必须在 `DOCS.md` §2（职责表）、§3（唯一真相表）登记，并在 §4 补边**。没有入边的文档视为孤儿文档。 |
| **不要复制契约数字** | `main` 把**视场高度契约、程序集清单、跨程序集接口**收敛到 [DEVELOPMENT-WORKFLOW.md](DEVELOPMENT-WORKFLOW.md) §2–§3 一处。你的 `PLANET-STELLAR.md` 若引用了 `viewHeight` / 比例阈值 / 程序集引用列表，请改成**链接**，不要抄数字——发现第二份拷贝就会删掉拷贝。 |
| **踩坑表归位** | 通用踩坑在 [DEVLOG.md](DEVLOG.md)；程序化的在 [PLANET-PROCEDURAL.md](PLANET-PROCEDURAL.md)；尺度层的在 [PLANET-SPACE.md](PLANET-SPACE.md) §11。恒星相关的坑请自建一节放 `PLANET-STELLAR.md`，不要塞进上面三处。 |
| **`DEVELOPMENT-WORKFLOW.md` §2 的程序集表** | 你新增了 `Stellar*.cs`（在 `Assets/Scripts/Procedural/`，属 `Planet.Procedural`）与 `Assets/Editor/StellarFrameworkBuilder.cs`。若**没有**新增 asmdef，则程序集表无需改；若新增了，请在 §2 登记并说明引用方向。 |
| **ownership 表** | 恒星框架落在 `Assets/Scripts/Procedural/` + `Assets/Editor/` + `Assets/Resources/Solar/BodyMaterials/Stellar/`，**都在 `planet-definition` 的既有 ownership 内**，不需要重新划界。但 `PLANET-STELLAR.md` 里请写清这一层与 `PlanetAtmosphere` / `PlanetCloudLayer` 的分工。 |

---

## 5. 行动清单（照做即可）

- [ ] **C1-a**：`CelestialBody.cs` 块 A（字段，行 53–96）——两侧全留，删冲突标记
- [ ] **C1-b**：`CelestialBody.cs` 块 B（`ApplyState` 尾部，行 341–352）——按 §2.2 **合成**，
      **不要**照抄自己的 `radiusUnits` 版本（会 `CS0103`）
- [ ] **C1-c**：给 `ApplyRadius` 加 `_stellarSurface` 分支；`SetRigidSpin(angle)` 留在
      `ApplyRadius` **之外**（否则被 `Approximately` 早退吃掉，恒星不转）
- [ ] **C1-d**：在你自己的 Unity 实例里**编译通过**（唯一硬性验收）
- [ ] **C2-a**：场景冲突块取 `main` 侧（保留带序列化字段的 `SolarSpaceContext`）
- [ ] **C2-b**：退出 Play → 在 `Solar System` 上**手工 Add Component → `SolarSystemVisualCoordinator`**
      （方案 A，首选）。**不要**为了这个冲突去重跑 Rebuild Scene——那是从空场景整文件覆盖（§3.3）
- [ ] **C2-c**：进 Play 确认 coordinator 的 5 个引用被 `Refresh()` 自动填上、LOD 随视距分层
- [ ] **C2-d**：跑 **Audit Space Scale**，七节齐全、无缺 `SolarSpaceContext` 警告
- [ ] **C2-e**：只有场景确实损坏时才走方案 B，并严格按 §3.3 的菜单顺序（Rebuild Scene →
      Build Solar System Materials → Build Stellar Framework），跑完照 §3.4 复核
- [ ] **D1**：把 `PLANET-STELLAR.md` 登记进 [DOCS.md](DOCS.md)，契约与坑位按 §4 归位
- [ ] **提交**：源码与 `.meta`、场景与定义资产**同一提交**（[DEVELOPMENT-WORKFLOW.md](DEVELOPMENT-WORKFLOW.md) §5）
- [ ] **通知主工作树**重跑合并（主工作树会用 `git merge-tree` 复核一次，确认 0 冲突）

---

## 6. 主工作树侧的验收方式

你改完后，主工作树按下面复核，全部通过才并入：

```powershell
# 1) 三方合并预言机：应报 0 conflict
git merge-tree --write-tree --messages main <你的新 tip>

# 2) 场景 fileID 唯一性（见 §3.5），"无输出"才算通过：
Select-String -Path Assets/Scenes/SolarSystem.unity -Pattern '^--- !u!\d+ &(\d+)' |
  ForEach-Object { $_.Matches[0].Groups[1].Value } |
  Group-Object | Where-Object Count -gt 1

# 3) 备份标签（合并前）
git tag integration/pre-2026-10-05-planet-definition <你的新 tip>
```

合并**之后**再依次跑：

1. 编译零错误零警告；
2. EditMode 全量：`Planet.Interaction.Tests` + `Planet.Solar.Tests`（59）+ 程序化回归 +
   你的 `StellarFrameworkTests`；
3. `Tools ▸ Planet ▸ Build Solar System Materials` 与 `Build Stellar Framework`；
4. 全量 **Audit Space Scale**（七节齐全）；
5. **Validate Body Scales**（21 个天体，确认缩放没在场景重建中丢）；
6. 场景里 `SolarSpaceContext` 与 `SolarSystemVisualCoordinator` **同时**存在。

---

*本文件由主工作树在 `main` = `0f936ea` 上生成；所有冲突结论均来自
`git merge-tree --write-tree --messages` 与一次隔离工作树试合并的实测输出，非推测。*
