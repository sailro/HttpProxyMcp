# Mouse — History

## Project Context

- **Project:** httpproxymcp — HTTP/HTTPS MITM proxy with traffic capture and MCP server
- **Stack:** .NET 10, C#, SQLite
- **User:** Sebastien Lebreton
- **My Focus:** SQLite storage for captured traffic, MCP server with tools for listing/filtering/searching/stats/control

## Learnings

### HAR 1.2 Export — Phase 1 Preparation (2026-03-18)

**Context:** Morpheus completed gap analysis; identified 7 new capture fields needed for high-quality HAR export. Mouse's responsibility: Storage layer changes (DB schema + Dapper mapping) + later implement HAR export tool.

**Phase 1 — Storage Changes (Mouse):**
1. **DB Migration:** Add 7 new columns to `traffic_entries` table:
   - `request_http_version` (TEXT) — e.g., "HTTP/1.1", "HTTP/2.0"
   - `response_http_version` (TEXT) — e.g., "HTTP/1.1", "HTTP/2.0"
   - `server_ip_address` (TEXT) — resolved server IP, e.g., "192.0.2.1"
   - `timing_blocked_ms` (REAL) — milliseconds from Session Created to Connection Ready
   - `timing_send_ms` (REAL) — milliseconds from Connection Ready to Request Sent
   - `timing_wait_ms` (REAL) — milliseconds from Request Sent to Response Received
   - `timing_receive_ms` (REAL) — milliseconds from Response Received to Response Sent

2. **TrafficRow DTO:** Add 7 properties (snake_case names matched via Dapper's MatchNamesWithUnderscores)

3. **SqliteTrafficStore:** Update SaveTrafficEntryAsync to populate all 7 fields

**Phase 2 — HAR Export Tool (Mouse, after Tank + Mouse complete Phase 1):**
- New MCP tool: `ExportAsHar` (session_id) → returns HAR 1.2 JSON
- Derivable fields computed at export time: cookies, queryString NVPs, header sizes
- Format timestamps as ISO 8601, encode bodies as base64

**Dependencies:**
- Tank: Populate HttpVersion + ServerIpAddress + TimingMs fields in ProxyEngine.CaptureRequest/CaptureResponse
- Mouse: DB schema + Dapper mapping (Phase 1a)
- Mouse: HAR export tool (Phase 2, after Tank finishes)

**Status:** Awaiting Tank's capture changes — no blocking issues identified

**Audit Scope:** Verify MCP tools, storage layer, and hosted service alignment after recent changes.

**Checklist Results:**

1. ✅ **MCP Tool [Description] Attributes** — All 13 tools have proper [Description] attributes on both class-level and parameter-level
   - TrafficTools: ListTraffic, GetTrafficEntry, SearchBodies, GetStatistics, ClearTraffic (5 tools)
   - SessionTools: ListSessions, CreateSession, SetActiveSession, CloseSession, DeleteSession (5 tools)
   - ProxyControlTools: StartProxy, StopProxy, GetProxyStatus (3 tools)
   - All parameters are documented

2. ✅ **ProxyHostedService Wiring** — Correctly implements event-driven traffic capture and session lifecycle
   - ExecuteAsync: Initializes storage, wires TrafficCaptured event → OnTrafficCaptured handler
   - OnTrafficCaptured: Assigns entry to active session (auto-creates "default" if none exists), persists via store
   - StopAsync: Unsubscribes from TrafficCaptured, stops proxy engine, closes active session before shutdown
   - Session closure prevents "active" state bleeding across process restarts

3. ✅ **VACUUM Calls After Deletions** — Both delete operations properly reclaim disk space
   - SqliteTrafficStore.ClearTrafficAsync: Calls VACUUM after DELETE FROM traffic_entries (line 280)
   - SqliteSessionManager.DeleteSessionAsync: Calls VACUUM after DELETE FROM sessions with FK cascade (line 133)
   - Both operations enable PRAGMA foreign_keys=ON; to ensure cascading behavior

4. ✅ **StartProxy Tool setSystemProxy Parameter** — Properly exposed with description
   - Method signature: StartProxy(..., bool setSystemProxy = true) with [Description]("Auto-configure Windows system proxy (default: true)")
   - Parameter is passed to ProxyConfiguration and used correctly
   - README.md documents this parameter in the MCP Tools Reference table

5. ✅ **Storage Connection String Documentation** — Located and documented in multiple places
   - Default: "Data Source=traffic.db" (StorageServiceExtensions.cs, line 13)
   - StorageServiceExtensions.AddStorageServices() method accepts connectionString parameter for customization
   - README.md section "### Storage" explains database persists to SQLite and mentions traffic.db
   - Connection string allows flexible location via DI parameter

6. ✅ **Stale Squad Agent Comments** — No stale references found
   - Grep search for "Morpheus|Tank|Mouse|Switch|Scribe" across src/ returned no matches
   - All comments are generic ("Hosted service that initializes...", "MCP tools for...", etc.)
   - Team charter referenced only in history.md (appropriate knowledge artifact, not code)

**Technical Details Verified:**
- ServiceRegistration.cs properly chains AddProxyServices() → AddStorageServices()
- Program.cs calls AddProxyServices() which includes storage initialization
- All tools use [McpServerTool] + [McpServerToolType] for auto-discovery
- TrafficRow/SessionRow mappers use snake_case with Dapper's MatchNamesWithUnderscores
- Headers/bodies serialized correctly for list vs detail queries

**Conclusion:** All components are properly aligned. No changes needed.

### Team Cross-Updates (2026-03-11)
- **Morpheus's code audit:** Project in excellent state; documentation comprehensive and current; no architectural drift detected.
- **Switch's test audit:** Identified 5 coverage gaps (ProxyHostedService, SystemProxyManager, RootCertificateManager, real ProxyEngine, VACUUM); cleaned up 3 stale test comments. Priority: ProxyHostedService tests first.

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
