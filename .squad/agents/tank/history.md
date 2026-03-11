# Tank — History

## Project Context

- **Project:** httpproxymcp — HTTP/HTTPS MITM proxy with traffic capture and MCP server
- **Stack:** .NET 10, C#, SQLite
- **User:** Sebastien Lebreton
- **My Focus:** Proxy engine — HTTP/HTTPS listener, MITM TLS interception with dynamic cert generation from root CA, connection management, request forwarding

## Learnings

### Architecture Foundation (2026-03-11)
- **Framework & Stack:** .NET 10, C#, SQLite with Titanium.Web.Proxy, Microsoft.Data.Sqlite, and official MCP SDK
- **Proxy Architecture:** 5-project structure with Core, Proxy, Storage, McpServer, Tests modules
- **Key Interfaces:** IProxyEngine (MITM, cert generation, forwarding), ITrafficStore (SQLite queries), ISessionManager (session lifecycle)
- **Storage Design:** Dapper chosen over EF Core for write-heavy real-time traffic capture
- **MCP Integration:** Official ModelContextProtocol SDK with [McpServerTool] auto-discovery for tool exposure
- **Team Charter:** Morpheus (architecture), Tank (proxy engine), Mouse (storage + MCP), Switch (testing), Scribe (documentation)

_(Session learnings will be appended here)

### Proxy Engine Implementation (2025-07-11)

**Files created:**
- `src/HttpProxyMcp.Proxy/ProxyEngine.cs` — `IProxyEngine` implementation wrapping Titanium.Web.Proxy
- `src/HttpProxyMcp.Proxy/RootCertificateManager.cs` — Root CA generation, persistence, and loading
- `src/HttpProxyMcp.Proxy/ServiceCollectionExtensions.cs` — DI registration via `AddProxyServices()`

**Key patterns:**
- **UserData pattern:** Request data is captured in `BeforeRequest` and stashed in `e.UserData` as a `TrafficCapture` context object. Retrieved in `BeforeResponse` to pair request+response into a `TrafficEntry`.
- **Body capture:** Bodies read via `e.GetRequestBody()` / `e.GetResponseBody()` (byte arrays). Capped at `MaxBodyCaptureBytes` (default 10MB) to prevent memory blowout.
- **Header conversion:** Titanium's `HeaderCollection.GetAllHeaders()` returns `List<HttpHeader>`. Converted to `Dictionary<string, string[]>` to match Core model contract, grouping multi-value headers.
- **Certificate lifecycle:** Titanium handles per-host cert generation internally. We just configure the root CA. Uses `X509CertificateLoader.LoadPkcs12FromFile()` (not the obsolete X509Certificate2 ctor) for .NET 10 compliance.
- **HTTPS exclusions:** `ExcludedHostnames` in config → `BeforeTunnelConnectRequest` sets `e.DecryptSsl = false` for matching hosts.
- **Event-driven capture:** `TrafficCaptured` event fired synchronously on the response handler thread. Subscribers (storage layer) handle persistence.
- **Server cert validation:** All upstream certs accepted (`e.IsValid = true`) — this is a debugging proxy, not a security boundary.

**Dependencies added to Proxy csproj:**
- `Microsoft.Extensions.DependencyInjection.Abstractions` (for `IServiceCollection`)
- `Microsoft.Extensions.Logging.Abstractions` (for `ILogger<T>`)

**McpServer wiring:** Updated `ServiceRegistration.cs` to call `HttpProxyMcp.Proxy.ServiceCollectionExtensions.AddProxyServices()` explicitly.
