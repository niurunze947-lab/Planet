# Planet — 项目概览

> 本文件是文档体系的入口，只回答「这是什么、东西在哪、从哪上手」。
> 每件事的 owner 见 [doc.md](doc.md) §3–§4；本文不复制任何专题的细节或数字。

---

## 1. 这是什么

一个 **Unity 2022.3 LTS + URP** 的行星演示工程。正式内容是真实比例太阳系，
建立在三层共享实现之上：

**正式线 —— 真实比例的太阳系**（`SolarSystem.unity`，Build Settings index 0）

- 从太阳到柯伊伯带，比例不做任何压缩，21 个天体 + 两条碎带
- 轨道根数取自 JPL/Standish 与 Meeus；**自转与公转全部由代码按开普勒方程推算**
- 全部天体接入定义驱动的程序化表面；五组环使用径向光学材质
- 整个场景由代码生成，不是手工摆放的

**三条共享实现**（正式线的全部内容）

| 层 | 做什么 | 一句话判据 | 文档 |
|---|---|---|---|
| 程序化框架 | `PlanetDefinition → PlanetSurfaceSampler → 网格 / Cubemap → PlanetSurfaceView` | **定义是唯一权威**，网格与贴图都是可重建的派生缓存；采样器按单位球方向取值，与分辨率、半径、场景尺度无关 | [PLANET-PROCEDURAL.md](docs/PLANET-PROCEDURAL.md) |
| 尺度坐标层 | 宇宙侧 `double` 米四级架树 + 地表侧 `float` 米分块本地坐标 | 唯一规则是「**在 double 里减，最后一步才降到 float**」 | [PLANET-SPACE.md](docs/PLANET-SPACE.md) |
| 交互层 | 统一自由飞行、临时聚焦、近地体固定运动和地形碰撞 | 玩家位姿与速度只有一个所有者，近地不切换运动控制器 | [DEVELOPMENT-WORKFLOW.md](docs/DEVELOPMENT-WORKFLOW.md) §3 |

三个消费者建立在程序化框架之上，各自只读公开的观察契约，不修改输入与轨道：
Earth 的近景纹理分级（[PLANET-TEXTURE-LOD.md](docs/PLANET-TEXTURE-LOD.md)）、
21 个天体的可见光外观（[PLANET-MATERIALS.md](docs/PLANET-MATERIALS.md)）、
太阳的独立光球框架（[PLANET-STELLAR.md](docs/PLANET-STELLAR.md)）。

面向 SpaceEngine 体验目标的商业级渲染路线、已落地的采样优化和独立 Player 性能验收见
[PLANET-RENDERING.md](docs/PLANET-RENDERING.md) §1–§5；该目标尚未全部实现。

---

## 2. 基本信息

| 项 | 值 |
|---|---|
| 项目名 / 路径 | `Planet` / `D:\UnityProjects\Planet` |
| 公司名 / 版本 | `DefaultCompany` / `0.1.0` |
| Unity | **2022.3.62f3**（LTS） |
| 渲染管线 | **Universal (URP) 14.0.12** |
| 色彩空间 | **Linear** |
| 输入系统 | 新输入系统（`activeInputHandler = 1`，**仅**新系统） |
| 序列化模式 / Meta | **Force Text** / Visible Meta Files（资产 YAML 文本，可 diff） |
| 默认分辨率 | 1024 × 768（窗口模式） |
| 场景数 | **1**（`SolarSystem`，Build Settings index 0） |
| Run In Background | **开启**——编辑器默认关闭，这是本工程最容易踩的坑（[PLANET-PITFALLS.md](docs/PLANET-PITFALLS.md) §7） |
| 版本控制 | **git + Git LFS**，整合分支 `main` |
| **不要用 Unity 6 打开** | `Library/` 曾被 Unity 6 打开并降级，`manifest.json` 也一度混入 Unity 6 的包，已修复；再开一次会重新污染 |

---

## 3. 目录结构

