# Planet — 项目概览

> 生成时间：2026-10-02 ｜ 数据来源：Unity Editor 实时查询（MCP）+ 工程文件直读

---

## 1. 基本信息

| 项 | 值 |
|---|---|
| 项目名 | `Planet` |
| 路径 | `D:\UnityProjects\Planet` |
| 公司名 | `DefaultCompany` |
| 版本号 | `0.1.0` |
| Unity 版本 | **2022.3.62f3**（LTS，revision `96770f904ca7`） |
| 渲染管线 | **Universal (URP) 14.0.12** |
| 色彩空间 | Linear |
| 脚本后端 | Mono（Windows Standalone 默认） |
| API 兼容级别 | .NET Standard 2.0（`apiCompatibilityLevel = 6`） |
| 默认分辨率 | 1024 × 768（窗口模式） |
| 版本控制 | **未接入**（无 git 仓库、无 `.gitignore`） |
| 模板来源 | Unity URP Empty Template（`Readme.asset` 标题为 "URP Empty Template"） |

---

## 2. 技术栈与依赖

`Packages/manifest.json` 实际依赖（共 18 个显式包）：

**渲染**
- `com.unity.render-pipelines.universal` **14.0.12** — 主渲染管线
- `com.unity.burst` 1.8.21 — 高性能数学编译

**输入**
- `com.unity.inputsystem` **1.19.0** — 新输入系统（`activeInputHandler = 1`，即**仅**新输入系统）

**编辑器与工具链**
- `com.unity.ide.rider` 3.0.40
- `com.unity.ide.visualstudio` 2.0.26
- `com.unity.collab-proxy` 2.12.4
- `com.unity.test-framework` 1.1.33
- `com.unity.timeline` 1.8.12
- `com.unity.visualscripting` 1.9.11
- `com.unity.textmeshpro` 3.0.7
- `com.unity.ugui` 1.0.0

**MCP 集成**
- `com.coplaydev.unity-mcp` **10.0.0**（嵌入式包 `file:com.coplaydev.unity-mcp`）

**内置模块**：ai、animation、audio、physics/2D、particlesystem、terrain、tilemap、ui/uielements、video、vr、xr、subsystems 等约 27 个。

---

## 3. 场景内容

### `Assets/Scenes/SampleScene.unity`（build index 0，唯一场景）

模板自带的空场景，**仅 3 个 GameObject，无任何游戏内容**：

| 对象 | 组件 | 关键参数 |
|---|---|---|
| **Main Camera** | Transform, Camera, AudioListener, `UniversalAdditionalCameraData` | FOV 60，近裁 0.3，远裁 1000，透视，Clear Flags = Skybox，背景色 `(0.192, 0.302, 0.475)` |
| **Directional Light** | Transform, Light, `UniversalAdditionalLightData` | 类型 Directional，白色 `(1,1,1)`，强度 1，软阴影，Shadow Resolution −1（默认） |
| **Global Volume** | Transform, Volume | 引用 `Assets/Scenes/SampleScene/Global Volume Profile.asset` |

场景中**没有**：地面/地形、任何模型、预制体、材质、光照贴图、NavMesh 烘焙数据。`m_NavMeshData: {fileID: 0}`（未烘焙）。

---

## 4. 渲染管线配置

### Render Pipeline Assets

| 资产 | 用途 | Renderer |
|---|---|---|
| `Assets/Settings/PC_RPAsset.asset` | **当前使用**（Graphics 默认 + Quality「PC」） | `PC_Renderer` |
| `Assets/Settings/Mobile_RPAsset.asset` | 备用（Quality「Mobile」） | `Mobile_Renderer` |

### 当前生效配置（实时查询）

```
Pipeline      : Universal (URP)
Quality Level : PC（m_CurrentQuality = 1）
Render Scale  : 1.0
HDR           : 开启
MSAA          : 1x（关闭）
Shadow Distance : 50
Shadow Cascades : 4
Soft Shadows  : 开启
附加光源上限  : 4
Color Grading : Low Dynamic Range
```

### Renderer Feature

`PC_Renderer` 上有 **1 个** Renderer Feature：
- **ScreenSpaceAmbientOcclusion**（已激活）

### Volume 配置

- `Assets/Settings/DefaultVolumeProfile.asset` — 全局默认
- `Assets/Settings/SampleSceneProfile.asset` — 场景级
- `Assets/Scenes/SampleScene/Global Volume Profile.asset` — 场景内 Global Volume 使用

### Quality 等级

| 等级 | 自定义管线 |
|---|---|
| Mobile | `Mobile_RPAsset` |
| PC | `PC_RPAsset` ← 当前 |

### 渲染层（URP Rendering Layers）

`Default` + `Light Layer 1` ~ `Light Layer 7`。

---

## 5. 资产清单（全量）

整个 `Assets/` 只有 **15 个非 meta 文件**，合计 **约 0.2 MB**：

```
Assets/
├── InputSystem_Actions.inputactions          41.0 KB
├── Readme.asset                               1.1 KB
├── Scenes/
│   ├── SampleScene.unity                     11.5 KB
│   └── SampleScene/Global Volume Profile.asset 1.1 KB
├── Settings/
│   ├── DefaultVolumeProfile.asset            24.0 KB
│   ├── Mobile_Renderer.asset                  1.7 KB
│   ├── Mobile_RPAsset.asset                   4.3 KB
│   ├── PC_Renderer.asset                      4.1 KB
│   ├── PC_RPAsset.asset                       4.7 KB
│   ├── SampleSceneProfile.asset               3.7 KB
│   └── UniversalRenderPipelineGlobalSettings.asset 25.6 KB
└── TutorialInfo/
    ├── Icons/URP.png                         24.1 KB
    ├── Layout.wlt                            15.5 KB
    └── Scripts/
        ├── Readme.cs                          0.3 KB
        └── Editor/ReadmeEditor.cs             6.7 KB
```

