# Squad Decisions

## Active Decisions

### 1. Architecture & Solution Structure (Morpheus — 2026-03-11)

**Status:** ✅ Approved & Implemented

**Framework & Technology Stack:**
- .NET 10 with `net10.0` TFM
- Titanium.Web.Proxy 3.2.0 for HTTP/HTTPS MITM with dynamic TLS cert generation
- Microsoft.Data.Sqlite 10.0.4 + Dapper 2.1.72 for write-heavy traffic capture
- ModelContextProtocol 1.1.0 SDK with `WithToolsFromAssembly()` auto-discovery
- 5 projects: Core (models/interfaces), Proxy, Storage, McpServer, Tests

**Project Responsibility Map:**
- **Core:** Domain models (TrafficEntry, CapturedRequest/Response, ProxySession) and interfaces (IProxyEngine, ITrafficStore, ISessionManager)
- **Proxy:** Titanium.Web.Proxy wrapper with HTTP/HTTPS interception, dynamic cert gen, TrafficCaptured event emission
- **Storage:** SQLite schema, Dapper queries, ITrafficStore and ISessionManager implementations
- **McpServer:** DI composition, MCP server host, tool registration via reflection
- **Tests:** xUnit, NSubstitute mocks, 101 tests across 9 files

**Data Flow:** Browser → Proxy (MITM) → TrafficCaptured event → SqliteTrafficStore → MCP tools → AI assistant

**Rationale:**
- Titanium.Web.Proxy is industry-standard for MITM interception; handles CONNECT tunneling and cert chain automatically
- Dapper chosen over EF Core for low-overhead, high-throughput data access suitable for real-time traffic capture
- Event-driven architecture keeps proxy decoupled from storage; easy to add future subscribers (e.g., WebSocket feeds)

### 2. Proxy Engine Design (Tank — 2026-03-11)

**Status:** ✅ Approved & Implemented

**Key Design Choices:**
1. **UserData-based request/response pairing** — Titanium fires BeforeRequest/BeforeResponse separately; request data carried via UserData to response handler for assembly of full TrafficEntry
2. **Body size cap (configurable, default 10 MB)** — Prevents memory exhaustion on large downloads; bodies exceeding cap stored as null, but headers/metadata preserved
3. **Synchronous event firing** — TrafficCaptured fires on Titanium's response handler thread; storage subscriber must be fast
4. **Certificate management delegation** — Titanium's built-in CertificateManager handles per-host certs; RootCertificateManager layers on top for PFX load/generate/persist
5. **Accept all upstream certificates** — Dev tool mode; no TLS validation on upstream servers

**Consequences:**
- Simple, single-threaded capture flow — easy to reason about
- Body cap prevents memory issues but large payloads aren't fully captured
- Storage layer must handle synchronous callback efficiently (acceptable with Dapper + SQLite)

### 3. Storage Layer Design (Mouse — 2026-03-11)

**Status:** ✅ Approved & Implemented

**Key Design Choices:**
1. **Flat row schema with JSON headers, BLOB bodies** — Single traffic_entries row per TrafficEntry; headers serialized as JSON TEXT, bodies as BLOB
2. **Body search via CAST** — `CAST(request_body AS TEXT) LIKE @search` for text-based filtering (harmless no-op on binary)
3. **List views exclude bodies** — QueryTrafficAsync omits body columns for performance; GetTrafficEntryAsync returns full row
4. **Active session tracked in-memory** — Volatile Guid? with lock protection; resets on process restart (intentional — user picks session on startup)
5. **WAL mode + FK cascade** — Write-ahead logging for concurrent read performance; deleting session cascades to traffic cleanup

**Consequences:**
- Simple, fast, correct for dev-tool use case
- Body search performance degrades on very large databases (acceptable)
- Session state is ephemeral across restarts (by design)
- Single-writer SQLite model acceptable for dev-tool workload

### 4. EventHandler<T> Constraint (Switch — Informational, 2026-03-11)

**Status:** ℹ️ Documented (No change required)

