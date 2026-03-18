# HttpProxyMcp
[![Build status](https://github.com/sailro/HttpProxyMcp/workflows/CI/badge.svg)](https://github.com/sailro/HttpProxyMcp/actions?query=workflow%3ACI)

An HTTP/HTTPS MITM proxy with full traffic capture, exposed as an **MCP server** so LLMs can inspect, filter, and analyze network traffic through natural language.

Built with .NET 10, C#, SQLite, and the [Model Context Protocol](https://modelcontextprotocol.io/).

## What It Does

```
Browser/App  ──►  HttpProxyMcp (localhost:8080)  ──►  Internet
                        │
                        ▼
                   SQLite DB (all traffic stored)
                        │
                        ▼
                   MCP Server (stdio)
                        │
                        ▼
               LLM (Copilot CLI, VS Code, etc.)
```

- **MITM proxy** intercepts HTTP and HTTPS traffic (dynamic certificate generation)
- **Stores everything** — requests, responses, headers, bodies, timing — in SQLite
- **Auto-configures Windows system proxy** — no manual browser setup needed (like Fiddler)
- **MCP server** exposes 14 tools for LLMs to query and control the proxy
- **HAR 1.2 export** — export captured sessions to standards-compliant HAR files with granular timings and server IPs

## Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### Run the MCP Server

```bash
dotnet run --project src/HttpProxyMcp.McpServer
```

The server communicates over **stdio** using the MCP protocol. It's meant to be launched by an MCP client (Copilot CLI, VS Code, etc.), not run directly.

### Configure Copilot CLI

Add to `~/.copilot/mcp-config.json`:

```json
{
  "mcpServers": {
    "httpproxymcp": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "C:\\path\\to\\src\\HttpProxyMcp.McpServer\\HttpProxyMcp.McpServer.csproj"
      ]
    }
  }
}
```

Restart the CLI, then verify with `/mcp`.

### Configure VS Code / Visual Studio

Add to `.copilot/mcp-config.json` in your repo root:

```json
{
  "mcpServers": {
    "httpproxymcp": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "path/to/src/HttpProxyMcp.McpServer/HttpProxyMcp.McpServer.csproj"
      ]
    }
  }
}
```

## MCP Tools Reference

### Proxy Control

| Tool | Description | Key Parameters |
|------|-------------|----------------|
| `StartProxy` | Start the proxy | `port` (default: 8080), `enableSsl` (default: true), `setSystemProxy` (default: true) |
| `StopProxy` | Stop the proxy and restore system proxy settings | — |
| `GetProxyStatus` | Check if the proxy is running | — |

### Session Management

Sessions are logical groupings of captured traffic (e.g., "testing login flow", "debugging API").

| Tool | Description | Key Parameters |
|------|-------------|----------------|
| `CreateSession` | Create a new capture session | `name` |
| `ListSessions` | List all sessions with entry counts | — |
| `SetActiveSession` | Set which session captures new traffic | `sessionId` |
| `CloseSession` | Close a session (stops capturing to it) | `sessionId` |
| `DeleteSession` | Delete a session and all its traffic | `sessionId` |

### Traffic Analysis

| Tool | Description | Key Parameters |
|------|-------------|----------------|
| `ListTraffic` | List captured entries with filters | `hostname`, `method`, `urlPattern`, `statusCode`, `limit`, `offset` |
| `GetTrafficEntry` | Get full request/response details | `id` |
| `SearchBodies` | Search through request/response bodies | `searchText`, `limit` |
| `GetStatistics` | Traffic stats by method, status, host | `sessionId` (optional) |
| `ClearTraffic` | Delete captured traffic | `sessionId` (optional) |
| `ExportHar` | Export session traffic to HAR 1.2 file | `sessionId`, `filePath` |

## Sample Prompts

### Getting Started

```
Start the proxy and create a session called "browsing-test"
```

```
Check the proxy status
```

### Analyzing Traffic

```
Show me all traffic captured so far
```

```
List all requests to api.github.com
```

```
Show me all POST requests that returned a 4xx status code
```

```
Get the full details of traffic entry 42
```

### Searching Content

```
Search for "Authorization" in request/response bodies
```

```
Find all responses containing "error" in the body
```

### Statistics & Overview

```
Show me traffic statistics broken down by hostname
```

```
How many requests went to each API endpoint?
```

### Session Workflow

```
Create a new session called "login-flow", then start the proxy.
I'll log in to the app — after that, list all the traffic captured in that session.
```

```
Show me all sessions and their traffic counts
```

### Stopping

```
Stop the proxy
```

## How It Works

### HTTPS Interception & Root CA Certificate

The proxy uses [Titanium.Web.Proxy](https://github.com/justcoding121/titanium-web-proxy) for MITM interception:

1. On first start with SSL enabled, a **root CA certificate** is automatically generated
2. For each HTTPS connection, a per-host certificate is dynamically signed by the root CA
3. Request and response bodies are captured before forwarding

#### Trusting the Root CA

On first run, Titanium.Web.Proxy generates a root CA and **prompts you to install it** into the Windows certificate store. Accept the prompt to trust the certificate — no manual commands needed.

If you need to reinstall it later (e.g., after deleting the PFX), simply restart the proxy — a new root CA will be generated and the install prompt will appear again.

You can also provide your own root CA via `ProxyConfiguration`:
- `RootCertificatePath` — path to a custom PFX file
- `RootCertificatePassword` — password for the PFX

The root CA is generated once and persisted for reuse across sessions.

> **Security note:** The root CA allows the proxy to decrypt all HTTPS traffic. Only install it on development machines. Remove it from the trust store when you're done.

### System Proxy (Windows)

When `setSystemProxy` is `true` (the default), the proxy automatically configures itself as the Windows system proxy — **no manual browser configuration needed** (like Fiddler).

#### How it works

- **On start**: Snapshots current proxy settings, then sets `HKCU\...\Internet Settings\ProxyEnable=1` and `ProxyServer=localhost:{port}`
- **On stop**: Restores the original proxy settings from the snapshot
- Calls `InternetSetOption` to notify WinInet immediately — browsers pick up the change without restart

#### Crash safety

The proxy registers multiple cleanup handlers to prevent leaving the system proxy dangling if the process terminates unexpectedly:

| Handler | Covers |
|---------|--------|
| `AppDomain.ProcessExit` | Normal exit, `Environment.Exit()`, SIGTERM |
| `Console.CancelKeyPress` | Ctrl+C / SIGINT |
| `IDisposable.Dispose()` | DI container shutdown |

All three are idempotent — the proxy settings are restored exactly once regardless of how many handlers fire.

> **Limitation:** A forced kill (`taskkill /F` / SIGKILL) cannot be intercepted by any handler — this is an OS limitation. If this happens, restore manually:
> ```powershell
> Set-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings" -Name ProxyEnable -Value 0
> ```

#### Browser compatibility

| Browser | System proxy | Notes |
|---------|-------------|-------|
| Chrome | ✅ Automatic | Follows Windows Internet Settings |
| Edge | ✅ Automatic | Follows Windows Internet Settings |
| IE | ✅ Automatic | Follows Windows Internet Settings |
| Firefox | ❌ Manual | Uses its own proxy settings (Options → Network → Proxy) |
| .NET HttpClient | ✅ Automatic | Respects system proxy by default |
| curl | ❌ Manual | Use `curl --proxy http://localhost:8080` |

#### Checking proxy status

```powershell
# Check if the system proxy is currently active:
Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings" | Select-Object ProxyEnable, ProxyServer
```

### Storage

All traffic is persisted to a SQLite database (`traffic.db`) using Dapper:

- WAL mode for concurrent read/write performance
- Indexed on hostname, URL, status code, method, and timestamp
- Bodies stored as BLOBs (up to 10MB per body by default)
- Foreign key cascading: deleting a session deletes its traffic

### HAR 1.2 Export

The `ExportHar` tool exports captured traffic to a standards-compliant HAR 1.2 JSON file:

```
Export traffic session to HAR file for analysis in browser DevTools, Charles, Fiddler, or other tools
```

**Parameters:**
- `sessionId` — Session GUID to export
- `filePath` — Output path for the .har file (UTF-8 encoded, no BOM)

**Captured Data:**
Each HAR entry includes:
- Request/response headers, cookies, query strings, HTTP methods
- Request/response bodies (base64 for binary, UTF-8 text for text types)
- HTTP version (HTTP/1.1, HTTP/2.0, etc.)
- Server IP address (resolved IP of upstream server)
- Granular timings: send, wait, receive (milliseconds)
- Status codes, content types, redirect URLs
- Entry timestamps (ISO 8601 format)

**Database Schema:**
The proxy automatically captures 6 new fields for HAR export on startup:
- `request_http_version` (HTTP version of request)
- `response_http_version` (HTTP version of response)
- `server_ip_address` (resolved server IP)
- `timing_send_ms` (request send duration)
- `timing_wait_ms` (time waiting for response)
- `timing_receive_ms` (response receive duration)

These fields are added transparently to existing databases via `ALTER TABLE` — old captured traffic remains intact with NULL values for these fields.

### Architecture

```
src/
├── HttpProxyMcp.Core/       Models & interfaces
├── HttpProxyMcp.Proxy/      MITM proxy engine + certificate manager + system proxy
├── HttpProxyMcp.Storage/    SQLite persistence (Dapper)
└── HttpProxyMcp.McpServer/  MCP stdio server, 14 tools, hosted service
tests/
└── HttpProxyMcp.Tests/      tests (xUnit + NSubstitute + FluentAssertions)
```

## Configuration

`ProxyConfiguration` supports:

| Property | Default | Description |
|----------|---------|-------------|
| `Port` | `8080` | Proxy listen port |
| `EnableSsl` | `true` | Enable HTTPS MITM interception |
| `SetSystemProxy` | `true` | Auto-configure Windows system proxy |
| `RootCertificatePath` | `null` | Path to a custom root CA PFX file |
| `RootCertificatePassword` | `null` | Password for the root CA PFX |
| `MaxBodyCaptureBytes` | `10MB` | Max body size to capture per request/response |
| `ExcludedHostnames` | `[]` | Hostnames to pass through without MITM |

## Development

```bash
# Build
dotnet build

# Test
dotnet test

# Run
dotnet run --project src/HttpProxyMcp.McpServer
```

## License

MIT
