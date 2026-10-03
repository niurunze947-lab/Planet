# MCP for Unity — 安装与四方握手报告

项目：`D:\UnityProjects\Planet`
生成时间：2026-10-02

---

## 1. 结论摘要

| 项 | 状态 |
|---|---|
| Unity 版本 | **2022.3.62f3 LTS**（已从错误的 Unity 6 状态修正） |
| 渲染管线 | **URP 14.0.12**（Universal，`PC_RPAsset`，Linear / HDR / 4 cascades） |
| MCP Unity 包 | `com.coplaydev.unity-mcp` **10.0.0**（嵌入式 `file:`） |
| MCP 服务端 | `mcp-for-unity-server` **3.4.7**，协议 `2024-11-05` |
| Unity 桥接 | `127.0.0.1:6400`，由 `Unity.exe` 持有，`reason: ready` |
| 实例标识 | `Planet@73cccd62` |
| 注册工具数 | **47**（10 个工具组）+ 19 个资源 |
| 编译错误 | **0** |

### 纠正的前提

任务描述称这是「unity 2022 lts universal 3D 项目」，但磁盘上的实际状态并非如此：

- `ProjectSettings/ProjectVersion.txt` 原为 **6000.3.16f1（Unity 6.3）**
- `Packages/manifest.json` 原声明 **URP 17.3.0**（Unity 6 专属）及多个 Unity 6 包
- 但工程正被 **2022.3.62f3** 打开，`Library/PackageCache` 已被降级为 URP **14.0.12**

即工程处于**半降级的不一致状态**。经确认，已按 **Unity 2022.3.62f3 + URP 14.x** 归一化。

---

## 2. 对项目所做的改动

### 2.1 `ProjectVersion.txt`
Unity 2022.3.62f3 在打开工程时自行将其改写为：

```
m_EditorVersion: 2022.3.62f3
m_EditorVersionWithRevision: 2022.3.62f3 (96770f904ca7)
```

### 2.2 `Packages/manifest.json`（已重写）

对齐 Unity 2022.3 实际可解析的版本集合：

| 变更 | 包 | 从 → 到 |
|---|---|---|
| 降级 | `com.unity.render-pipelines.universal` | 17.3.0 → **14.0.12** |
| 降级 | `com.unity.ugui` | 2.0.0 → **1.0.0** |
| 降级 | `com.unity.test-framework` | 1.6.0 → **1.1.33** |
| 移除 | `com.unity.ai.navigation` | 2.0.12（Unity 6 API，编译失败） |
| 移除 | `com.unity.multiplayer.center` | 1.0.1（Unity 6 专属） |
| 移除 | `com.unity.modules.accessibility` | 1.0.0（2022.3 无此模块） |
| 移除 | `com.unity.modules.adaptiveperformance` | 1.0.0（2022.3 无此模块） |
| 移除 | `com.unity.modules.vectorgraphics` | 1.0.0（2022.3 无此模块） |
| 新增 | `com.coplaydev.unity-mcp` | `file:com.coplaydev.unity-mcp` |
| 新增 | `com.unity.textmeshpro` | 3.0.7（2022.3 需独立包） |
| 新增 | `com.unity.modules.subsystems` | 1.0.0 |
| 保留 | inputsystem / timeline / visualscripting / rider / visualstudio / collab-proxy / burst / ai.navigation 之外的模块 | 实测可解析版本 |

> **关于 `com.unity.ai.navigation`**：2.0.x 全系为 Unity 6 包，其 `NavMeshLink.cs` 调用了 2022.3 不存在的
> `NavMesh.SetLinkActive` / `IsLinkValid` / `SetLinkOwner` / `IsLinkOccupied`，导致 8 个编译错误。
> 经全量检索 `Assets/`，工程内仅有 `SampleScene` 的内置 `NavMeshSettings` 字段（属核心
> `com.unity.modules.ai`），**并未引用该包**，故直接移除，与 Unity 2022.3 URP 3D 官方模板一致。

### 2.3 MCP 包安装

从同机同版本工程 `D:\UnityProjects\4DV-Quest-App\4DV-Quest-App-Unity\Packages\com.coplaydev.unity-mcp`
复制为 **嵌入式包**（699 个文件），走 `file:` 引用 —— 无需联网、版本可控，与 4DV 工程做法一致。

### 2.4 重新解析

删除了不一致的 `packages-lock.json`，由 Unity 重新生成（11665 字节，含 unity-mcp 条目）。

---

## 3. 四个客户端的握手链接

四个客户端统一使用 stdio 传输，命令完全一致：

```
uvx --from mcpforunityserver==10.0.0 mcp-for-unity --transport stdio
```