```
D:\UnityProjects\Planet\
├── Assets/                     ← 入库
│   ├── Scenes/                 `SolarSystem.unity`（唯一场景）
│   ├── Scripts/                运行时：Solar/（含 Space/）、Procedural/（含 Terrain/）、Interaction/、相机与天空控制
│   ├── Editor/                 Tools ▸ Planet 全部菜单；刻意留在 Assembly-CSharp-Editor
│   ├── Shaders/                行星表面、恒星、大气、云、星空、环、白模与轨道
│   ├── Settings/               URP 配置、场景材质、后处理 Profile
│   ├── Resources/Solar/        PlanetDefinition 定义与派生缓存（含回退白模材质）
│   ├── Tests/EditMode/         每个测试程序集独占一个子目录
│   ├── Textures/Sky/           星空 HDR 贴图（唯一保留的位图资产）
├── ProjectSettings/            ← 入库
├── Packages/                   ← 入库
├── MCP-Setup/                  握手报告与工具清单 ← 入库
├── docs/                       专题文档（PLANET-*、DEVLOG、WORKFLOW）
└── doc.md + README.md          文档地图与工程入口
```

`Library/`、`Logs/`、`UserSettings/` 已被 `.gitignore` 排除。

**代码分布**（文件级地图在各专题文档，这里只给职责边界）：

| 位置 | 职责 | 程序集 |
|---|---|---|
| `Assets/Scripts/`（根） | 高程球、环绕相机、自转、表面测光、以及把相机契约接到渲染消费者的协调器 | `Assembly-CSharp` |
| `Assets/Scripts/Solar/` | 目录、开普勒解算、模拟、浮动原点、场景工厂、相机、HUD、环、碎带 | `Planet.Solar` |
| `Assets/Scripts/Solar/Space/` | 坐标阶梯、架树、分块网格、LOD 选择、流式策略 | `Planet.Solar` |
| `Assets/Scripts/Procedural/` | 统一定义、坐标、采样、外观参数、Cube Sphere、壳层、半径接口 | `Planet.Procedural` |
| `Assets/Scripts/Procedural/Terrain/` | 跨程序集契约（C1 / C2 / C-ratio）与**运行时分块地形** | `Planet.Procedural` |
| `Assets/Scripts/Interaction/` | 视距边界纯函数、缩放与漫游输入、近地状态机、body-fixed 锚 | `Planet.Interaction` |
| `Assets/Editor/` | 构建、烘焙、审计、截图 | `Assembly-CSharp-Editor` |

**程序集清单、依赖方向与完整的跨程序集接口列表见
[DEVELOPMENT-WORKFLOW.md](docs/DEVELOPMENT-WORKFLOW.md) §2**（那里是唯一权威版本）。
依赖是**单向**的，反向没有任何引用；这样划分的直接目的是
**让不同工作树合并时不会因为新增的引用或被引用而互相绊住**。
编辑器工具**故意不另立 asmdef**——多一个 asmdef 就多一个合并冲突点。

---

## 4. 依赖与渲染配置

`Packages/manifest.json` 是包清单的真相，本文不复制版本号。工程包集已归一至 Unity 2022.3
兼容范围，其中两项值得记住：

- `com.unity.inputsystem`——**仅**使用新输入系统；
- `com.coplaydev.unity-mcp`——以 `file:` 嵌入式引入的 MCP 桥接（见 §7）。
- `com.unity.ai.navigation` **已移除**：原 Unity 6 模板引入的 2.0.12 在 2022.3 下编译失败
  （调用了不存在的 `NavMesh.SetLinkActive`），且工程并未引用。日后需要寻路请装 2022.3 兼容的 1.x。

```
Pipeline        : PC_RPAsset (Universal)
Color Space     : Linear
Quality Level   : PC
Shadow Distance : 50
Renderer Feature: ScreenSpaceAmbientOcclusion
备用管线        : Mobile_RPAsset（Quality 档位 "Mobile"，当前档位为 PC）
渲染层          : Default + Light Layer 1~7
```

### 环境设置（在场景里，不是全局）

环境设置**逐场景**保存在 `.unity` 里。唯一场景 `SolarSystem` 用 `Skybox_Starfield` 材质
（星空 HDR），环境光取 Flat 暗蓝灰，反射走 Skybox 探针且压低强度，雾关闭——
整体偏暗，以配合程序化表面的发光与测光。具体数值在场景文件里。

天空盒的采样方式与逐轴 wrap 契约见 [PLANET-SOLARSYSTEM.md](docs/PLANET-SOLARSYSTEM.md) §5，
导入类故障见 [PLANET-PITFALLS.md](docs/PLANET-PITFALLS.md) §6。

---

## 5. 场景

| 场景 | Build index | 定位 |
|---|---|---|
| `SolarSystem.unity` | 0 | **全工程唯一的场景**：新增功能、比例与验证都以它为准 |

早期还有一个贴图版地球／月球的参考场景与一个模板空场景，已于 2026-10-06 连同其专用资产一起移除
（见 [DEVLOG.md](docs/DEVLOG.md)）；目录里现在只有这一条线，不存在"参考场景 vs 正式场景"的分工。

`SolarSystem.unity` 由 `Tools ▸ Planet ▸ Rebuild Solar System Scene` 生成，**不是手工摆放的**：
改了 `SolarSystemCatalog` / `SolarSystemFactory` 里的数字后重跑菜单，不要手工在 Hierarchy 里挪物体。
星球自己的场景缩放是**序列化在星球组件上**的派生缓存，改目录或 `kmPerUnit` 后必须重跑烘焙菜单，
规则见 [DEVELOPMENT-WORKFLOW.md](docs/DEVELOPMENT-WORKFLOW.md) §4.1。

场景对象构成、比例选择与轨道力学见 [PLANET-SOLARSYSTEM.md](docs/PLANET-SOLARSYSTEM.md)。

---

## 6. 上手顺序

1. 打开 `Assets/Scenes/SolarSystem.unity` → Play。按 `1`–`0` 在太阳与行星间飞行，`空格` 暂停，
   `-` / `=` 调时间流速看公转快慢，`L` 开关经纬网看自转；用滚轮贴近一颗行星，
   控制台会在跨过地表模式闸门时打出 `[SurfaceMode] ENTER` / `EXIT`。
   完整键位见 [PLANET-SOLARSYSTEM.md](docs/PLANET-SOLARSYSTEM.md) §4。
2. 改程序化星球的外观：先读 [PLANET-PROCEDURAL.md](docs/PLANET-PROCEDURAL.md) 与
   [PLANET-MATERIALS.md](docs/PLANET-MATERIALS.md)，改 `ProceduralTerra.asset` 或
   `BodyMaterials/<Name>.asset`，再跑 **Build Solar System Materials**
   （改到噪声 / 公式时必须提升 `GeneratorVersion`）。
3. 准备开分支或合并前，必读 [DEVELOPMENT-WORKFLOW.md](docs/DEVELOPMENT-WORKFLOW.md) §1 与 §5–§7。

---

## 7. MCP 集成与多工作树

工程已接入 **MCP for Unity**，编辑器工具可通过本机客户端驱动。服务端与工具清单见
`MCP-Setup/MCP-HANDSHAKE-REPORT.md` 与 `MCP-Setup/mcp-tool-list.json`。

**桥接是单消费者的**：同一时刻只应一个客户端驱动编辑器。

### 驱动前必须做的自检

多个工作树各自开着 Unity 时，**每个实例注册自己的端口**，写在
`%USERPROFILE%\.unity-mcp\unity-mcp-port-<hash>.json` 与 `unity-mcp-status-<hash>.json` 里。

MCP 客户端会**记住上次连接的实例**，而且各工作树的场景与脚本处在同一基线上——
读出来的结构完全一样，**不专门查 `Application.dataPath` 根本发现不了**，
你会以为改的是自己的工程。所以每次驱动前先确认归属：

```
unity_reflect / execute_code → Application.dataPath        # 确认是哪个工作树
set_active_instance "<projectName>@<hash>"                  # 切到自己的实例
```

端口表**只在启动时可靠**：客户端可能在会话中途换实例。稳妥做法是在 `execute_code` 里加一道
`Application.dataPath` 断言，并核对程序集 `asm.Location` 落在哪个工作树。

另外两个限制：

- **Unity 窗口失去焦点时不会自动刷新资源库**：新建的 asmdef / 脚本如果没被导入，
  显式调一次 `AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate)` 即可。
- **`execute_code` 走 CodeDom（C# 6）**：`Enum.GetValues<T>()`、表达式体成员、插值字符串都不可用。
  测试代码与工程代码走正常编译，不受此限。

（以上现象的完整记录与恢复方式见 [PLANET-PITFALLS.md](docs/PLANET-PITFALLS.md) §7–§8。）
