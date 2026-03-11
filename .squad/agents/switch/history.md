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
- Tests written against interfaces — will validate real implementations when Tank and Mouse deliver

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