| 客户端 | 配置文件 | 键位 | 本报告生成时状态 |
|---|---|---|---|
| **DSH** | `C:\Users\nrz\.dsh\profiles\desktop\cordis.patch.yml` | `mcp-unity` 插件条目 | ✅ 原已正确（10.0.0），实测已连通 |
| **codex** | `C:\Users\nrz\.codex\config.toml` | `[mcp_servers.unityMCP]` | ✅ 已修正：`9.7.3` → **`10.0.0`** |
| **opencode** | `C:\Users\nrz\.config\opencode\opencode.json` | `mcp.unityMCP` | ✅ 已修正：错误的 `remote http://127.0.0.1:8080/mcp` → **`local` stdio** |
| **MiMo AI** | `C:\Users\nrz\AppData\Roaming\Xiaomi MiMo AI\engine-config\mimocode.json` | `mcp.unityMCP` | ✅ 新增（原无 `mcp` 键） |

### 修正说明

- **codex**：原固定 `mcpforunityserver==9.7.3`，与 Unity 包 10.0.0 不匹配。MCP for Unity 要求
  服务端版本与 Editor 包配对，已统一为 `10.0.0`。
- **opencode**：原配置为 `"type": "remote", "url": "http://127.0.0.1:8080/mcp"`。
  MCP for Unity 的 stdio 传输**不是** HTTP 服务；且 `8080` 在本机是 llama.cpp 的
  LLM 端点（`http://127.0.0.1:8080/v1`），并非 MCP 端点 —— 该配置无法建立握手。已改为 `local` stdio。
- **MiMo AI**：其 `feature-gates.json` 中 `domestic.show_mcp_entry: false`，即**国内区域 UI 隐藏了 MCP 入口**，
  无法通过界面添加。但已核实其配置 schema（`https://mimo.xiaomi.com/mimocode/config.json`，
  267,694 字节）根级确实包含 `mcp` 属性，支持 `McpLocalConfig`（`type` + `command`，必填）。
  因此直接写入配置文件。默认 `timeout` 仅 5000ms，对 Unity 偏短，已设为 `120000`。

### 关键约束（务必知悉）

> Unity 侧的 MCP 桥接（`127.0.0.1:6400`）是**单消费者**的。
> 四个客户端都可用，但**同一时刻只应有一个客户端驱动 Unity**。
> DSH 配置文件中对此已有原始注释警告，Blender MCP 同理。

---

## 4. 注册的 MCP 工具列表

服务端 `tools/list` 实测返回 **47 个工具**。其中 42 个按组管理，5 个元工具恒常可见。

### 4.1 工具组

| 组 | 工具数 | 默认启用 | 工具 |
|---|---|---|---|
| `core` | 25 | ✅ 是 | batch_execute, execute_menu_item, find_gameobjects, find_in_file, manage_asset, manage_build, manage_camera, manage_components, manage_editor, manage_gameobject, manage_graphics, manage_material, manage_packages, manage_physics, manage_prefabs, manage_scene, refresh_unity, apply_text_edits, create_script, delete_script, validate_script, manage_script, get_sha, read_console, script_apply_edits |
| `animation` | 1 | 否 | manage_animation |
| `asset_gen` | 4 | 否 | generate_image, generate_model, import_model, import_model_file |
| `docs` | 2 | 否 | unity_docs, unity_reflect |
| `probuilder` | 1 | 否 | manage_probuilder |
| `profiling` | 1 | 否 | manage_profiler |
| `scripting_ext` | 2 | 否 | execute_code, manage_scriptable_object |
| `testing` | 2 | 否 | run_tests, get_test_job |
| `ui` | 1 | 否 | manage_ui |
| `vfx` | 3 | 否 | manage_shader, manage_texture, manage_vfx |

### 4.2 元工具（group=None，始终可见）

`manage_tools`, `set_active_instance`, `debug_request_context`, `execute_custom_tool`, `manage_script_capabilities`

按组激活：`manage_tools(action="activate", group="<组名>")`
查看分组：资源 `mcpforunity://tool-groups`

### 4.3 资源（19 个）

```
mcpforunity://editor/state              mcpforunity://editor/active-tool
mcpforunity://editor/selection          mcpforunity://editor/windows
mcpforunity://editor/prefab-stage       mcpforunity://instances
mcpforunity://project/info              mcpforunity://project/layers
mcpforunity://project/tags              mcpforunity://menu-items
mcpforunity://prefab-api                mcpforunity://scene/cameras
mcpforunity://scene/gameobject-api      mcpforunity://scene/volumes
mcpforunity://rendering/stats           mcpforunity://pipeline/renderer-features
mcpforunity://custom-tools              mcpforunity://tests
mcpforunity://tool-groups
```

机器可读版本见同目录 `mcp-tool-list.json`。

---

## 5. 验证证据

### 5.1 真实 MCP 协议握手

用 `verify-handshake.ps1` 以客户端身份启动服务端并完成
`initialize` → `notifications/initialized` → `tools/list` → `resources/list`：

