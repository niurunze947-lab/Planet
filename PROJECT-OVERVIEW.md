# Planet — 项目概览

> 更新：2026-10-03 ｜ 数据来源：Unity Editor 实时查询（MCP）+ 工程文件直读 + git 仓库状态
> 上一版把本工程描述为"几乎空白的 URP 模板工程"，那一版已完全过时，本版为准。

---

## 1. 这是什么

一个 **Unity 2022.3 LTS + URP** 的行星演示工程。核心内容是用**公开的真实天文数据**重建地球与月球：

- 地表颜色来自 NASA 真实影像，高程来自真实地形测量数据（含海底地形）
- 星球**不是贴图球**，几何体由 16 位高程图逐顶点径向位移生成
- 背景是 NASA 基于 **Gaia DR2（17 亿颗恒星）** 绘制的 HDR 星空

名字里的 "Planet" 现在名副其实了。

---

## 2. 基本信息

| 项 | 值 |
|---|---|
| 项目名 | `Planet` |
| 路径 | `D:\UnityProjects\Planet` |
| 公司名 / 版本 | `DefaultCompany` / `0.1.0` |
| Unity | **2022.3.62f3**（LTS，revision `96770f904ca7`） |
| 渲染管线 | **Universal (URP) 14.0.12** |
| 色彩空间 | **Linear** |
| 输入系统 | 新输入系统（`activeInputHandler = 1`，**仅**新系统） |
| 序列化模式 | **Force Text**（所有资产 YAML 文本，可 diff） |
| Meta 文件 | Visible Meta Files |
| 默认分辨率 | 1024 × 768（窗口模式） |
| 场景数 | 2（`PlanetDemo`、`SampleScene`） |
| 版本控制 | ✅ **git + Git LFS**，分支 `main`，工作区干净 |

---

## 3. 目录结构

```
D:\UnityProjects\Planet\
├── Assets/                     197.5 MB   ← 入库
│   ├── Scenes/                 2 个场景 + 1 个 Volume Profile
│   ├── Scripts/                3 个 C#（233 行）
│   ├── Settings/               URP 配置、3 个材质、后处理 Profile
│   ├── Textures/               182.9 MB，12 张贴图
│   ├── Screenshots/            14.3 MB，9 张验证截图
│   └── TutorialInfo/           模板自带（可删）
├── Packages/                   3.4 MB     ← 入库
├── ProjectSettings/            0.1 MB     ← 入库
├── MCP-Setup/                  握手报告与工具清单 ← 入库
├── _assets_src/                384.2 MB   ← 仅 .py 与 qa/ 入库
├── _mcp_setup_backup_20261002/ 配置备份    ← 入库
├── .git/                       225.7 MB（含 LFS 对象池）
├── PLANET-ASSETS.md            资产来源与授权
├── PLANET-DEMO.md              演示场景说明
├── PROJECT-OVERVIEW.md         本文档
└── .gitignore / .gitattributes
```

`Library/`（2,353.7 MB）与 `Logs/` 已被 `.gitignore` 排除。

---

## 4. 技术栈与依赖

`Packages/manifest.json` 共 **12 个显式包 + 31 个内置模块**：

| 包 | 版本 | 用途 |
|---|---|---|
| `com.unity.render-pipelines.universal` | 14.0.12 | 主渲染管线 |
| `com.unity.inputsystem` | **1.19.0** | 新输入系统（**仅**此系统） |
| `com.unity.burst` | 1.8.21 | 高性能数学编译 |
| `com.unity.textmeshpro` | 3.0.7 | 文本 |
| `com.unity.timeline` | 1.8.12 | 时间轴 |
| `com.unity.visualscripting` | 1.9.11 | 可视化脚本 |
| `com.unity.ugui` | 1.0.0 | UI |
| `com.unity.test-framework` | 1.1.33 | 测试 |
| `com.unity.ide.rider` | 3.0.40 | IDE |
| `com.unity.ide.visualstudio` | 2.0.26 | IDE |
| `com.unity.collab-proxy` | 2.12.4 | 版本控制面板 |
| `com.coplaydev.unity-mcp` | `file:` 嵌入式 | **MCP 桥接** |

> 版本集已归一至 Unity 2022.3 兼容范围。

---

## 5. 渲染配置

```
Pipeline        : PC_RPAsset (Universal)
Color Space     : Linear
Quality Level   : PC
Shadow Distance : 40
Renderer Feature: ScreenSpaceAmbientOcclusion
备用管线        : Mobile_RPAsset（Quality 档位 "Mobile"）
渲染层          : Default + Light Layer 1~7
```

### 环境设置（当前场景）

