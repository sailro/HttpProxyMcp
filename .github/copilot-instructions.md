# Copilot Instructions — HttpProxyMcp

An HTTP/HTTPS MITM proxy with full traffic capture, exposed as an MCP server so LLMs can inspect, filter, and analyze network traffic. Built with .NET 10, C#, SQLite (Dapper), and the Model Context Protocol.

## Build, Test, Run

```bash
dotnet build
dotnet test
dotnet test --filter "FullyQualifiedName~TrafficStoreTests"       # tests in one class
dotnet test --filter "FullyQualifiedName~StartProxy_WhenNotRunning" # single test
dotnet run --project src/HttpProxyMcp.McpServer                    # run MCP server (stdio)
```

CI runs `dotnet build --configuration Release && dotnet test --configuration Release` on both Ubuntu and Windows.

## Architecture

```
MCP Client (Copilot CLI, VS Code, etc.)
    │ stdio / MCP Protocol
    ▼
McpServer  ── Worker Service + 13 MCP tools (auto-discovered via [McpServerToolType])
    │
    ├── ProxyHostedService (BackgroundService)
    │     └── Wires ProxyEngine.TrafficCaptured → ITrafficStore.SaveTrafficEntryAsync
    │
    ├── IProxyEngine (Proxy layer)
    │     ├── ProxyEngine (Titanium.Web.Proxy MITM)
    │     ├── RootCertificateManager (X509 root CA lifecycle)
    │     └── SystemProxyManager (Windows registry + WinInet P/Invoke)
    │
    ├── ITrafficStore / ISessionManager (Storage layer)
    │     ├── SqliteTrafficStore (Dapper, raw SQL, snake_case mapping)
    │     └── SqliteSessionManager (in-memory Lock for active session)
    │
    └── Core (interfaces + models, no dependencies)
```

**Key flow:** Proxy captures traffic → fires `TrafficCaptured` event → `ProxyHostedService` assigns to active session → persists to SQLite via Dapper.

## Project Structure

| Project | Purpose |
|---------|---------|
| `HttpProxyMcp.Core` | Domain models (`TrafficEntry`, `ProxySession`, `CapturedRequest/Response`, `ProxyConfiguration`, `TrafficFilter`, `TrafficStatistics`) and interfaces (`IProxyEngine`, `ISessionManager`, `ITrafficStore`) |
| `HttpProxyMcp.Proxy` | MITM proxy engine using Titanium.Web.Proxy, root CA cert management, Windows system proxy auto-config. Has `AllowUnsafeBlocks=true` for P/Invoke. |
| `HttpProxyMcp.Storage` | SQLite persistence with Dapper. Uses `MatchNamesWithUnderscores` for snake_case→PascalCase mapping. Internal DTOs (`TrafficRow`, `SessionRow`) flatten nested objects for DB mapping. Headers stored as JSON in TEXT columns. |
| `HttpProxyMcp.McpServer` | Entry point. Worker service hosting MCP server over stdio. Tools in `Tools/` folder are static async methods with DI via parameters, auto-discovered by `WithToolsFromAssembly()`. |
| `HttpProxyMcp.Tests` | xUnit + NSubstitute + FluentAssertions. Organized by layer: `Storage/`, `Proxy/`, `McpTools/`, `Helpers/`. |

## Code Conventions

- **Target framework:** .NET 10 (`net10.0`) across all projects
- **Nullable reference types:** enabled globally
- **Implicit usings:** enabled globally
- **Namespaces:** file-scoped (`namespace HttpProxyMcp.Core.Models;`)
- **Private fields:** `_camelCase` with underscore prefix
- **Async methods:** always accept `CancellationToken cancellationToken = default`
- **DI registration:** extension methods per layer (`AddProxyServices()`, `AddStorageServices()`)
- **MCP tools:** static async methods decorated with `[McpServerToolType]` / `[McpServerTool]`, parameters with `[Description]`, return `Task<string>`

## Testing Conventions

- **Mocking:** NSubstitute (`Substitute.For<T>()`, `.Returns()`, `.Received()`, `Arg.Any<T>()`, `Arg.Is<T>()`)
- **Assertions:** FluentAssertions (`.Should().Be()`, `.Should().Contain()`) for most tests; plain `Assert.*` in `ModelTests.cs`
- **Test naming:** `MethodName_Condition_ExpectedResult`
- **Test data:** `TrafficEntryBuilder` — fluent builder with convenience factories (`TrafficEntryBuilder.Get(url)`, `.Post(url, body)`) and chainable `.WithStatusCode()`, `.WithRequestHeaders()`, etc.
- **Constructor setup:** mocks initialized in test class constructor, not per-test

## Storage Details

- SQLite with WAL mode, Dapper for all queries
- Tables: `sessions` (id TEXT PK, name, created_at, closed_at) and `traffic_entries` (id INTEGER PK AUTOINCREMENT, foreign key to sessions with CASCADE delete)
- Indexed on: session_id, hostname, url, status_code, method, started_at
- Bodies stored as BLOBs (max 10MB default via `ProxyConfiguration.MaxBodyCaptureBytes`)
- Body search uses `CAST(request_body AS TEXT) LIKE`
- `ClearTrafficAsync` includes `VACUUM` to reclaim disk space

## Windows System Proxy

`SystemProxyManager` is Windows-only (`RuntimeInformation.IsOSPlatform(OSPlatform.Windows)`). It snapshots current proxy settings before enabling, modifies `HKCU\...\Internet Settings`, and calls `InternetSetOption` via P/Invoke to notify WinInet. Cleanup hooks on `ProcessExit`, `CancelKeyPress`, and `IDisposable.Dispose()` ensure proxy settings are restored.
