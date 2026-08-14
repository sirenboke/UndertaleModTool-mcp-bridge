#!/usr/bin/env python3
"""
UndertaleModTool MCP Server
通过HTTP桥接与UndertaleModTool中的.csx脚本通信
使用stdio传输，注册为Hermes的MCP工具
"""

import os
import sys
import json
import socket
import urllib.request
import urllib.error


def detect_bridge_url():
    """自动探测 Windows 侧桥接地址（默认网关 + 9500 端口）"""
    env_url = os.environ.get("UMT_BRIDGE_URL")
    if env_url:
        return env_url
    # 从 /proc/net/route 读取默认网关（WSL 下即 Windows 主机 IP）
    try:
        with open("/proc/net/route") as f:
            next(f)
            for line in f:
                parts = line.split()
                if len(parts) >= 3 and parts[1] == "00000000" and parts[2] != "00000000":
                    gw = parts[2]
                    ip = ".".join(str(int(gw[i:i+2], 16)) for i in (6, 4, 2, 0))
                    return f"http://{ip}:9500"
    except Exception:
        pass
    return "http://192.168.160.1:9500"


BRIDGE_URL = detect_bridge_url()

def call(endpoint, data=None):
    """调用桥接API"""
    url = f"{BRIDGE_URL}/{endpoint}"
    body = json.dumps(data).encode() if data else None
    try:
        req = urllib.request.Request(url, data=body, method="POST" if body else "GET")
        req.add_header("Content-Type", "application/json")
        with urllib.request.urlopen(req, timeout=10) as resp:
            return json.loads(resp.read().decode())
    except urllib.error.URLError as e:
        return {"error": f"Bridge not running. Start UndertaleModTool and run mcp_bridge.csx. ({e.reason})"}
    except Exception as e:
        return {"error": str(e)}

def handle_request(method, params):
    """处理MCP请求"""
    
    if method == "tools/list":
        return {
            "tools": [
                {
                    "name": "umt_info",
                    "description": "Get UndertaleModTool game info: name, counts of code/sprites/objects/rooms/sounds",
                    "inputSchema": {"type": "object", "properties": {}}
                },
                {
                    "name": "umt_code_list",
                    "description": "List GML code entries. Optionally filter by name.",
                    "inputSchema": {
                        "type": "object",
                        "properties": {
                            "filter": {"type": "string", "description": "Optional name filter (case-insensitive)"},
                            "limit": {"type": "integer", "description": "Max results (default 100)"}
                        }
                    }
                },
                {
                    "name": "umt_code_get",
                    "description": "Decompile and get GML source code for a named code entry",
                    "inputSchema": {
                        "type": "object",
                        "properties": {
                            "name": {"type": "string", "description": "Code entry name (exact match)"}
                        },
                        "required": ["name"]
                    }
                },
                {
                    "name": "umt_code_set",
                    "description": "Compile and replace GML code for a named code entry",
                    "inputSchema": {
                        "type": "object",
                        "properties": {
                            "name": {"type": "string", "description": "Code entry name"},
                            "gml": {"type": "string", "description": "New GML source code"}
                        },
                        "required": ["name", "gml"]
                    }
                },
                {
                    "name": "umt_search",
                    "description": "Search all resources (code, sprites, objects, rooms) by name",
                    "inputSchema": {
                        "type": "object",
                        "properties": {
                            "query": {"type": "string", "description": "Search term"}
                        },
                        "required": ["query"]
                    }
                },
                {
                    "name": "umt_sprites",
                    "description": "List all sprites with dimensions and frame counts",
                    "inputSchema": {"type": "object", "properties": {}}
                },
                {
                    "name": "umt_objects",
                    "description": "List all game objects with properties",
                    "inputSchema": {"type": "object", "properties": {}}
                },
                {
                    "name": "umt_rooms",
                    "description": "List all rooms with sizes and object counts",
                    "inputSchema": {"type": "object", "properties": {}}
                },
                {
                    "name": "umt_save",
                    "description": "Save the current data.win file",
                    "inputSchema": {"type": "object", "properties": {}}
                },
                {
                    "name": "umt_run",
                    "description": "Execute arbitrary C# script in UndertaleModTool context. Has access to Data, EnsureDataLoaded(). Returns script result as string. Use code= for direct code or b64= for base64-encoded code (avoids JSON escaping).",
                    "inputSchema": {
                        "type": "object",
                        "properties": {
                            "code": {"type": "string", "description": "C# source code to execute"},
                            "b64": {"type": "string", "description": "Base64-encoded C# code (alternative to code=)"}
                        }
                    }
                },
            ]
        }
    
    elif method == "tools/call":
        tool_name = params.get("name", "")
        args = params.get("arguments", {})
        
        route_map = {
            "umt_info":        ("info", None),
            "umt_code_list":   ("code", args if args else None),
            "umt_code_get":    ("code/get", {"name": args.get("name", "")}),
            "umt_code_set":    ("code/set", {"name": args.get("name", ""), "gml": args.get("gml", "")}),
            "umt_search":      ("search", {"query": args.get("query", "")}),
            "umt_sprites":     ("sprites", None),
            "umt_objects":     ("objects", None),
            "umt_rooms":       ("rooms", None),
            "umt_save":        ("save", None),
            "umt_run":         ("run", {"code": args.get("code", ""), "b64": args.get("b64", "")}),
        }
        
        if tool_name in route_map:
            endpoint, data = route_map[tool_name]
            result = call(endpoint, data)
            return {
                "content": [{"type": "text", "text": json.dumps(result, ensure_ascii=False, indent=2)}]
            }
        else:
            return {"content": [{"type": "text", "text": f"Unknown tool: {tool_name}"}], "isError": True}
    
    elif method == "initialize":
        return {
            "protocolVersion": "2024-11-05",
            "serverInfo": {"name": "undertale-mod-tool", "version": "2.0.0"},
            "capabilities": {"tools": {}}
        }
    
    else:
        return {}

def main():
    # Read JSON-RPC from stdin
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            request = json.loads(line)
            req_id = request.get("id")
            method = request.get("method", "")
            params = request.get("params", {})
            
            result = handle_request(method, params)
            
            response = {
                "jsonrpc": "2.0",
                "id": req_id,
                "result": result
            }
            sys.stdout.write(json.dumps(response) + "\n")
            sys.stdout.flush()
            
        except Exception as e:
            error_response = {
                "jsonrpc": "2.0",
                "id": request.get("id") if 'request' in dir() else None,
                "error": {"code": -32603, "message": str(e)}
            }
            sys.stdout.write(json.dumps(error_response) + "\n")
            sys.stdout.flush()

if __name__ == "__main__":
    main()
