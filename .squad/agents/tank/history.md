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

**Status:** ✅ Implemented (2026-03-18)

### HAR 1.2 Capture Implementation (2026-03-18)

**Changes to `src/HttpProxyMcp.Proxy/ProxyEngine.cs`:**

1. **HTTP Version capture** — `CaptureRequest()` and `CaptureResponse()` now extract `e.HttpClient.Request.HttpVersion` / `e.HttpClient.Response.HttpVersion` (System.Version) and format as `"HTTP/{Major}.{Minor}"` via new `FormatHttpVersion()` helper. Null-safe — returns null if Titanium doesn't provide a version.

2. **Server IP Address** — `OnBeforeResponse()` now reads `e.HttpClient.UpStreamEndPoint?.Address?.ToString()` and sets `TrafficEntry.ServerIpAddress`. Null-safe for failed connections.

3. **Granular Timings** — New `ExtractTimings()` and `ComputeTimingMs()` static helpers compute HAR-style timing phases from Titanium's `e.TimeLine` dictionary:
   - `TimingSendMs` = `"Connection Ready"` → `"Request Sent"` (time to send request)
   - `TimingWaitMs` = `"Request Sent"` → `"Response Received"` (TTFB)
   - `TimingReceiveMs` = `"Response Received"` → `"Response Sent"` (download time)
   - Each phase independently nullable — missing TimeLine keys (e.g., connection reuse skips "Connection Ready") leave that phase null
   - Negative durations rejected (returns null) as a safety guard

**Model properties verified present** (Mouse added in parallel):
- `CapturedRequest.HttpVersion` (string?) ✓
- `CapturedResponse.HttpVersion` (string?) ✓
- `TrafficEntry.ServerIpAddress` (string?) ✓
- `TrafficEntry.TimingSendMs` (double?) ✓
- `TrafficEntry.TimingWaitMs` (double?) ✓
- `TrafficEntry.TimingReceiveMs` (double?) ✓

**Build:** Clean — 0 warnings, 0 errors
**Tests:** 133 total, 0 failed, 1 skipped (existing non-Windows skip)

### Timing & ServerIP Capture Fix (2026-03-18)

**Problem:** HAR export showed serverIPAddress=null for all entries (0/253 populated) and timings showed send=0, wait=total, receive=0.

**Root Causes Identified:**

1. **ServerIPAddress always null** — `e.HttpClient.UpStreamEndPoint` is a user-settable *override* property on `HttpWebClient`, not the actual resolved server IP. It defaults to null and stays null unless explicitly set by the caller. The real server IP lives on the internal `TcpServerConnection.TcpSocket.RemoteEndPoint`, which is not exposed through Titanium's public API.

2. **TimingReceiveMs always null** — `TimeLine["Response Sent"]` is populated *after* `BeforeResponse` fires (response body is copied to client between BeforeResponse and the "Response Sent" timestamp). So our code in `OnBeforeResponse` couldn't read this key — it didn't exist yet.

**Fixes Applied to `src/HttpProxyMcp.Proxy/ProxyEngine.cs`:**

1. **Moved TrafficEntry construction from `OnBeforeResponse` to new `OnAfterResponse` handler** — `AfterResponse` fires after the response is fully sent to the client, meaning all TimeLine keys ("Session Created", "Connection Ready", "Request Sent", "Response Received", "Response Sent") are populated. Body capture still happens in `BeforeResponse` (required — body is streamed to client after that handler).

2. **Split capture flow across two handlers:**
   - `OnBeforeResponse`: Captures response headers + body, stores in `TrafficCapture.Response` (UserData)
   - `OnAfterResponse`: Reads TrafficCapture from UserData, builds TrafficEntry with server IP + full timings, fires `TrafficCaptured` event

3. **Replaced `UpStreamEndPoint` with reflection-based `GetServerIpAddress()`** — Accesses the internal `HttpWebClient.connection` field → `TcpServerConnection.TcpSocket` → `Socket.RemoteEndPoint` to get the actual resolved server IP. Uses cached `FieldInfo`/`PropertyInfo` for performance. Wrapped in try/catch returning null if reflection fails (future Titanium version changes).

4. **Extended `TrafficCapture` context** — Added `CapturedResponse? Response` property so BeforeResponse can store its work for AfterResponse to consume.

**Key Titanium.Web.Proxy Learnings:**

- **`HttpWebClient.UpStreamEndPoint`** is NOT the server's IP — it's a user-configurable NIC/endpoint override. The XML doc says "Override UpStreamEndPoint for this request; Local NIC via request is made." Always null by default.
- **`ProxyServer.UpStreamEndPoint`** is the same concept at server level — a global override, not the connected IP.
- **`TcpServerConnection.TcpSocket.RemoteEndPoint`** is the actual resolved server IP — but it's internal.
- **Event lifecycle:** `BeforeRequest` → connection → "Connection Ready" → request sent → "Request Sent" → response received → "Response Received" → `BeforeResponse` → response streamed to client → "Response Sent" → `AfterResponse` → `Dispose()`
- **`AfterResponse` fires in `finally` block** — runs even on exceptions, before `Dispose()` clears UserData/connection
- **`OnRequestBodyWrite` / `OnResponseBodyWrite` are DEBUG-only** events (behind `#if DEBUG` in Titanium source)

**Build:** Clean — 0 warnings, 0 errors
**Tests:** 133 total (132 passed, 1 skipped — existing non-Windows skip)