**代码**：唯一两个 `.cs` 是模板自带的 `Readme.cs` / `ReadmeEditor.cs`（用于显示模板欢迎页），**没有业务脚本**。

**缺失的常见资产类型**：模型（`.fbx/.obj`）、纹理（除模板图标外）、材质（`.mat`）、预制体（`.prefab`）、动画、音频、Shader。

---

## 6. 输入系统

`Assets/InputSystem_Actions.inputactions`（41 KB，模板自带）：

| Action Map | Actions | Bindings |
|---|---|---|
| **Player** | 9 | 35 |
| **UI** | 10 | 38 |

**Control Schemes**：`Keyboard&Mouse`、`Gamepad`、`Touch`、`Joystick`、`XR`

> 注意：输入资产已存在，但场景中**没有任何对象引用它**（无 `PlayerInput` 组件）。

---

## 7. Tags / Layers

- **Tags**：空（仅内置 `Untagged` 等）
- **Layers**：`Default`、`TransparentFX`、`Ignore Raycast`、`Water`、`UI`（标准配置，未新增自定义层）
- **Sorting Layers**：仅 `Default`

---

## 8. 构建配置

```
Scenes in Build : Assets/Scenes/SampleScene.unity (enabled, index 0)
Build Target    : StandaloneWindows64
```

---

## 9. 磁盘占用

| 目录 | 大小 |
|---|---|
| `Library/` | **1,981.7 MB** |
| `Packages/` | 3.4 MB |
| `Logs/` | 0.9 MB |
| `Assets/` | 0.2 MB |
| `ProjectSettings/` | 0.1 MB |
| `UserSettings/` | 0.1 MB |
| `MCP-Setup/` | < 0.1 MB |

`Library/` 占了 99.99% —— 属正常（Unity 导入缓存），可安全删除后重建。

---

## 10. MCP 集成状态

当前工程已接入 **MCP for Unity**，四个本机客户端可驱动编辑器：

| 客户端 | 状态 |
|---|---|
| DSH | ✅ 已验证（可实际读写本工程场景） |
| codex | ✅ 已验证（`codex mcp list` 显示 enabled） |
| opencode | ✅ 已验证（`opencode mcp list` 显示 connected） |
| MiMo AI | ⚠️ 配置已写入，需人工在界面确认 |

- 服务端：`mcp-for-unity-server` 3.4.7，协议 `2024-11-05`
- 桥接：`127.0.0.1:6400`（**单消费者**，同一时刻只应一个客户端驱动）
- 实例标识：`Planet@73cccd62`
- 注册工具：**47 个** + 19 个资源

详见 `MCP-Setup/MCP-HANDSHAKE-REPORT.md` 与 `MCP-Setup/mcp-tool-list.json`。

---

## 11. 现状评估

### 结论

**这是一个几乎空白的新建 URP 模板工程**，尚无任何实际项目内容。名字叫 "Planet"，但场景里没有星球、地形或任何相关资产——目前只是准备好了一套可用的 URP 渲染环境。

### 工程健康度

| 项 | 状态 |
|---|---|
| 编译错误 | ✅ 0 |
| 编辑器控制台 | ✅ 仅 1 条无害警告（Unity 6 遗留布局 `default-6000.dwlt`） |
| URP 管线 | ✅ 正常（`UniversalAdditionalCameraData` / `UniversalAdditionalLightData` 均在位） |
| 包依赖一致性 | ✅ 已归一至 Unity 2022.3 兼容集 |

### 值得注意的风险

1. **未接入版本控制** —— 无 git 仓库、无 `.gitignore`。当前 `Library/` 近 2 GB，一旦纳入版本控制会非常痛苦。建议尽早 `git init` 并添加 Unity 标准 `.gitignore`。
2. **不要用 Unity 6 打开本工程**。工程 `Library/` 曾被 Unity 6.3 打开并降级，`manifest.json` 也一度混用 Unity 6 包（URP 17.3.0、`ai.navigation` 2.0.12、`multiplayer.center` 等），已修复。再用 Unity 6 打开会重新污染。
3. **`com.unity.ai.navigation` 已移除**。原 Unity 6 模板引入的 2.0.12 版本在 2022.3 下编译失败（调用了不存在的 `NavMesh.SetLinkActive` 等 API），且工程并未引用该包，故移除。若日后需要寻路导航，请安装 2022.3 兼容的 1.x 版本。
4. **Quality 默认是 PC**，但 `Mobile_RPAsset` 仍在 —— 若目标平台包含移动端，需核对 Mobile 档位的参数。

### 若要开始做 "Planet"

从零开始的话，通常的下一步顺序：

1. `git init` + Unity `.gitignore`（强烈建议先做）
2. 确定玩法与技术方向（是否用 DOTS、程序化生成、HDRP 还是 URP）
3. 建立场景骨架：地面/球体、光照、天空盒（当前背景是纯色 + 默认天空盒）
4. 用 MCP 接入 AI 工作流批量生成/摆放资产

---

*本概览由 Unity Editor 实时状态生成，非推测。*
