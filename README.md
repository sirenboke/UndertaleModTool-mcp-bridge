# utmt MCP Bridge

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-brightgreen.svg)](https://github.com/yourname/utmt-mcp-bridge/pulls)
[![Made for UndertaleModTool](https://img.shields.io/badge/for-UndertaleModTool-blueviolet)](https://github.com/krzys-h/UndertaleModTool)

[![English](https://img.shields.io/badge/Language-English-blue.svg)](README.md)
[![中文](https://img.shields.io/badge/Language-中文-red.svg)](README.zh-CN.md)

Expose **UndertaleModTool (utmt)** to any [Model Context Protocol (MCP)](https://modelcontextprotocol.io) client as first-class tools — inspect GameMaker games, read/decompile/edit GML code, list resources, and run arbitrary C# scripts, all from your AI agent.

---

## ✨ Features

| Tool | Description |
|------|-------------|
| `utmt_info` | Game metadata (name, code/sprite/object/room/sound counts) |
| `utmt_code_list` | List GML code entries (optional name filter + limit) |
| `utmt_code_get` | Decompile & fetch GML source for a code entry |
| `utmt_code_set` | Compile & replace GML code in the loaded game |
| `utmt_search` | Search across code / sprites / objects / rooms |
| `utmt_sprites` | List sprites with dimensions |
| `utmt_objects` | List game objects |
| `utmt_rooms` | List rooms with sizes |
| `utmt_save` | Save the loaded `data.win` |
| `utmt_run` | Execute arbitrary C# in utmt context (`code=` or `b64=` base64) |

## 🏗 Architecture

```
┌─────────────────────┐      stdio (JSON-RPC)      ┌──────────────────────┐
│  MCP Client         │ ◄────────────────────────► │ undertale_mcp_server │
│  (Hermes / Claude / │                            │  .py — Python, WSL / │
│   Cursor)           │                            │  Linux / macOS       │
└─────────────────────┘                            └──────────┬───────────┘
                                                              │ HTTP :9500
                                                   ┌──────────▼───────────┐
                                                   │  mcp_bridge_v6.csx   │
                                                   │  C# script running   │
                                                   │  inside utmt (Windows)│
                                                   └──────────────────────┘
```

- **`server/undertale_mcp_server.py`** — MCP server speaking stdio JSON-RPC. Bridges to utmt over HTTP.
- **`bridge/mcp_bridge_v6.csx`** — C# script executed inside UndertaleModTool. Hosts a `TcpListener` HTTP API on port `9500`. UI stays responsive; runs on a background thread.

## 📦 Requirements

- **Windows** with [UndertaleModTool](https://github.com/krzys-h/UndertaleModTool) (the `.csx` script runs in its C# scripting console)
- **Python 3.8+** for the MCP server (WSL/Linux/macOS/Windows all fine)
- Any MCP-capable client (Hermes Agent, Claude Desktop, Cursor, …)

## 🚀 Quick Start

### 1. Start the bridge inside utmt (Windows)

1. Open UndertaleModTool and load a game (`data.win`).
2. Open **Scripts → Run Script** from the menu bar.
3. Load and run `bridge/mcp_bridge_v6.csx`.
4. You should see: `MCP Bridge v6 on port 9500`. The bridge listens on `0.0.0.0:9500`.

> ⚠ Keep utmt open while using the MCP tools.

### 2. Configure the MCP server

Add to your MCP client config. Example for **Hermes Agent** (`~/.hermes/config.yaml`):

```yaml
mcp_servers:
  undertale:
    command: python3
    args:
      - /path/to/server/undertale_mcp_server.py
    timeout: 30
    connect_timeout: 10
```

### 3. Verify

```bash
# Ping the bridge directly
curl http://<windows-host-ip>:9500/ping
# → {"ok":true,"game":"Your_Game_Name"}

# Test the MCP server (Hermes CLI)
hermes mcp test undertale
```

### 4. Use it

Ask your agent: *"List all GML code entries containing 'twitch'"*, or *"Get the source of `scr_newGame`"*, or *"Change `global.dev` to 1"*.

## 💬 Example Workflows

**1. Read a decompiled function**
> You: *What does `scr_newGame` do?*
> Agent: calls `utmt_code_get(name="scr_newGame")` → returns the full GML source.

**2. Search for a resource across the whole game**
> You: *Find everything related to "vip".*
> Agent: calls `utmt_search(query="vip")` → lists matching code / sprites / objects / rooms.

**3. Edit GML and save**
> You: *Set `global.dev` to 1 in `scr_newGame`, then save.*
> Agent: calls `utmt_code_get` → `utmt_code_set(name="scr_newGame", gml="<modified>")` → `utmt_save`. Remember to save the file in utmt afterwards.

**4. Run a custom C# snippet against the loaded game**
> You: *List all objects that have a sprite.*
> Agent: calls `utmt_run(code="string.Join(\",\", Data.GameObjects.Where(o => o.Sprite != null).Select(o => o.Name.Content))")`.

## 🌐 Network Configuration

The MCP server auto-detects the Windows host address by reading the default gateway from `/proc/net/route` (WSL). You can override it:

| Environment variable | Default | Description |
|---|---|---|
| `utmt_BRIDGE_URL` | auto-detected (gateway IP) | Full URL of the bridge, e.g. `http://192.168.1.100:9500` |

```bash
export utmt_BRIDGE_URL="http://192.168.1.100:9500"
```

Or via Hermes config:

```yaml
mcp_servers:
  undertale:
    command: python3
    args: [/path/to/server/undertale_mcp_server.py]
    env:
      utmt_BRIDGE_URL: "http://192.168.1.100:9500"
```

## 🔌 HTTP API Reference

The bridge exposes a minimal JSON-over-HTTP API (all endpoints accept POST with a JSON body):

| Endpoint | Body | Returns |
|---|---|---|
| `GET /ping` | – | `{"ok":true,"game":"..."}` |
| `POST /info` | – | Game metadata & resource counts |
| `POST /code` | `{"filter":"twitch","limit":100}` | Code entry list |
| `POST /code/get` | `{"name":"scr_x"}` | Decompiled GML source |
| `POST /code/set` | `{"name":"scr_x","gml":"..."}` | `{"ok":true}` |
| `POST /sprites` | – | Sprite list with dimensions |
| `POST /objects` | – | Object list |
| `POST /rooms` | – | Room list with sizes |
| `POST /search` | `{"query":"twitch"}` | Cross-resource search |
| `POST /save` | – | Save hint |
| `POST /run` | `{"code":"..."}` or `{"b64":"..."}` | C# script result |

## ⚠️ Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `Bridge not running` | utmt closed or script not executed | Run the `.csx` in utmt and keep utmt open |
| Connection timeout | Wrong bridge URL | `curl http://<host-ip>:9500/ping`; fix with `utmt_BRIDGE_URL` |
| MCP can't reach Windows from WSL | Gateway IP changed | Auto-detection handles it; verify with `ip route show default` |
| Changes to `.csx` not taking effect | Bridge not restarted | Re-run the script in utmt |
| `utmt_code_set` compile error | Invalid GML | Fix GML syntax; utmt compiler reports diagnostics |

## 🤝 Contributing

PRs welcome! Keep the bridge script dependency-free (no NuGet packages beyond utmt's built-ins) and the MCP server stdlib-only (no pip installs).

## 📄 License

MIT — see [LICENSE](LICENSE).
