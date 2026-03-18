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

### Database Schema Migration Strategy for HAR 1.2 Export (2026-03-18)

**Context from Morpheus:** DB migration impact analysis completed. Identified backward compatibility risk: adding 6 new columns to `traffic_entries` table would crash old user databases on `SELECT *` queries (Dapper mapping failure).

**Decision:** Implement Option A — `ALTER TABLE ADD COLUMN` with idempotency check via `PRAGMA table_info`.

**My (Mouse's) Implementation Responsibilities:**

1. **SqliteTrafficStore.InitializeAsync():**
   - After CREATE TABLE IF NOT EXISTS block, add migration block:
   ```csharp
   var existingColumns = await conn.QueryAsync<dynamic>("PRAGMA table_info(traffic_entries)");
   var columnNames = existingColumns.Select(c => (string)c.name).ToHashSet();
   
   if (!columnNames.Contains("request_http_version"))
       await conn.ExecuteAsync("ALTER TABLE traffic_entries ADD COLUMN request_http_version TEXT");
   if (!columnNames.Contains("response_http_version"))
       await conn.ExecuteAsync("ALTER TABLE traffic_entries ADD COLUMN response_http_version TEXT");
   if (!columnNames.Contains("server_ip_address"))
       await conn.ExecuteAsync("ALTER TABLE traffic_entries ADD COLUMN server_ip_address TEXT");
   if (!columnNames.Contains("timing_blocked_ms"))
       await conn.ExecuteAsync("ALTER TABLE traffic_entries ADD COLUMN timing_blocked_ms REAL");
   if (!columnNames.Contains("timing_send_ms"))
       await conn.ExecuteAsync("ALTER TABLE traffic_entries ADD COLUMN timing_send_ms REAL");
   if (!columnNames.Contains("timing_wait_ms"))
       await conn.ExecuteAsync("ALTER TABLE traffic_entries ADD COLUMN timing_wait_ms REAL");
   if (!columnNames.Contains("timing_receive_ms"))
       await conn.ExecuteAsync("ALTER TABLE traffic_entries ADD COLUMN timing_receive_ms REAL");
   ```

2. **TrafficRow.cs (Dapper DTO):**
   - Add 7 new nullable properties (matched via MatchNamesWithUnderscores):
   ```csharp
   public string? RequestHttpVersion { get; set; }      // maps to request_http_version
   public string? ResponseHttpVersion { get; set; }     // maps to response_http_version
   public string? ServerIpAddress { get; set; }         // maps to server_ip_address
   public double? TimingBlockedMs { get; set; }         // maps to timing_blocked_ms
   public double? TimingSendMs { get; set; }            // maps to timing_send_ms
   public double? TimingWaitMs { get; set; }            // maps to timing_wait_ms
   public double? TimingReceiveMs { get; set; }         // maps to timing_receive_ms
   ```

3. **SaveTrafficEntryAsync:**
   - Update INSERT to populate new columns when provided by Tank's ProxyEngine changes
   - Will receive HttpVersion, ServerIp, and timing data from CaptureRequest/CaptureResponse helpers

4. **HAR Export Tool (Phase 2, after Tank completes):**
   - Implement `ExportAsHar(string session_id)` MCP tool
   - Derivable fields computed at export time (cookies, queryString NVPs, header sizes)
   - Format timestamps as ISO 8601, encode bodies as base64

**Dependencies:**
- Tank must populate HttpVersion, ServerIpAddress, and timing fields in ProxyEngine capture handlers
- Phase 1 (DB schema + Dapper mapping) can proceed independently
- Phase 2 (HAR export tool) waits for Tank + Phase 1 completion

### HAR 1.2 Phase 1 — Storage Fields Implemented (2026-07-18)

**Context:** Implemented the 6 new HAR 1.2 capture fields across Core models, Storage DTOs, and SQLite schema.

**Changes Made:**
1. **Core Models:** Added `HttpVersion` (nullable string) to both `CapturedRequest` and `CapturedResponse`. Added `ServerIpAddress`, `TimingSendMs`, `TimingWaitMs`, `TimingReceiveMs` to `TrafficEntry`.
2. **TrafficRow DTO:** Added 6 matching flat properties (`RequestHttpVersion`, `ResponseHttpVersion`, `ServerIpAddress`, `TimingSendMs`, `TimingWaitMs`, `TimingReceiveMs`).
3. **SQL Schema:** Extended CREATE TABLE with 6 new columns (`request_http_version TEXT`, `response_http_version TEXT`, `server_ip_address TEXT`, `timing_send_ms REAL`, `timing_wait_ms REAL`, `timing_receive_ms REAL`).
4. **DB Migration:** Added idempotent `PRAGMA table_info` + `ALTER TABLE ADD COLUMN` migration in `InitializeAsync()` — safe for both new and existing databases. Old rows get NULL.
5. **INSERT/SELECT/Mapping:** Updated `SaveTrafficEntryAsync` INSERT, both `QueryTrafficAsync` and `SearchBodiesAsync` SELECT lists, and `MapToEntry` bidirectional mapping.

**Observation:** Tank had already updated `ProxyEngine.cs` with `FormatHttpVersion()` and `ExtractTimings()` helpers plus the capture code — the proxy layer was ready for the new model fields.

**Test Results:** All 133 tests pass (1 skip). Existing `MigrationTests` (already present) cover the migration pattern.

**Status:** Phase 1 complete. Ready for Phase 2 (HAR export MCP tool).

**Testing Plan:**
- Unit: Old 24-column DB + InitializeAsync → columns added
- Unit: InitializeAsync idempotency

### HAR 1.2 Phase 2 — Export MCP Tool Implemented (2026-07-18)

**Context:** Phase 1 (storage fields) was complete. Built the `export_har` MCP tool as Phase 2.

**Files Created:**
1. `src/HttpProxyMcp.McpServer/Tools/ExportTools.cs` — New MCP tool class with `ExportHar` method
2. `src/HttpProxyMcp.McpServer/HarConverter.cs` — Static utility for HAR 1.2 JSON conversion

**Tool Design:**
- Parameters: `sessionId` (string, GUID) + `filePath` (string, output path)
- Loads all entries for a session (counts first, then fetches with bodies via GetTrafficEntryAsync per entry)
- Converts to HAR 1.2 JSON via HarConverter, writes UTF-8 to file
- Returns summary string: "Exported N entries to {path}"

**HarConverter Implementation:**
- Full HAR 1.2 spec compliance: log.version, creator, entries array
- Request: method, url, httpVersion, cookies (parsed from Cookie header), headers (flattened), queryString (parsed via HttpUtility.ParseQueryString), postData, headerSize/bodySize
- Response: status, statusText, httpVersion, cookies (parsed from Set-Cookie with attributes), headers, content (with text/base64 encoding), redirectURL (from Location header)
- Timings: send/wait/receive from TrafficEntry fields, fallback to duration
- Text vs binary detection: comprehensive MIME type matching (text/*, json, xml, javascript, svg+xml, form-urlencoded, etc.)
- Bodies encoded as base64 for binary content types; UTF-8 text for text types
- Entries sorted by startedDateTime per HAR spec recommendation
- Handles null responses gracefully (pending requests)

**Architecture Decision:** List query excludes bodies for performance; HAR export loads each entry individually via GetTrafficEntryAsync. N+1 queries but acceptable for export use case (point lookups by PK). Avoids changing ITrafficStore interface.

**Build & Test:** All tests pass. Zero warnings, zero errors.
- Integration: Old DB → upgrade → traffic capture → HAR export

### HAR Export Chrome Compatibility Improvements (2026-03-18)

**Context:** Comparing our HAR output to Chrome's DevTools export revealed three issues: (1) timing waterfall showed send=0/wait=all/receive=0 which renders poorly in Chrome, (2) UTF-8 BOM caused JSON.parse issues, (3) file size ~2.4x Chrome's due to 4-space indentation.

**Changes Made:**
1. **Timing distribution (HarConverter.BuildTimings):** When all three granular timings are populated by the proxy, use them directly. When they're null (legacy data or missing), estimate a realistic distribution from total duration: send ≈ 1% (capped 0.5–5ms), receive proportional to response body size at ~10MB/s throughput (capped at 40% of duration), wait = remainder. All values rounded to 3 decimal places.
2. **BOM fix (ExportTools.ExportHar):** Changed `Encoding.UTF8` to `new UTF8Encoding(false)` — no BOM prefix. Standard JSON tooling expects no BOM.
3. **File size (HarConverter.JsonOptions):** Added `IndentSize = 2` (was 4-space default). Reduces whitespace by ~40% in deeply nested HAR structures.

**Test Results:** 133 pass, 0 fail, 1 skip (existing platform conditional). No regressions.

### HAR Export Documentation Updated (2026-07-18)

**Context:** HAR 1.2 export feature complete (Phase 1 storage + Phase 2 MCP tool). Updated all project documentation to reflect the 14th tool and 6 new capture fields.

**Changes Made:**

1. **README.md:**
   - Updated "What It Does" section: "exposes 14 tools" (was 13)
   - Added HAR 1.2 export to feature list
   - Updated Tools Reference table: Added `ExportHar` row (sessionId, filePath parameters)
   - New "HAR 1.2 Export" section with:
     * Tool description and use cases (DevTools, Charles, Fiddler)
     * Parameters documented: sessionId (GUID), filePath (output path, UTF-8 no BOM)
     * Captured data details: headers, cookies, bodies (base64/text), HTTP version, server IP, granular timings, ISO 8601 timestamps
     * Database schema: 6 new fields auto-migrated via ALTER TABLE with NULL for old traffic
   - Updated Architecture diagram: 14 tools (was 13)

2. **.github/copilot-instructions.md:**
   - Updated architecture diagram: 14 MCP tools (was 13)
   - Updated Project Structure table: McpServer entry now mentions `ExportTools.cs` (HAR tool) and `HarConverter.cs`
   - Storage Details section: Added HAR 1.2 field documentation with auto-migration via ALTER TABLE

3. **CLAUDE.md / AGENTS.md:**
   - No changes needed (already reference copilot-instructions.md)

**Documentation Quality:**
- All user-facing docs (README) use plain English, no SQL or technical jargon exposed
- Copilot instructions document internal architecture (ExportTools, HarConverter files)
- HAR export portrayed as high-value feature for cross-tool compatibility
- Auto-migration explicitly called out so users understand transparent backward compatibility

**Status:** Documentation complete. Project now has comprehensive, accurate coverage of HAR 1.2 export feature.
