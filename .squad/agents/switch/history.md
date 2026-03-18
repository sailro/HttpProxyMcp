# Switch — History

## Project Context

- **Project:** httpproxymcp — HTTP/HTTPS MITM proxy with traffic capture and MCP server
- **Stack:** .NET 10, C#, SQLite
- **User:** Sebastien Lebreton
- **My Focus:** Testing all components — proxy engine, storage layer, MCP tools. Integration tests for full request flows.

## Learnings

### Architecture Foundation (2026-03-11)
- **Framework & Stack:** .NET 10, C#, SQLite with Titanium.Web.Proxy, Microsoft.Data.Sqlite, and official MCP SDK
- **Proxy Architecture:** 5-project structure with Core, Proxy, Storage, McpServer, Tests modules
- **Key Interfaces:** IProxyEngine (MITM, cert generation, forwarding), ITrafficStore (SQLite queries), ISessionManager (session lifecycle)
- **Storage Design:** Dapper chosen over EF Core for write-heavy real-time traffic capture
- **MCP Integration:** Official ModelContextProtocol SDK with [McpServerTool] auto-discovery for tool exposure
- **Team Charter:** Morpheus (architecture), Tank (proxy engine), Mouse (storage + MCP), Switch (testing), Scribe (documentation)

### Test Suite Authoring (2025-07-11)

**101 tests written and passing.** All compile against the current scaffolded solution.

#### Test File Structure
```
tests/HttpProxyMcp.Tests/
├── Helpers/
│   ├── TrafficEntryBuilder.cs     — Fluent builder for test TrafficEntry objects
│   └── TestData.cs                — Batch generators for varied/large entries
├── Storage/
│   ├── TrafficStoreTests.cs       — 25 tests: save/retrieve, filtering, pagination, stats, clear, empty/large bodies
│   └── SessionManagerTests.cs     — 13 tests: CRUD, active session, list, close, delete
├── McpTools/
│   ├── TrafficToolTests.cs        — 18 tests: ListTraffic filters, GetEntry, SearchBodies, GetStatistics, ClearTraffic
│   ├── SessionToolTests.cs        — 7 tests: ListSessions, CreateSession, SetActive, Close, Delete
│   └── ProxyControlToolTests.cs   — 7 tests: Start/Stop/Status with running/not-running states
├── Proxy/
│   └── ProxyEngineTests.cs        — 12 tests: lifecycle, event firing, HTTPS/HTTP, concurrency, body capture
├── IntegrationTests.cs            — 6 tests: full flow capture→store→query, multi-host filter, session lifecycle, search, stats, proxy lifecycle
└── ModelTests.cs                  — 7 tests (pre-existing from Morpheus)
```

#### Key Patterns
- **NSubstitute** for mocking — `TrafficEntry` doesn't extend `EventArgs`, use `Raise.Event<EventHandler<TrafficEntry>>()` not `Raise.EventWith<T>()`
- **FluentAssertions** for readable assertions
- **TrafficEntryBuilder** — fluent builder with `Get()` / `Post()` factory methods
- Integration tests marked with `[Trait("Category", "Integration")]`
- Tests written against interfaces — real implementations are now delivered and running

#### NuGet Packages Added
- `NSubstitute 5.3.0`, `FluentAssertions 8.4.0`

#### Edge Cases Covered
- Empty database returns empty/zero (not null or exceptions)
- Large bodies (1MB) round-trip correctly
- Pending responses display "pending"
- Binary vs text body rendering
- Status code range filtering (MinStatusCode/MaxStatusCode)
- Time range filtering (After/Before)
- Session-scoped body search
- Proxy idempotent start/stop messaging

### Test Audit & Coverage Gap Analysis (2026-03-11)

**Stale comment cleanup:** Found and fixed 3 stale comments referencing squad agents or "when X delivers/lands":
1. `ProxyEngineTests.cs` — removed "When Tank delivers the Titanium.Web.Proxy implementation, upgrade to real tests"
2. `IntegrationTests.cs` — removed "the real implementations are being built by Tank and Mouse"
3. `TrafficStoreTests.cs` — removed "When a real implementation lands, these can be upgraded to integration tests"
Also cleaned up a stale bullet in this history file referencing "when Tank and Mouse deliver."

**ProxyEngineTests analysis:** All 12 tests mock `IProxyEngine` via NSubstitute — none test the real `ProxyEngine` class. These are interface-contract tests, not implementation tests.