| 项 | 值 |
|---|---|
| Skybox | `Skybox_Starfield`（NASA Deep Star Maps 2020，HDR） |
| Ambient | **Flat**，暗蓝灰 `(0.08, 0.09, 0.13)` |
| Reflection | **Skybox**（128px 探针） |
| Fog | 关闭 |

---

## 6. 场景

### `Assets/Scenes/PlanetDemo.unity` ← **build index 0**

| 对象 | 组件 | 说明 |
|---|---|---|
| **Earth** | Transform, MeshFilter, MeshRenderer, `PlanetMeshGenerator`, `PlanetSpin` | (-1.5, 0, 0)，半径 1，296,065 顶点 |
| **Moon** | 同上 | (2.3, 0, 0)，半径 0.2725（真实半径比），131,841 顶点 |
| **Sun** | Transform, Light, `UniversalAdditionalLightData` | 平行光，强度 1.7，软阴影 |
| **Focus** | Transform | 相机环绕中心 |
| **Main Camera** | Transform, Camera, `OrbitCamera`, `UniversalAdditionalCameraData` | FOV 45，SMAA High，后处理开启 |
| **Global Volume** | Transform, Volume | Bloom + ACES + Vignette |

地形起伏实测：**地球为半径的 2.08%，月球为 7.13%**。详情见 `PLANET-DEMO.md`。

### `Assets/Scenes/SampleScene.unity`

模板自带的空场景（Main Camera / Directional Light / Global Volume，无内容），保留作为对照。

---

## 7. 纹理资产与显存

`Assets/Textures/` 共 **12 张贴图，磁盘 182.9 MB**。

| 贴图 | 尺寸 | 格式 | sRGB | GPU |
|---|---|---|---|---|
| `earth_color_8k` | 8192×4096 | BC7 | ✔ | 42.7 MB |
| `earth_color_2k` | 2048×1024 | BC7 | ✔ | 2.7 MB |
| `earth_height_16bit` | 8192×4096 | **R16** | ✘ | 85.3 MB |
| `earth_height_2k_16bit` | 2048×1024 | **R16** | ✘ | 5.3 MB |
| `earth_normal_2k` | 2048×1024 | DXT5 | ✘ | 2.7 MB |
| `earth_metallic_smoothness` | 2048×1024 | DXT5 | ✘ | 2.7 MB |
| `moon_color_8k` | 8192×4096 | BC7 | ✔ | 42.7 MB |
| `moon_color_2k` | 2048×1024 | BC7 | ✔ | 2.7 MB |
| `moon_height_16bit` | 5760×2880 | **R16** | ✘ | 42.2 MB |
| `moon_height_4ppd_16bit` | 1440×720 | **R16** | ✘ | 2.6 MB |
| `moon_normal_2k` | 2048×1024 | DXT5 | ✘ | 2.7 MB |
| `starmap_2020_4k` | 4096×2048 | RGBAHalf | ✘ | 85.3 MB |
| | | | **合计** | **319.5 MB** |

> **关于显存数字的口径**：上表按格式几何计算（含 mip 链，即 ×4/3），是真实的 GPU 占用。
> Unity 的 `Profiler.GetRuntimeMemorySizeLong` 对这 12 张贴图报告 **639.0 MB**，恰好是上表的 1.91 倍——
> 该 API 把 CPU 侧镜像也算进去了。**做显存预算时用 319.5 MB。**

**所有 8K 贴图都已把 `maxTextureSize` 提到 8192** —— Unity 默认值 2048 会静默把所有 8K 图压成 2K。
月球的 5760×2880 与 1440×720 是 NPOT，另需 `npotScale = None`，否则会被降到 4096/1024。

---

## 8. 代码

`Assets/Scripts/` 共 3 个脚本、233 行，**无第三方依赖**：

| 脚本 | 行数 | 职责 |
|---|---|---|
| `PlanetMeshGenerator.cs` | 149 | `[ExecuteAlways]`。从 16 位高程图构建位移球体网格（经纬分段、度→米解码、垂直夸张、海底钳制、法线/切线/包围盒重算、UInt32 索引） |
| `OrbitCamera.cs` | 63 | 左键拖动环绕、滚轮缩放、空闲自动漂移（Esc 停止）。基于新输入系统 |
| `PlanetSpin.cs` | 21 | 自转角速度 + 轴倾角 |

模板自带的 `Readme.cs` / `ReadmeEditor.cs` 仍在 `Assets/TutorialInfo/`，不影响构建。

---

## 9. 版本控制

```
分支        : main
提交        : 1171425  "Initial commit: Unity 2022.3 LTS planet demo with real Earth/Moon assets"
跟踪文件    : 853
LFS 对象    : 26 个（224.7 MB）
工作区      : clean
```