**Issue:** IProxyEngine.TrafficCaptured uses EventHandler<TrafficEntry>, but TrafficEntry is a plain POCO not extending EventArgs.

**Context:** Valid C# since .NET 4.5, but breaks NSubstitute's Raise.EventWith<T>() helper (requires T : EventArgs).

**Workaround:** Tests use `Raise.Event<EventHandler<TrafficEntry>>(sender, args)` directly.

**Recommendation:** Keep TrafficEntry clean as POCO. If it becomes cumbersome, introduce TrafficCapturedEventArgs wrapper, but current design is simpler and works fine.

### 5. Code & Documentation Audit (Morpheus — 2026-03-14)

**Status:** ✅ Approved & Archived

**Scope:** README.md, .github/copilot-instructions.md, CLAUDE.md, AGENTS.md, .gitignore, source code staleness audit

**Findings:**
- README.md: Comprehensive and accurate; all 13 MCP tools documented with correct parameters; architecture sections align with code
- Copilot instructions: Up-to-date; code conventions, storage design, testing patterns, Windows proxy management match actual implementation
- CLAUDE.md / AGENTS.md: Correctly redirect to copilot-instructions.md
- Source code: Clean; no stale comments (Tank/Mouse/Switch/Morpheus squad references, TODO/FIXME/HACK/STUB absent)
- .gitignore: Comprehensive; covers .NET artifacts, Squad runtime state, SQLite databases, Node modules
- Minor note: Custom instruction claims "101 tests across 9 files"; actual is 8 test files (excluding 2 helper files) — no functional impact

**Architecture Verifications:**
- MCP tools auto-discovery: All 13 tools decorated with [McpServerToolType] and [McpServerTool] ✓
- DI patterns: MCP tools use static async methods with parameter injection ✓
- Storage schema: Dapper + SQLite with WAL mode, FK cascading, BLOB bodies, JSON headers ✓
- Event-driven architecture: ProxyEngine.TrafficCaptured → ProxyHostedService → SqliteTrafficStore ✓
- System proxy management: Windows-only, snapshots/restores, idempotent cleanup ✓

**Recommendations (Non-blocking):**
1. Update custom instruction test count if precision needed
2. Document RootCertificateManager's default PFX path (platform-specific, in AppData) in README
3. Add .gitignore comment explaining why *.pfx not excluded (stored outside repo)

**Conclusion:** Project in excellent state. Documentation is comprehensive and current. No architectural drift or stale code detected.

### 6. Test Coverage Gaps Assessment (Switch — 2026-03-11)

**Status:** ℹ️ Documented (No change required)

**Test Suite Overview:** 101 tests passing across 8 test files; 3 stale comments cleaned up

**Coverage Gaps Identified (Non-critical):**
1. **ProxyHostedService** — Auto-close active session on shutdown, event wiring, session assignment
   - Status: Straightforward to unit test with mocked dependencies; high value
2. **SystemProxyManager** — Windows registry manipulation, P/Invoke, snapshot/restore, crash-safe cleanup
   - Status: Requires Windows registry access; unit test with abstraction or integration test
3. **RootCertificateManager** — Cert generation, PFX load/persist, X509 chain building
   - Status: Medium difficulty; important for HTTPS interception
4. **ProxyEngine (real implementation)** — Network binding, actual HTTP/HTTPS interception
   - Current: 12 tests mock IProxyEngine (interface contract only)
   - Proposal: Opt-in integration tests on ephemeral port with real HTTP client
5. **VACUUM behavior** — File size shrinkage after ClearTrafficAsync and session delete
   - Status: Low priority; implementation detail

**Recommendation:** Priority order: ProxyHostedService (easy wins, high value) → SystemProxyManager → RootCertificateManager → real ProxyEngine integration → VACUUM tests.

**Note:** Stale test comments fixed:
- ProxyEngineTests.cs: "When Tank delivers..." removed
- IntegrationTests.cs: "real implementations being built..." removed
- TrafficStoreTests.cs: "When implementation lands..." removed

### 7. MCP Tools & Storage Alignment Verification (Mouse — 2026-03-12)

**Status:** ✅ Verified & Approved

