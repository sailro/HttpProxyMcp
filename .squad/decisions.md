# Squad Decisions

## Active Decisions

### Architecture & Technology Stack (2026-03-11)

**Status:** ✅ Approved (Morpheus)

- **Framework:** .NET 10 with `net10.0` TFM
- **Proxy:** Titanium.Web.Proxy 3.2.0 for HTTP/HTTPS MITM with dynamic TLS cert generation
- **Storage:** Microsoft.Data.Sqlite 10.0.4 + Dapper 2.1.72 (chosen for write-heavy traffic capture workload vs EF Core)
- **MCP:** Official ModelContextProtocol 1.1.0 SDK with `WithToolsFromAssembly()` auto-discovery
- **Project Structure:** 5 projects (Core, Proxy, Storage, McpServer, Tests)

**Rationale:**
- .NET 10 confirmed available and compatible with all dependencies
- Titanium.Web.Proxy is industry-standard for MITM interception
- Dapper provides low-overhead, high-throughput data access for real-time traffic capture
- Official MCP SDK ensures long-term support and community alignment

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction
