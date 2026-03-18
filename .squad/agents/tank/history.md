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

### TLS Fingerprinting Analysis (2026-03-12)

**Research Findings:**
- **JA3/JA4 Fingerprinting:** TLS handshake hashing exposes consistent proxy fingerprints via .NET SslStream
- **TlsClient.NET Evaluation:** Golang library with CGo bindings; powerful fingerprint control but architecturally incompatible with Titanium.Web.Proxy (not designed as proxy middleware, incompatible with internal SslStream management)
- **.NET SslStream Limitations:** No public API to customize cipher suites, curves, extensions, extension ordering, signature algorithms, handshake versions, or ClientHello parameters
- **5 Alternative Mitigation Approaches:** Proxy-in-proxy (recommended), HttpClient forwarding (partial), fork Titanium (unsustainable), reflection injection (fragile), selective forwarding (hybrid)
- **Recommendation:** Accept TLS detectability — proxy is dev tool, not bot; integration overhead far exceeds benefit

**Architecture Implications:**
- **Future Extensibility:** If fingerprinting becomes requirement, introduce `IOutboundConnectionFactory` abstraction in ProxyEngine
- **Phased Approach:** Phase 0 (accept now), Phase 1 (factory abstraction), Phase 2 (proxy-in-proxy or external factories)
- **Key Insight:** Both core and Titanium.Web.Proxy tightly coupled to SslStream; fingerprinting hiding better solved as external proxy wrapper than core integration
- **Non-blocking:** No changes needed now; architecture is extensible for future if required

### HAR 1.2 Export — Phase 1 Preparation (2026-03-18)

**Context:** Morpheus completed gap analysis; identified 7 new capture fields needed for high-quality HAR export. Tank's responsibility: Add these fields to CapturedRequest/CapturedResponse capture logic in ProxyEngine.

**Fields to Capture (in ProxyEngine.CaptureRequest/CaptureResponse):**
1. **`CapturedRequest.HttpVersion`** — `e.HttpClient.Request.HttpVersion` → format as `"HTTP/{Major}.{Minor}"`
2. **`CapturedResponse.HttpVersion`** — `e.HttpClient.Response.HttpVersion` → format as `"HTTP/{Major}.{Minor}"`
3. **`TrafficEntry.ServerIpAddress`** — `e.HttpClient.UpStreamEndPoint?.Address.ToString()`
4. **`TrafficEntry.TimingBlockedMs`** — `(TimeLine["Connection Ready"] - TimeLine["Session Created"]).TotalMilliseconds`
5. **`TrafficEntry.TimingSendMs`** — `(TimeLine["Request Sent"] - TimeLine["Connection Ready"]).TotalMilliseconds`
6. **`TrafficEntry.TimingWaitMs`** — `(TimeLine["Response Received"] - TimeLine["Request Sent"]).TotalMilliseconds`
7. **`TrafficEntry.TimingReceiveMs`** — `(TimeLine["Response Sent"] - TimeLine["Response Received"]).TotalMilliseconds`

**Dependencies:**
- Mouse: Add 7 new columns to `traffic_entries` table (DB migration)
- Mouse: Extend TrafficRow DTO with 7 new fields
- Tank: Populate these fields in CaptureRequest/CaptureResponse
- After Phase 1 complete: Mouse implements HAR export serializer (Phase 2)

**Status:** Awaiting implementation — no blocking issues identified