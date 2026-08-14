# UTMT MCP Bridge

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-brightgreen.svg)](https://github.com/yourname/utmt-mcp-bridge/pulls)
[![Made for UndertaleModTool](https://img.shields.io/badge/for-UndertaleModTool-blueviolet)](https://github.com/krzys-h/UndertaleModTool)

[![English](https://img.shields.io/badge/Language-English-blue.svg)](README.md)
[![中文](https://img.shields.io/badge/Language-中文-red.svg)](README.zh-CN.md)
[![请我喝咖啡](https://img.shields.io/badge/💖-捐赠-orange.svg)](DONATE.md)

将 **UndertaleModTool (UTMT)** 以 [Model Context Protocol (MCP)](https://modelcontextprotocol.io) 工具的形式暴露给任意 AI 客户端——检查 GameMaker 游戏、读取/反编译/修改 GML 代码、列出资源、执行任意 C# 脚本，全部通过你的 AI 代理完成。

---

## ✨ 功能

| 工具 | 说明 |
|------|------|
| `UTMT_info` | 游戏元数据（名称、代码/精灵/对象/房间/声音数量） |
| `UTMT_code_list` | 列出 GML 代码条目（支持名称过滤 + 数量限制） |
| `UTMT_code_get` | 反编译并获取指定代码条目的 GML 源码 |
| `UTMT_code_set` | 编译并替换游戏中的 GML 代码 |
| `UTMT_search` | 跨代码 / 精灵 / 对象 / 房间搜索 |
| `UTMT_sprites` | 列出精灵及其尺寸 |
| `UTMT_objects` | 列出游戏对象 |
| `UTMT_rooms` | 列出房间及尺寸 |
| `UTMT_save` | 保存已加载的 `data.win` |
| `UTMT_run` | 在 UTMT 上下文中执行任意 C#（`code=` 或 `b64=` 传参） |

## 🏗 架构

```
┌─────────────────────┐      stdio (JSON-RPC)      ┌──────────────────────┐
│  MCP 客户端          │ ◄────────────────────────► │ undertale_mcp_server │
│  (Hermes / Claude / │                            │  .py — Python, WSL / │
│   Cursor)           │                            │  Linux / macOS       │
└─────────────────────┘                            └──────────┬───────────┘
                                                              │ HTTP :9500
                                                   ┌──────────▼───────────┐
                                                   │  mcp_bridge_v6.csx   │
                                                   │  C# 脚本, 在 UTMT 内部 │
                                                   │  运行 (Windows)       │
                                                   └──────────────────────┘
```

- **`server/undertale_mcp_server.py`** — MCP 服务端，通过 stdio JSON-RPC 通信，再经 HTTP 转发给 UTMT。
- **`bridge/mcp_bridge_v6.csx`** — 在 UndertaleModTool 内部执行的 C# 脚本，在 `9500` 端口启动 `TcpListener` HTTP API。后台线程运行，UI 保持响应。

## 📦 环境要求

- **Windows** 上安装 [UndertaleModTool](https://github.com/krzys-h/UndertaleModTool)（`.csx` 脚本在其 C# 脚本控制台中运行）
- **Python 3.8+** 运行 MCP 服务端（WSL / Linux / macOS / Windows 均可）
- 任意支持 MCP 的客户端（Hermes Agent、Claude Desktop、Cursor 等）

## 🚀 快速开始

### 1. 在 UTMT 中启动桥接（Windows）

1. 打开 UndertaleModTool 并加载游戏（`data.win`）。
2. 从菜单栏打开 **Scripts → Run Script**。
3. 加载并运行 `bridge/mcp_bridge_v6.csx`。
4. 看到 `MCP Bridge v6 on port 9500` 即成功，桥接监听在 `0.0.0.0:9500`。

> ⚠ 使用 MCP 工具期间请保持 UTMT 打开。

### 2. 配置 MCP 服务端

在 MCP 客户端配置中添加。以 **Hermes Agent**（`~/.hermes/config.yaml`）为例：

```yaml
mcp_servers:
  undertale:
    command: python3
    args:
      - /path/to/server/undertale_mcp_server.py
    timeout: 30
    connect_timeout: 10
```

### 3. 验证连接

```bash
# 直接 ping 桥接
curl http://<windows主机IP>:9500/ping
# → {"ok":true,"game":"你的游戏名"}

# 测试 MCP 服务端（Hermes CLI）
hermes mcp test undertale
```

### 4. 开始使用

直接让你的 AI 代理执行：*"列出所有包含 'twitch' 的 GML 代码条目"*，或 *"获取 `scr_newGame` 的源码"*，或 *"把 `global.dev` 改成 1"*。

## 💬 使用示例

**1. 读取反编译函数**
> 你：*`scr_newGame` 是干什么的？*
> 代理：调用 `UTMT_code_get(name="scr_newGame")` → 返回完整 GML 源码。

**2. 跨全游戏搜索资源**
> 你：*找出所有和 "vip" 相关的东西。*
> 代理：调用 `UTMT_search(query="vip")` → 列出匹配的代码 / 精灵 / 对象 / 房间。

**3. 修改 GML 并保存**
> 你：*把 `scr_newGame` 里的 `global.dev` 改成 1，然后保存。*
> 代理：调用 `UTMT_code_get` → `UTMT_code_set(name="scr_newGame", gml="<修改后>")` → `UTMT_save`。之后记得在 UTMT 里手动保存文件。

**4. 对已加载游戏执行自定义 C# 片段**
> 你：*列出所有有精灵的对象。*
> 代理：调用 `UTMT_run(code="string.Join(\",\", Data.GameObjects.Where(o => o.Sprite != null).Select(o => o.Name.Content))")`。

## 🌐 网络配置

MCP 服务端会自动从 `/proc/net/route` 读取默认网关（WSL 场景即为 Windows 主机 IP）来定位桥接地址，无需手动配置。如需覆盖：

| 环境变量 | 默认值 | 说明 |
|---|---|---|
| `UTMT_BRIDGE_URL` | 自动探测（网关 IP） | 桥接完整地址，如 `http://192.168.1.100:9500` |

```bash
export UTMT_BRIDGE_URL="http://192.168.1.100:9500"
```

或通过 Hermes 配置：

```yaml
mcp_servers:
  undertale:
    command: python3
    args: [/path/to/server/undertale_mcp_server.py]
    env:
      UTMT_BRIDGE_URL: "http://192.168.1.100:9500"
```

## 🔌 HTTP API 参考

桥接暴露一组极简的 JSON-over-HTTP 接口（均支持 POST + JSON body）：

| 端点 | Body | 返回 |
|---|---|---|
| `GET /ping` | – | `{"ok":true,"game":"..."}` |
| `POST /info` | – | 游戏元数据与资源数量 |
| `POST /code` | `{"filter":"twitch","limit":100}` | 代码条目列表 |
| `POST /code/get` | `{"name":"scr_x"}` | 反编译的 GML 源码 |
| `POST /code/set` | `{"name":"scr_x","gml":"..."}` | `{"ok":true}` |
| `POST /sprites` | – | 精灵列表（含尺寸） |
| `POST /objects` | – | 对象列表 |
| `POST /rooms` | – | 房间列表（含尺寸） |
| `POST /search` | `{"query":"twitch"}` | 跨资源搜索 |
| `POST /save` | – | 保存提示 |
| `POST /run` | `{"code":"..."}` 或 `{"b64":"..."}` | C# 脚本执行结果 |

## ⚠️ 常见问题

| 症状 | 原因 | 解决方法 |
|---|---|---|
| `Bridge not running` | UTMT 未打开或脚本未运行 | 在 UTMT 中执行 `.csx` 并保持 UTMT 打开 |
| 连接超时 | 桥接地址错误 | `curl http://<主机IP>:9500/ping`；用 `UTMT_BRIDGE_URL` 修正 |
| WSL 连不上 Windows | 网关 IP 变了 | 自动探测已处理；用 `ip route show default` 核实 |
| 修改 `.csx` 后不生效 | 桥接未重启 | 在 UTMT 中重新运行脚本 |
| `UTMT_code_set` 编译报错 | GML 语法错误 | 修正 GML 语法；UTMT 编译器会给出诊断信息 |

## 🤝 贡献

欢迎提交 PR！请保持桥接脚本零额外依赖（仅使用 UTMT 内置程序集），MCP 服务端仅用 Python 标准库（无需 pip 安装）。
# 捐赠支持 / Donations

如果这个项目对你有帮助，欢迎请我喝杯咖啡 ☕

If this project helped you, feel free to buy me a coffee ☕

## 赞助方式 / Ways to Support

### 🧡 GitHub Sponsors

<https://github.com/sponsors/sirenboke>


### 🚀 ko-fi / AFDIAN

<https:ko-fi.com/hc1091324664gmailcom>


> 💡 如果你愿意，也可以直接通过 GitHub Issue 告诉我你想支持的理由～
> 💡 Tip: You can also tell me why you're supporting via a GitHub Issue if you like!

## 📄 许可证

MIT — 详见 [LICENSE](LICENSE)。
