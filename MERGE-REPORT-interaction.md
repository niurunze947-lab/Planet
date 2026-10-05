# interaction 工作树 · 合并前提交报告

- 分支：`mimo/interaction`
- 范围：玩家输入、视距边界、近地模式判定（`DEVELOPMENT-WORKFLOW.md` §2 interaction ownership）
- 验证：EditMode **46/46 通过**（含 ViewDistance / SurfaceMode 回归）；Play 实测进入/退出 `[SurfaceMode]` 日志正常

---

## 1. 本轮交付

### 1.1 视距缩放边界（滚轮 / 键位）

| 项 | 说明 |
|---|---|
| 比例 | `surfaceClearance / viewHeight`（地表距 / 中心视距） |
| 边际效应 | 推近速率 smoothstep：`nearRate 0.08 → farRate 1.0`；拉远不阻尼 |
| 硬限制 | `viewHeight ≥ surfaceRadius`（允许等于，禁止进入实体） |
| 每帧顺序 | 读半径 → 处理输入 → 限制目标距离 → 平滑当前距离 → 发布事件 |

### 1.2 近地模式判定（仅判定，无漫游）

| 项 | 说明 |
|---|---|
| 两套基准 | `h_c` = 到球心视距；`h_s` = 相对地表正距离 |
| 百分比 | `ratio = h_s / h_c` |
| 进入 | `ratio ≤ 1.524%`（地球标定：100 km / 6562 km） |
| 退出 | `ratio ≥ 3.0%`（滞回，防抖） |
| 约束 | **仅行星外靠近地表时触发**；行星内部 / 球心附近不打开 |
| 事件 | `EnteredSurfaceMode` / `ExitedSurfaceMode` + 统一 `[SurfaceMode]` debug 日志 |

> 类 Unity QWASD 漫游飞行模式**已取消**，本轮不包含操控切换、重力坐标系与漫游速度曲线。

---

## 2. 文件清单

### 新增 `Assets/Scripts/Interaction/`（`Planet.Interaction` 程序集）

| 文件 | 职责 |
|---|---|
| `ViewDistanceBoundary.cs` | 视距/比例/速率/钳制/近地闸门纯函数 |
| `ViewDistanceController.cs` | 滚轮与键位缩放输入、近地速率阻尼、表面钳制 |
| `SurfaceModeController.cs` | 近地模式状态机（判定 + 滞回 + debug），只读发布事件 |
| `Planet.Interaction.asmdef` | 引用 `Planet.Solar` + `Unity.InputSystem` |

### 新增 `Assets/Tests/EditMode/Interaction/`

| 文件 | 覆盖 |
|---|---|
| `ViewDistanceBoundaryTests.cs` | 比例、阻尼、钳制、单次滚轮防穿 |
| `SurfaceModeTests.cs` | 地球 1.524% 标定、滞回、内部不触发、高度上限 |
| `Planet.Interaction.Tests.asmdef` | 测试程序集 |

### 修改共享面 `SolarSystemCamera`（允许的查询/设置接口）

| 成员 | 用途 |
|---|---|
| `handleZoomInput` | 外部缩放控制器接管开关 |
| `SurfaceClearanceOverViewHeight` | 地表距 / 中心视距查询 |
| `ClampToSurfaceFloor` / `ClampCurrentDistanceToSurface` | 目标与当前距离表面钳制 |
| `SetDistance` 写路径 | 一并钳制，并置 `_hasTarget` |

**未修改**：程序化采样、材质烘焙、`SolarSystem` 目录数据、轨道方程。

---

## 3. 对外契约（供主线合并）

```text
Planet.Interaction.ViewDistanceBoundary
  SurfaceClearance(view, radius)
  ClearanceOverViewHeight(view, radius)     // = SurfaceModeRatio
  ZoomRateMultiplier(view, radius, near, far)
  ClampToSurface(view, radius, min, max)
  ApplyLogZoom(...)
  EvaluateSurfaceMode(view, radius, inMode, enterRatio, exitRatio)
  IsOutsideSurface(view, radius)

Planet.Interaction.ViewDistanceController   // 输入 + 边界
Planet.Interaction.SurfaceModeController    // 近地判定 + 事件 + debug
```

默认阈值：`enterRatio = 100/6562`，`exitRatio = 0.030`，`nearRate = 0.08`，`farRate = 1`。

---

## 4. 验证记录

- EditMode：`ViewDistanceBoundaryTests` + `SurfaceModeTests` + 既有程序化回归，**46 passed / 0 failed**
- Play：从行星外滚轮靠近 → `[SurfaceMode] ENTER ... positive surface distance`；内部快照不触发
- `git diff --check`：无空白错误（提交前复核）

---

## 5. 合并注意

1. 依赖方向：`Planet.Interaction → Planet.Solar → Planet.Procedural`，无反向引用。
2. `SolarSystemCamera` 仅增加查询/设置接口与表面钳制；SolarSystem 侧无算法改动。
3. 近地模式**只判定不接管相机**；未来 mode 切换 / 资源预加载可订阅 `EnteredSurfaceMode` / `ExitedSurfaceMode`。
4. 测试 asmdef 位于 `Assets/Tests/EditMode/Interaction/`（同目录多 asmdef 会让 Unity 导入失败，勿合并到 EditMode 根目录）。
