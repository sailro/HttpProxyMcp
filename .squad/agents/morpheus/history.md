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

### User Preferences
- User: Sebastien Lebreton
- Prefers Titanium.Web.Proxy for MITM (not raw Kestrel)
- Prefers Microsoft.Data.Sqlite + Dapper (not EF Core)
- Prefers official ModelContextProtocol SDK