**Missing test coverage identified (no tests exist for):**
- `SystemProxyManager` — Windows registry manipulation, P/Invoke, snapshot/restore
- `ProxyHostedService` — auto-close active session on shutdown, event wiring
- VACUUM after `ClearTrafficAsync` / session delete in `SqliteTrafficStore`
- Crash-safe proxy cleanup (ProcessExit / CancelKeyPress hooks in SystemProxyManager)
- `RootCertificateManager` — cert generation, PFX load/persist

**Build note:** DLL locking prevents rebuild when McpServer process is running. Tests cannot be re-executed until the process is stopped.

### Team Cross-Updates (2026-03-11)
- **Morpheus's code audit:** Project in excellent state; documentation comprehensive and current; no architectural drift detected.
- **Mouse's storage verification:** Confirmed all 13 MCP tools properly annotated, ProxyHostedService event wiring correct, VACUUM behavior implemented. No changes needed.

### Coverage Gap Tests Written (2026-03-14)

**21 new tests written and passing.** Total test count: 122 (121 pass, 1 skipped on Windows).

#### New Test Files

| File | Tests | Coverage |
|------|-------|----------|
| `ProxyHostedServiceTests.cs` | 12 | ExecuteAsync initialization, event wiring, OnTrafficCaptured default session creation, session ID assignment, error resilience, StopAsync engine stop/skip, session close/skip, error tolerance, event unwiring |
| `Proxy/SystemProxyManagerTests.cs` | 7 (1 skip) | DisableSystemProxy without prior Enable is no-op, Dispose without Enable, Dispose idempotency, event handler lifecycle, EnableSystemProxy non-Windows skip |
| `Storage/VacuumTests.cs` | 2 | ClearTrafficAsync VACUUM compaction via page_count, DeleteSessionAsync VACUUM compaction via page_count |

#### Key Patterns & Learnings
- **BackgroundService timing:** `ExecuteAsync` runs asynchronously even when the first await is on a completed Task (.NET 10). Must add `await Task.Delay(100)` after `StartAsync` in tests — created `StartServiceAsync()` helper.
- **async void event handlers:** `OnTrafficCaptured` is `async void`; need `await Task.Delay(100)` after raising events to let the handler complete before asserting.
- **NSubstitute event tracking:** NSubstitute properly tracks `+=` and `-=` on mocked events — `Raise.Event` only fires to currently-subscribed handlers.
- **WAL mode VACUUM testing:** File size comparisons (db + wal + shm) are unreliable with WAL mode. Used `PRAGMA page_count` with WAL checkpoint before measuring — page count drops after DELETE + VACUUM, stays same after DELETE alone.
- **xUnit v3 Assert.Skip:** Works for conditional platform skipping (non-Windows test skipped on Windows, runs on Ubuntu CI).

### HAR 1.2 Field Tests (2026-03-18)

**10 new tests written and passing.** Total test count: 133 (132 pass, 1 skipped on Windows).

#### New Test Files

| File | Tests | Coverage |
|------|-------|----------|
| `Storage/HarFieldsTests.cs` | 6 | Round-trip of all 6 HAR fields, null fields, partial fields, zero timings, IPv6 address, existing fields unaffected |
| `Storage/MigrationTests.cs` | 4 | Fresh DB schema includes HAR columns, old schema migration adds columns, idempotent InitializeAsync, old data preserved after migration |

#### TrafficEntryBuilder Extensions
- `WithHttpVersion(requestVersion, responseVersion)` — sets CapturedRequest.HttpVersion and CapturedResponse.HttpVersion
- `WithServerIpAddress(ip)` — sets TrafficEntry.ServerIpAddress
- `WithTimings(sendMs, waitMs, receiveMs)` — sets TrafficEntry.TimingSendMs/WaitMs/ReceiveMs

#### Key Observations
- Mouse already implemented all storage changes (CREATE TABLE, ALTER TABLE migration, INSERT/SELECT, MapToEntry) — tests verify the contract is fulfilled
- Zero-valued timings (0.0) are correctly distinguished from null — important for HAR spec compliance
- IPv6 addresses round-trip correctly through TEXT columns
- Old schema migration preserves existing data while adding new nullable columns
- All 10 tests are integration tests using real SQLite (marked `[Trait("Category", "Integration")]`)