**Git LFS 跟踪**：贴图、模型、音频、视频、字体、压缩包。
**保留为普通文本**（可 diff）：`.unity` `.asset` `.mat` `.prefab` `.meta` `.inputactions` 及全部源码——靠的是 Unity 的 Force Text 模式。

`.gitattributes` 里同时把文本文件统一为 **LF**，并设 `core.autocrlf=false`。
这两件事必须一起做：否则 git 会在每次 checkout 时改写行尾，导致 Unity 反复重新导入。

**未入库的内容**（`_assets_src/` 下约 380 MB）：原始 TIFF/JPG/EXR 与 SRTM 瓦片缓存。
它们可由 `_assets_src/*.py` 从头重新生成，没必要进仓库。
**`.py` 脚本和 `qa/` 对比图是入库的**，所以整条资产管线可复现。

---

## 10. MCP 集成

工程已接入 **MCP for Unity**，四个本机客户端可驱动编辑器：

| 客户端 | 状态 |
|---|---|
| DSH | ✅ 已验证（本次所有场景/资产操作均通过它完成） |
| codex | ✅ 已验证（`codex mcp list` 显示 enabled） |
| opencode | ✅ 已验证（`opencode mcp list` 显示 connected） |
| MiMo AI | ⚠️ 配置已写入，需人工在界面确认 |

- 服务端 `mcp-for-unity-server` 3.4.7，协议 `2024-11-05`
- 桥接 `127.0.0.1:6400` —— **单消费者**，同一时刻只应一个客户端驱动
- 实例标识 `Planet@73cccd62`，注册工具 **47 个** + 19 个资源

详见 `MCP-Setup/MCP-HANDSHAKE-REPORT.md` 与 `MCP-Setup/mcp-tool-list.json`。

---

## 11. 磁盘占用

| 目录 | 大小 | 备注 |
|---|---|---|
| `Library/` | **2,353.7 MB** | 导入缓存，可安全删除重建，**不入库** |
| `_assets_src/` | 384.2 MB | 原始数据缓存，**基本不入库** |
| `.git/` | 225.7 MB | 含 LFS 对象池 |
| `Assets/` | 197.5 MB | **入库** |
| `Packages/` | 3.4 MB | **入库** |
| `Logs/` | 0.9 MB | 不入库 |
| `ProjectSettings/` | 0.1 MB | **入库** |
| `UserSettings/` | 0.1 MB | 不入库 |
| **合计** | **≈ 3,166 MB** | |

---

## 12. 风险与注意事项

1. **不要用 Unity 6 打开本工程。** `Library/` 曾被 Unity 6.3 打开并降级，`manifest.json` 也一度混入 Unity 6 的包（URP 17.3.0、`ai.navigation` 2.0.12 等），已修复。再用 Unity 6 打开会重新污染。

2. **`com.unity.ai.navigation` 已移除。** 原 Unity 6 模板引入的 2.0.12 在 2022.3 下编译失败（调用了不存在的 `NavMesh.SetLinkActive`），且工程并未引用。若日后需要寻路，请装 2022.3 兼容的 1.x。

3. **改高度解码参数会让地形错乱。** `heightOffsetMetres` / `heightSpanMetres` 必须与贴图编码一致：
   ```
   地球：metres = (raw / 65535) * 20000  - 11000
   月球：metres = (raw / 65535) * 32767.5 - 10000
   ```

4. **别为了"颜色好看"把高程图改成 RGBA32。** 那会把 16 位压成 8 位，地球高程量化误差从 **0.154 m 劣化到约 40 m**。R16 在 Inspector 里显示为红色是正常的（只有 R 通道有数据）。

5. **git clone 后必须装 git-lfs**，否则贴图会是指针文件而非图像。

6. **网格分辨率与 CPU 副本的权衡**：`PlanetMeshGenerator` 需要 CPU 读图，用的是 2K/4ppd 高程版本。8K 高程图是给 shader 位移和近景用的。若把网格分段数调大，需要相应换用 8K 高程图，并注意 CPU 端内存（8K R16 的 CPU 副本约 67 MB）。

---

## 13. 文档索引

| 文档 | 内容 |
|---|---|
| `PROJECT-OVERVIEW.md` | 本文档：工程结构与配置总览 |
| `PLANET-ASSETS.md` | 贴图来源、授权、编码公式、复现步骤 |
| `PLANET-DEMO.md` | 演示场景构成、参数、操作方法、"高度图为何是红色" |
| `MCP-Setup/MCP-HANDSHAKE-REPORT.md` | 四客户端握手验证记录 |
| `MCP-Setup/mcp-tool-list.json` | 47 个工具的完整清单 |

---

*本文档数据由 Unity Editor 实时查询与 git 仓库状态生成，非推测。*
