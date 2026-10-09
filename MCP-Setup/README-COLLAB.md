# MCP 协作说明

本仓库已内置 MCP for Unity，合作者**无需再单独下载插件**：

| 内容 | 位置 |
|------|------|
| Unity 插件包 com.coplaydev.unity-mcp 10.0.0 | `Packages/com.coplaydev.unity-mcp`（`manifest.json` 以 `file:` 嵌入） |
| 握手报告 / 工具清单 / 校验脚本 | `MCP-Setup/` |

## 合作者开箱步骤

1. 用 **Unity 2022.3.x** 打开本工程（会自动解析本地 MCP 包）
2. 菜单 **Window > MCP for Unity** → **Auto-Setup**
3. 需要时用 `MCP-Setup/verify-handshake.ps1` 做一次真实握手校验

> 首次 Auto-Setup 若拉起 Python 服务端，仍会经 `uvx` 取 `mcpforunityserver`（网络一次性）；Unity 插件本体已在仓库内。