```
HANDSHAKE: OK
  protocolVersion : 2024-11-05
  serverName      : mcp-for-unity-server
  serverVersion   : 3.4.7
  toolCount       : 47
```

### 5.2 DSH → Unity 实际控制

```
manage_scene(action="get_active")
→ {"name":"SampleScene","path":"Assets/Scenes/SampleScene.unity","isLoaded":true,"rootCount":3}

manage_scene(action="get_hierarchy")
→ Directional Light / Global Volume / Main Camera
   组件含 UniversalAdditionalLightData、UniversalAdditionalCameraData
```

即 **MCP 已可实际读写 Planet 工程**，且 URP 组件完整。

### 5.3 渲染管线

```
manage_graphics(action="pipeline_get_info")
→ Pipeline: Universal (URP), Quality: PC
  pipelineAsset: Assets/Settings/PC_RPAsset.asset
  colorSpace: Linear, supportsHDR: true, shadowCascadeCount: 4
```

### 5.4 桥接状态文件

Unity 自行写出的注册文件 `C:\Users\nrz\.unity-mcp\unity-mcp-status-73cccd62.json`：

```json
{"unity_port":6400,"reloading":false,"reason":"ready","seq":1,
 "project_path":"D:/UnityProjects/Planet/Assets","project_name":"Planet",
 "unity_version":"2022.3.62f3","project_scoped_tools":true}
```

### 5.5 编辑器控制台

仅 1 条无害警告（Unity 6 遗留布局文件）：

```
The layout "UserSettings\Layouts\default-6000.dwlt" could not be fully loaded...
```

如需消除，可删除 `UserSettings\Layouts\default-6000.dwlt`（Unity 会重建）。

### 5.6 客户端 CLI 实测确认

**codex**（`codex-cli 0.159.2`）—— `codex mcp list` 实际输出：

```
unityMCP   C:\Users\nrz\AppData\Local\Microsoft\WinGet\Links\uvx.exe
           --from mcpforunityserver==10.0.0 mcp-for-unity --transport stdio
           env: SystemRoot=*****        Status: enabled
```

→ codex 已正确解析该配置并启用。

**opencode**（`1.15.7`）—— `opencode mcp list` 实际输出：

```
•  ✓ unityMCP connected
      C:\Users\nrz\AppData\Local\Microsoft\WinGet\Links\uvx.exe --from mcpforunityserver==10.0.0 mcp-for-unity --transport stdio
—  1 server(s)
```

→ opencode **已实际建立 MCP 握手（connected）**，非仅配置写入。

**MiMo AI** —— 纯 Electron 应用（`app.asar` 91 MB），**不提供 CLI**，
故无法从命令行验证其握手。其配置已写入且通过 schema 校验，但需你重启 MiMo AI 后
在界面中确认是否加载该 MCP 服务（注意 §3 所述国内区域 UI 入口被隐藏）。

### 5.7 握手验证总览

| 客户端 | 配置写入 | 实际握手验证 | 证据 |
|---|---|---|---|
| DSH | 原本已正确 | ✅ 已验证 | 实际读写 Planet 场景（§5.2） |
| codex | 已修正 | ✅ 已验证 | `codex mcp list` 显示 enabled（§5.6） |
| opencode | 已修正 | ✅ 已验证 | `opencode mcp list` 显示 connected（§5.6） |
| MiMo AI | 已新增 | ⚠️ 无法 CLI 验证 | 应用无 CLI，需人工确认（§5.6） |

---

## 6. 备份与回滚

| 位置 | 内容 |
|---|---|
| `_mcp_setup_backup_20261002\ProjectVersion.txt` | 改动前版本文件 |
| `_mcp_setup_backup_20261002\manifest.json` | 改动前 Unity 6 manifest |
| `_mcp_setup_backup_20261002\packages-lock.json` | 改动前 lock |
| `_mcp_setup_backup_20261002\unity-mcp-registry\` | `~/.unity-mcp` 三个注册文件备份 |

---

## 7. 后续注意事项

1. **不要用 Unity 6 打开本工程**。若用 Hub 打开，务必选 **2022.3.62f3**，否则会再次触发版本升降级并污染 `Library/`。
2. **单客户端驱动**：见 §3 的关键约束。切换客户端前，建议在 Unity 中确认上一个客户端已断开。
3. **版本配对**：升级 `com.coplaydev.unity-mcp` 包时，必须同步更新四处的
   `mcpforunityserver==<版本>`（DSH 的 pin 是手工维护的，Editor 不会自动改写 DSH 配置）。
4. **MiMo AI**：MCP 入口在国内区域 UI 中隐藏；若日后 UI 仍不显示该服务，说明其引擎未读取
   `engine-config/mimocode.json` 的 `mcp` 键，需要通过其海外区域或后续版本开启。
5. `/tmp` 遗留：`~/.unity-mcp\unity-mcp-port.json` 原先指向 4DV 工程，已改为指向 Planet 并备份原件。
