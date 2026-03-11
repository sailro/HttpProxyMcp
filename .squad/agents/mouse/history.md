# Mouse — History

## Project Context

- **Project:** httpproxymcp — HTTP/HTTPS MITM proxy with traffic capture and MCP server
- **Stack:** .NET 10, C#, SQLite
- **User:** Sebastien Lebreton
- **My Focus:** SQLite storage for captured traffic, MCP server with tools for listing/filtering/searching/stats/control

## Learnings

### Architecture Foundation (2026-03-11)
- **Framework & Stack:** .NET 10, C#, SQLite with Titanium.Web.Proxy, Microsoft.Data.Sqlite, and official MCP SDK
- **Proxy Architecture:** 5-project structure with Core, Proxy, Storage, McpServer, Tests modules
- **Key Interfaces:** IProxyEngine (MITM, cert generation, forwarding), ITrafficStore (SQLite queries), ISessionManager (session lifecycle)
- **Storage Design:** Dapper chosen over EF Core for write-heavy real-time traffic capture
- **MCP Integration:** Official ModelContextProtocol SDK with [McpServerTool] auto-discovery for tool exposure
- **Team Charter:** Morpheus (architecture), Tank (proxy engine), Mouse (storage + MCP), Switch (testing), Scribe (documentation)

_(Session learnings will be appended here)

### SQLite Storage Implementation (completed)

**Schema (traffic.db):**
- `sessions`: id TEXT PK, name TEXT, created_at TEXT, closed_at TEXT
- `traffic_entries`: 21 columns — flattened TrafficEntry with JSON headers, BLOB bodies, numeric indexes
- Indexes on: session_id, hostname, url, status_code, method, started_at
- WAL journal mode, FK cascade on session delete

**Key Files:**
- `src/HttpProxyMcp.Storage/SqliteTrafficStore.cs` — ITrafficStore (Dapper + Microsoft.Data.Sqlite)
- `src/HttpProxyMcp.Storage/SqliteSessionManager.cs` — ISessionManager; active session in-memory
- `src/HttpProxyMcp.Storage/StorageServiceExtensions.cs` — `AddStorageServices()` DI extension
- `src/HttpProxyMcp.Storage/TrafficRow.cs` / `SessionRow.cs` — flat Dapper DTOs

**Patterns:**
- `DefaultTypeMap.MatchNamesWithUnderscores = true` for snake_case mapping
- Headers: `Dictionary<string,string[]>` ↔ JSON TEXT column
- Bodies: BLOB storage; search via `CAST(body AS TEXT) LIKE @search`
- List queries exclude body BLOBs; detail queries include them
- DateTimeOffset as ISO 8601 TEXT; Guid as TEXT
- All queries parameterized via DynamicParameters
- Connection-per-operation (driver handles pooling)
