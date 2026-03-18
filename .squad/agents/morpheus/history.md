# Morpheus — History

## Project Context

- **Project:** httpproxymcp — HTTP/HTTPS MITM proxy with traffic capture and MCP server
- **Stack:** .NET 10, C#, SQLite
- **User:** Sebastien Lebreton
- **Key Components:**
  1. HTTP/HTTPS proxy with MITM HTTPS interception (dynamic cert generation from root CA)
  2. SQLite-based storage for all traffic (headers, bodies, timing, status codes, URLs, hostnames)
  3. MCP server with tools for: listing sessions, filtering by hostname/URL/status/method/time, getting full request/response details, searching bodies, traffic stats, proxy start/stop, clearing data

## Learnings

### Code & Documentation Reassessment (2026-03-14)
- **README.md:** Comprehensive and accurate. All 13 MCP tools documented with correct parameters. Architecture, proxy control, system proxy, root CA sections all align with code.
- **Copilot instructions:** Up-to-date. Code conventions, storage layer, testing patterns, Windows proxy management all match actual implementation.
- **CLAUDE.md / AGENTS.md:** Correctly redirect to copilot-instructions.md.
- **Source code cleanliness:** No stale comments (Tank/Mouse/Switch/Morpheus references, TODO/FIXME/HACK/STUB). Code is clean and production-ready.
- **.gitignore:** Comprehensive coverage of .NET artifacts, Squad runtime state, SQLite databases, Node modules. Root CA certificates (*.pfx) not excluded by design — stored in user AppData, not repo.
- **Minor note:** Custom instruction claims "101 tests across 9 files"; actual test file count is 8 (excluding 2 helper files). No functional impact.
- **Recommendation:** Document RootCertificateManager's default PFX path in README for clarity.

### TLS Fingerprinting & Architectural Extensibility (2026-03-12)

**Research Scope:** Analyzed TLS fingerprinting mechanisms and evaluated 5 integration approaches for potential fingerprint mitigation.

**Key Architectural Insights:**
- **Proxy-in-Proxy Pattern:** Cleanest solution IF fingerprinting becomes requirement — chain through external TLS-spoofing proxy (mitmproxy, Fiddler)
- **IOutboundConnectionFactory Abstraction:** Proposed future extension point:
  - DirectConnectionFactory (default) — standard SslStream
  - ProxyChainFactory — routes through external proxy
  - TlsClientFactory (future) — uses TlsClient.NET if integrated
- **No Core Integration:** Tank's evaluation confirmed TlsClient.NET incompatible with Titanium.Web.Proxy integration; keeping fingerprinting mitigation out of core is correct architectural decision
- **Phased Implementation Plan:**
  - Phase 0 (Now): Accept TLS detectability; document if needed
  - Phase 1 (If required): Introduce IOutboundConnectionFactory abstraction in ProxyEngine (~100 lines)
  - Phase 2 (Future): Implement external proxy factories without further core changes

**Why No Core Integration:**
- Titanium.Web.Proxy tightly coupled to .NET SslStream; no interception point for TlsClient.NET
- P/Invoke overhead and platform-specific code violate proxy's clean architecture
- Proxy-in-proxy solution is simpler, more maintainable, and leverages existing tools
- Architecture extensible for future without current code changes

**Non-blocking:** TLS detectability is acceptable for dev-tool use case. No user has raised this as blocker. Extensible architecture ready if requirement emerges.

### Architecture Decisions (2025-07-11)
- .NET 10 SDK (10.0.200) confirmed available; `net10.0` TFM works natively with `dotnet new`
- .NET 10 uses `.slnx` format by default (not `.sln`)
- Titanium.Web.Proxy 3.2.0 installs cleanly on net10.0 — targets netstandard, no compatibility issues
- ModelContextProtocol 1.1.0 is the official MCP SDK for .NET; supports `WithStdioServerTransport()` and `WithToolsFromAssembly()` for auto-discovery of `[McpServerTool]` methods
- Microsoft.Data.Sqlite 10.0.4 + Dapper 2.1.72 for storage; chose Dapper over EF Core for write-heavy capture workload
- MCP tools use static methods with DI parameters — the SDK resolves `ITrafficStore`, `IProxyEngine`, `ISessionManager` from the container automatically

### Key File Paths
- Solution: `HttpProxyMcp.slnx` (root)
- Core interfaces: `src/HttpProxyMcp.Core/Interfaces/` — `IProxyEngine.cs`, `ITrafficStore.cs`, `ISessionManager.cs`
- Core models: `src/HttpProxyMcp.Core/Models/` — `TrafficEntry`, `CapturedRequest`, `CapturedResponse`, `ProxySession`, `TrafficFilter`, `TrafficStatistics`, `ProxyConfiguration`
- MCP tools: `src/HttpProxyMcp.McpServer/Tools/` — `TrafficTools.cs`, `ProxyControlTools.cs`, `SessionTools.cs`
- MCP host: `src/HttpProxyMcp.McpServer/Program.cs` + `ProxyHostedService.cs` + `ServiceRegistration.cs`
- Tests: `tests/HttpProxyMcp.Tests/ModelTests.cs`

### HAR 1.2 Export Gap Analysis (2026-03-15)

**Scope:** Full field-by-field analysis of HAR 1.2 specification vs current data model and Titanium.Web.Proxy API surface.

**Key Discoveries:**
1. **Titanium `TimeLine` dictionary** — `SessionEventArgs.TimeLine` is `Dictionary<string, DateTime>` with keys: `"Session Created"`, `"Connection Ready"`, `"Request Sent"`, `"Response Received"`, `"Response Sent"`. Maps directly to HAR timing fields (blocked, send, wait, receive). We never captured this.
2. **HttpVersion available** — `e.HttpClient.Request.HttpVersion` and `e.HttpClient.Response.HttpVersion` are `System.Version` properties. We never mapped them.
3. **Server IP available** — `e.HttpClient.UpStreamEndPoint?.Address` (IPEndPoint) provides resolved server IP. Note: `e.WebSession` is deprecated alias for `e.HttpClient`.
4. **Connection ID available** — `e.ServerConnectionId` (Guid) for TCP connection tracking.

**Recommended Model Changes (7 new fields):**
- `CapturedRequest.HttpVersion` (string?) — from `e.HttpClient.Request.HttpVersion`
- `CapturedResponse.HttpVersion` (string?) — from `e.HttpClient.Response.HttpVersion`
- `TrafficEntry.ServerIpAddress` (string?) — from `e.HttpClient.UpStreamEndPoint?.Address`
- `TrafficEntry.TimingSendMs` (double?) — `TimeLine["Request Sent"] - TimeLine["Connection Ready"]`
- `TrafficEntry.TimingWaitMs` (double?) — `TimeLine["Response Received"] - TimeLine["Request Sent"]`
- `TrafficEntry.TimingReceiveMs` (double?) — `TimeLine["Response Sent"] - TimeLine["Response Received"]`
- `TrafficEntry.TimingBlockedMs` (double?) — `TimeLine["Connection Ready"] - TimeLine["Session Created"]`

**Verdict:** Can produce valid, high-quality HAR 1.2 with these changes. Without them, still valid but with defaults. Derivable fields (cookies, queryString NVPs, header sizes) computed at export time.

**Decision document:** `.squad/decisions/inbox/morpheus-har-gap-analysis.md`

### Key File Paths (Updated)
- Titanium API types used: `Titanium.Web.Proxy.EventArguments.SessionEventArgs`, `Titanium.Web.Proxy.Http.Request`, `Titanium.Web.Proxy.Http.Response`, `Titanium.Web.Proxy.Http.HttpWebClient`
- Server IP property: `e.HttpClient.UpStreamEndPoint?.Address` (NOT `e.WebSession.ServerEndPoint`)
- TimeLine property: `e.TimeLine` dictionary on `SessionEventArgsBase`
- Capture helpers: `ProxyEngine.cs` lines 239-271 (`CaptureRequest`, `CaptureResponse`)

### Database Schema Migration Strategy for HAR 1.2 Export (2026-03-15)

**Problem:** Adding 6 columns to traffic_entries table. What happens to existing users with old 24-column DBs?

**Key Findings:**
1. **INSERT pattern is safe** — SaveTrafficEntryAsync uses explicit column lists, so new columns are optional
2. **SELECT * breaks** — GetTrafficEntryAsync uses `SELECT *`; Dapper tries to map 6 new columns to TrafficRow, fails with InvalidOperationException
3. **CREATE TABLE IF NOT EXISTS is NOT sufficient** — doesn't add missing columns to existing tables

**Critical Discovery:** SQL query patterns in use:
- List views (QueryTrafficAsync, SearchBodiesAsync): explicit column enumeration ✅ safe
- Detail view (GetTrafficEntryAsync): `SELECT *` ❌ breaks on schema drift
- Statistics queries: aggregate with specific columns ✅ safe

**Recommended Migration Strategy:** ALTER TABLE with PRAGMA table_info idempotency check
- Query `PRAGMA table_info(traffic_entries)` in InitializeAsync to detect existing columns
- For each new column, check if it exists; if not, run `ALTER TABLE ADD COLUMN`
- SQLite's ALTER TABLE is fast and doesn't rewrite the table
- Old rows automatically get NULL for new columns; new rows capture HAR data
- Transparent to users — runs on first startup after upgrade

**Why this approach:**
- Simplest implementation (1 query + 6 conditional ALTERs)
- Idempotent (safe to re-run)
- Zero user action required
- Data-safe (no recreation, no copy risk)
- Aligns with existing pattern (extends InitializeAsync)

**Decision document:** `.squad/decisions/inbox/morpheus-db-migration.md`

### User Preferences
- User: Sebastien Lebreton
- Prefers Titanium.Web.Proxy for MITM (not raw Kestrel)
- Prefers Microsoft.Data.Sqlite + Dapper (not EF Core)
- Prefers official ModelContextProtocol SDK