**Scope:** Post-implementation audit of MCP tools, storage layer, hosted service wiring

**Verification Results:**
- ✅ All 13 MCP tools properly decorated with [McpServerToolType] and [McpServerTool]
- ✅ All tools have [Description] attributes at class and parameter level
- ✅ TrafficTools (5), SessionTools (5), ProxyControlTools (3) all verified
- ✅ ProxyHostedService correctly wires TrafficCaptured event → OnTrafficCaptured → storage persistence
- ✅ Session lifecycle: auto-create "default" on first traffic, auto-close on shutdown
- ✅ VACUUM properly called after ClearTrafficAsync and session delete for disk reclamation
- ✅ StartProxy.setSystemProxy parameter properly exposed with documentation
- ✅ No stale squad agent comments in code; only in history.md (correct usage)

**Storage Layer Details:**
- Dapper + SQLite with snake_case mapping via MatchNamesWithUnderscores
- Headers: JSON TEXT columns; bodies: BLOB storage
- List queries exclude bodies; detail queries include (performance optimization)
- Default connection string: "Data Source=traffic.db" (customizable via DI)
- FK cascade on session delete ensures data integrity
- WAL mode enables concurrent read performance

**Conclusion:** All components properly aligned. No changes required. Project ready for production use.

### 8. User Directive: MCP Server Shutdown Protocol (2026-03-11T18:33:23Z)

**Status:** ℹ️ Operating Procedure

**Directive:** Never force-kill the MCP server process. Always call StopProxy via MCP first to gracefully restore system proxy settings.

**Rationale:** Force-killing leaves Windows system proxy enabled with no server to respond, breaking the user's internet connection.

**Responsibility:** All agents must follow this protocol when stopping/restarting MCP server.

### 9. Coverage Gap Tests Implementation (Switch — 2026-03-14)

**Status:** ✅ Implemented

**Summary:** Implemented 21 new unit tests addressing three prioritized coverage gaps from the audit assessment.

**Tests Added:**

1. **ProxyHostedService (12 tests)** — HIGH PRIORITY ✅
   - ExecuteAsync: store initialization, event wiring
   - OnTrafficCaptured: default session creation, session ID assignment, no duplicate sessions, error resilience
   - StopAsync: engine stop when running/not running, session close when active/inactive, error tolerance, event unwiring

2. **SystemProxyManager (7 tests, 1 skip)** — MEDIUM PRIORITY ✅
   - DisableSystemProxy without prior Enable is a safe no-op
   - Dispose without Enable, Dispose idempotency
   - Event handler registration/cleanup lifecycle
   - EnableSystemProxy on non-Windows (skipped on Windows, runs on Ubuntu CI)

3. **VACUUM verification (2 tests)** — LOWER PRIORITY ✅
   - ClearTrafficAsync compacts database (verified via PRAGMA page_count)
   - DeleteSessionAsync compacts database (verified via PRAGMA page_count)

**Technical Decisions:**

1. **BackgroundService timing:** Used `await Task.Delay(100)` helper after `StartAsync` to handle .NET 10's async `ExecuteAsync` scheduling. Pragmatic approach over fragile synchronization.

2. **VACUUM verification strategy:** WAL mode makes file size comparisons unreliable. Used `PRAGMA page_count` with WAL checkpoint — page count drops after DELETE + VACUUM but stays same after DELETE alone. This proves VACUUM execution without needing to intercept SQL.

3. **SystemProxyManager:** Tests verify safe/observable behavior only (no registry modification). The non-Windows path test uses `Assert.Skip` for CI cross-platform coverage.

**Results:**
- Total tests now 122 (was 101, +21 new)
- All tests pass on Windows; 1 conditional skip + passes on Ubuntu CI
- No regressions in existing test suite

**Files Modified:**
- tests/HttpProxyMcp.Tests/ProxyHostedServiceTests.cs (+12)
- tests/HttpProxyMcp.Tests/SystemProxyManagerTests.cs (+7)
- tests/HttpProxyMcp.Tests/VacuumTests.cs (+2, new file)

---

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction
