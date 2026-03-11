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

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction
