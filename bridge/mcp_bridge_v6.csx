/// UndertaleModTool MCP Bridge v6
/// TcpListener HTTP API for Hermes MCP - runs on background thread
/// v6: same as v5 (no csx changes needed). Config moved to MCP server side (UMT_BRIDGE_URL env var).

using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using UndertaleModLib;
using UndertaleModLib.Models;
using Underanalyzer.Decompiler;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

const int PORT = 9500;

EnsureDataLoaded();

var listener = new TcpListener(IPAddress.Any, PORT);
listener.Start();
ScriptMessage($"MCP Bridge v6 on port {PORT}\nUI stays responsive. /run enabled.");

Task.Run(async () =>
{
    while (true)
    {
        try
        {
            var client = await listener.AcceptTcpClientAsync();
            _ = Task.Run(() => HandleClient(client));
        }
        catch { }
    }
});

// ====== HTTP Handler ======

async Task HandleClient(TcpClient client)
{
    try
    {
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        
        string line = await reader.ReadLineAsync();
        if (string.IsNullOrEmpty(line)) return;
        var parts = line.Split(' ');
        if (parts.Length < 2) return;
        string path = parts[1].Trim('/');
        
        int contentLength = 0;
        while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                int.TryParse(line.Substring(15).Trim(), out contentLength);
        
        string body = "";
        if (contentLength > 0)
        {
            char[] buf = new char[contentLength];
            int total = 0;
            while (total < contentLength)
                total += await reader.ReadAsync(buf, total, contentLength - total);
            body = new string(buf);
        }
        
        string result;
        if (path == "run")
            result = await RunScript(body);
        else
            result = Route(path, body);
        
        byte[] resp = Encoding.UTF8.GetBytes(result);
        string header = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {resp.Length}\r\nConnection: close\r\n\r\n";
        byte[] headerBytes = Encoding.ASCII.GetBytes(header);
        await stream.WriteAsync(headerBytes, 0, headerBytes.Length);
        await stream.WriteAsync(resp, 0, resp.Length);
        await stream.FlushAsync();
    }
    catch { }
    finally { try { client.Close(); } catch { } }
}

// ====== Router ======

string Route(string path, string body)
{
    try
    {
        switch (path)
        {
            case "ping": return $"{{\"ok\":true,\"game\":\"{Esc(Data?.GeneralInfo?.Name?.Content ?? "?")}\"}}";
            case "info": return Info();
            case "code": return ListCode(body);
            case "code/get": return GetCode(body);
            case "code/set": return SetCode(body);
            case "sprites": return Sprites();
            case "objects": return Objects();
            case "rooms": return Rooms();
            case "search": return Search(body);
            case "save": return Save();
            default: return $"{{\"error\":\"unknown:{Esc(path)}\"}}";
        }
    }
    catch (Exception ex) { return $"{{\"error\":\"{Esc(ex.Message)}\"}}"; }
}

// ====== Script Runner (v5) ======

async Task<string> RunScript(string body)
{
    string code = ParseJsonStr(body, "code");
    if (string.IsNullOrEmpty(code))
    {
        string b64 = ParseJsonStr(body, "b64");
        if (!string.IsNullOrEmpty(b64))
        {
            try { code = Encoding.UTF8.GetString(Convert.FromBase64String(b64)); }
            catch (Exception ex) { return $"{{\"error\":\"bad base64: {Esc(ex.Message)}\"}}"; }
        }
    }
    if (string.IsNullOrEmpty(code))
        return "{\"error\":\"no code or b64 field\"}";

    try
    {
        var result = await CSharpScript.EvaluateAsync(code, ScriptOptions.Default
            .WithReferences(AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location)))
            .WithImports(
                "System", "System.Linq", "System.Text", "System.Collections.Generic",
                "UndertaleModLib", "UndertaleModLib.Models", "Underanalyzer.Decompiler"
            ), globals: Data);
        string output = result?.ToString() ?? "null";
        return $"{{\"ok\":true,\"result\":\"{Esc(output)}\"}}";
    }
    catch (CompilationErrorException ex)
    {
        return $"{{\"error\":\"compile: {Esc(string.Join("; ", ex.Diagnostics.Take(5).Select(d => d.ToString())))}\"}}";
    }
    catch (Exception ex)
    {
        return $"{{\"error\":\"runtime: {Esc(ex.Message)}\"}}";
    }
}

// ====== Helpers ======

string Esc(string s) => (s ?? "").Replace("\\","\\\\").Replace("\"","\\\"").Replace("\n","\\n").Replace("\r","\\r");

string ParseJsonStr(string json, string key)
{
    if (string.IsNullOrEmpty(json)) return "";
    string search = $"\"{key}\":";
    int idx = json.IndexOf(search, StringComparison.Ordinal);
    if (idx < 0) return "";
    idx += search.Length;
    while (idx < json.Length && (json[idx] == ' ' || json[idx] == '\t')) idx++;
    if (idx >= json.Length || json[idx] != '"') return "";
    idx++;
    int end = idx;
    while (end < json.Length)
    {
        end = json.IndexOf('"', end);
        if (end < 0) return "";
        int bs = 0, p = end - 1;
        while (p >= 0 && json[p] == '\\') { bs++; p--; }
        if (bs % 2 == 0) break;
        end++;
    }
    return json.Substring(idx, end - idx).Replace("\\\\","\\").Replace("\\\"","\"").Replace("\\n","\n").Replace("\\r","\r").Replace("\\t","\t");
}

// ====== API Methods ======

