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

### User Preferences
- User: Sebastien Lebreton
- Prefers Titanium.Web.Proxy for MITM (not raw Kestrel)
- Prefers Microsoft.Data.Sqlite + Dapper (not EF Core)
- Prefers official ModelContextProtocol SDK