string Info()
{
    return "{" +
        $"\"name\":\"{Esc(Data?.GeneralInfo?.Name?.Content ?? "")}\"," +
        $"\"code\":{Data?.Code?.Count ?? 0}," +
        $"\"sprites\":{Data?.Sprites?.Count ?? 0}," +
        $"\"objects\":{Data?.GameObjects?.Count ?? 0}," +
        $"\"rooms\":{Data?.Rooms?.Count ?? 0}," +
        $"\"sounds\":{Data?.Sounds?.Count ?? 0}" +
        "}";
}

string ListCode(string body)
{
    int limit = 100;
    string filter = ParseJsonStr(body, "filter");
    string limStr = ParseJsonStr(body, "limit");
    if (!string.IsNullOrEmpty(limStr)) int.TryParse(limStr, out limit);
    
    var codes = Data.Code.Where(c => c.ParentEntry == null);
    if (!string.IsNullOrEmpty(filter))
        codes = codes.Where(c => (c.Name?.Content ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
    
    var sb = new StringBuilder();
    sb.Append($"{{\"total\":{Data.Code.Count(c => c.ParentEntry == null)},\"items\":[");
    bool first = true;
    foreach (var c in codes.Take(limit))
    {
        if (!first) sb.Append(",");
        first = false;
        sb.Append($"{{\"name\":\"{Esc(c.Name?.Content ?? "")}\",\"len\":{c.Length}}}");
    }
    sb.Append("]}");
    return sb.ToString();
}

string GetCode(string body)
{
    string name = ParseJsonStr(body, "name");
    if (string.IsNullOrEmpty(name)) return "{\"error\":\"missing name\"}";
    var code = Data.Code.FirstOrDefault(c => c.ParentEntry == null && (c.Name?.Content ?? "") == name);
    if (code == null) return $"{{\"error\":\"not found: {Esc(name)}\"}}";
    string gml = GetDecompiledText(code);
    return $"{{\"name\":\"{Esc(name)}\",\"len\":{code.Length},\"gml\":\"{Esc(gml)}\"}}";
}

string SetCode(string body)
{
    string name = ParseJsonStr(body, "name");
    string gml = ParseJsonStr(body, "gml");
    if (string.IsNullOrEmpty(gml)) return "{\"error\":\"no gml\"}";
    var code = Data.Code.FirstOrDefault(c => c.ParentEntry == null && (c.Name?.Content ?? "") == name);
    if (code == null) return "{\"error\":\"not found\"}";
    try
    {
        var group = new UndertaleModLib.Compiler.CodeImportGroup(Data);
        group.QueueReplace(code, gml);
        group.Import();
        return "{\"ok\":true}";
    }
    catch (Exception ex) { return $"{{\"error\":\"compile: {Esc(ex.Message)}\"}}"; }
}

string Sprites()
{
    var sb = new StringBuilder();
    sb.Append($"{{\"total\":{Data.Sprites.Count},\"items\":[");
    bool first = true;
    foreach (var s in Data.Sprites)
    {
        if (!first) sb.Append(",");
        first = false;
        sb.Append($"{{\"name\":\"{Esc(s.Name?.Content ?? "")}\",\"w\":{s.Width},\"h\":{s.Height}}}");
    }
    sb.Append("]}");
    return sb.ToString();
}

string Objects()
{
    var sb = new StringBuilder();
    sb.Append($"{{\"total\":{Data.GameObjects.Count},\"items\":[");
    bool first = true;
    foreach (var o in Data.GameObjects)
    {
        if (!first) sb.Append(",");
        first = false;
        sb.Append($"{{\"name\":\"{Esc(o.Name?.Content ?? "")}\"}}");
    }
    sb.Append("]}");
    return sb.ToString();
}

string Rooms()
{
    var sb = new StringBuilder();
    sb.Append($"{{\"total\":{Data.Rooms.Count},\"items\":[");
    bool first = true;
    foreach (var r in Data.Rooms)
    {
        if (!first) sb.Append(",");
        first = false;
        sb.Append($"{{\"name\":\"{Esc(r.Name?.Content ?? "")}\",\"w\":{r.Width},\"h\":{r.Height}}}");
    }
    sb.Append("]}");
    return sb.ToString();
}

string Search(string body)
{
    string query = ParseJsonStr(body, "query");
    if (string.IsNullOrEmpty(query)) return "{\"error\":\"no query\"}";
    query = query.ToLowerInvariant();
    var sb = new StringBuilder();
    sb.Append($"{{\"query\":\"{Esc(query)}\",\"items\":[");
    bool first = true;
    void Add(string type, string name) {
        if (string.IsNullOrEmpty(name)) return;
        if (!first) sb.Append(",");
        first = false;
        sb.Append($"{{\"type\":\"{type}\",\"name\":\"{Esc(name)}\"}}");
    }
    try { foreach (var c in Data.Code) if (c != null && c.ParentEntry == null && (c.Name?.Content ?? "").ToLowerInvariant().Contains(query)) Add("code", c.Name.Content); } catch {}
    try { foreach (var s in Data.Sprites) if (s != null && (s.Name?.Content ?? "").ToLowerInvariant().Contains(query)) Add("sprite", s.Name.Content); } catch {}
    try { foreach (var o in Data.GameObjects) if (o != null && (o.Name?.Content ?? "").ToLowerInvariant().Contains(query)) Add("object", o.Name.Content); } catch {}
    try { foreach (var r in Data.Rooms) if (r != null && (r.Name?.Content ?? "").ToLowerInvariant().Contains(query)) Add("room", r.Name.Content); } catch {}
    sb.Append("]}");
    return sb.ToString();
}

string Save()
{
    return "{\"info\":\"Save manually (Ctrl+S).\"}";
}
