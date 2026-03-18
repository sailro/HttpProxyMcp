# Decisions Log

## 2026-03-18: HAR 1.2 Export Gap Analysis

**Author:** Morpheus  
**Date:** 2026-03-15  
**Status:** 📋 Analysis Complete — Ready for Implementation Decision

### Summary

Analyzed the full HAR 1.2 specification against our current data model and Titanium.Web.Proxy's API surface. **We can produce valid, useful HAR files today with minor model changes.** The biggest surprise: Titanium exposes a `TimeLine` dictionary (`Dictionary<string, DateTime>`) with per-phase timestamps that maps almost perfectly to HAR timing fields — we just never captured it.

### Field-by-Field Classification

#### ✅ Captured — Direct Mapping

| HAR Field | Source | Notes |
|-----------|--------|-------|
| `entry.startedDateTime` | `TrafficEntry.StartedAt` | ISO 8601 — direct |
| `entry.time` | `TrafficEntry.Duration.TotalMilliseconds` | Direct |
| `request.method` | `CapturedRequest.Method` | Direct |
| `request.url` | `CapturedRequest.Url` | Direct |
| `request.headers[]` | `CapturedRequest.Headers` | Transform `Dict<string,string[]>` → `[{name,value}]` |
| `request.bodySize` | `CapturedRequest.Body?.Length` or `ContentLength` | Direct |
| `response.status` | `CapturedResponse.StatusCode` | Direct |
| `response.statusText` | `CapturedResponse.ReasonPhrase` | Direct |
| `response.headers[]` | `CapturedResponse.Headers` | Same transform as request |
| `response.content.mimeType` | `CapturedResponse.ContentType` | Direct |
| `response.content.size` | `CapturedResponse.ContentLength` or `Body?.Length` | Direct |
| `response.content.text` | `CapturedResponse.Body` | Base64-encode binary, UTF-8 text |
| `response.redirectURL` | Parse from `Location` header | Available in captured headers |
| `request.postData.mimeType` | `CapturedRequest.ContentType` | Direct |
| `request.postData.text` | `CapturedRequest.Body` | Decode from bytes |
| `log.creator` | Hardcode `{name:"HttpProxyMcp", version:"1.0"}` | Static |

#### 🔄 Derivable — Can Compute at Export Time

| HAR Field | Derivation | Notes |
|-----------|-----------|-------|
| `request.queryString[]` | Parse `CapturedRequest.QueryString` into NVPs | `HttpUtility.ParseQueryString()` or manual split on `&`/`=` |
| `request.cookies[]` | Parse `Cookie` header from `CapturedRequest.Headers` | Standard cookie parsing |
| `response.cookies[]` | Parse `Set-Cookie` headers from `CapturedResponse.Headers` | Standard Set-Cookie parsing |
| `request.headersSize` | Reconstruct HTTP/1.1 header block from captured headers, measure bytes | `"{Method} {Path} HTTP/1.1\r\n" + headers + "\r\n\r\n"` |
| `response.headersSize` | Same approach: `"HTTP/1.1 {StatusCode} {ReasonPhrase}\r\n" + headers + "\r\n\r\n"` | Approximation |
| `response.content.encoding` | Set to `"base64"` for binary bodies | Convention |
| `response.content.compression` | `ContentLength - Body.Length` (if Content-Encoding present) | Approximation |

#### ❌ Missing — Not Currently Captured, But Available in Titanium

| HAR Field | Titanium API | Effort | Recommendation |
|-----------|-------------|--------|----------------|
| `request.httpVersion` | `e.HttpClient.Request.HttpVersion` (`System.Version`) | **Low** — add `string? HttpVersion` to `CapturedRequest` | **Capture it** |
| `response.httpVersion` | `e.HttpClient.Response.HttpVersion` (`System.Version`) | **Low** — add `string? HttpVersion` to `CapturedResponse` | **Capture it** |
| `entry.serverIPAddress` | `e.HttpClient.UpStreamEndPoint?.Address` (`IPEndPoint`) | **Low** — add `string? ServerIpAddress` to `TrafficEntry` | **Capture it** |
| `entry.connection` | `e.ServerConnectionId` (`Guid`) | **Low** — add `string? ConnectionId` to `TrafficEntry` | **Optional — nice-to-have** |
| `timings.send` | `TimeLine["Request Sent"] - TimeLine["Connection Ready"]` | **Medium** — capture `TimeLine` dictionary | **Capture it** |
| `timings.wait` | `TimeLine["Response Received"] - TimeLine["Request Sent"]` | **Medium** — same | **Capture it** |
| `timings.receive` | `TimeLine["Response Sent"] - TimeLine["Response Received"]` | **Medium** — same | **Capture it** |
| `timings.blocked` | `TimeLine["Connection Ready"] - TimeLine["Session Created"]` | **Medium** — same | **Capture it** |

#### ⏭️ Not Applicable — HAR Allows Defaults

| HAR Field | Default Value | Notes |
|-----------|--------------|-------|
| `cache` | `{}` | HAR spec allows empty object |
| `log.browser` | Omit | Optional |
| `log.pages` | Omit or `[]` | Optional; could map ProxySession → page later |
| `log.comment` | Omit | Optional |
| `timings.dns` | `-1` | Not available from Titanium (resolved internally) |
| `timings.connect` | `-1` | Not separable from `blocked` without lower-level hooks |
| `timings.ssl` | `-1` | Not available from Titanium |
| `entry.pageref` | Omit | Optional |

### Key Findings

#### 1. HttpVersion — Available, Easy Win

`Request.HttpVersion` and `Response.HttpVersion` are `System.Version` properties on Titanium's types. We simply never mapped them. Format as `"HTTP/{Major}.{Minor}"` (e.g., `"HTTP/1.1"`, `"HTTP/2.0"`).

**Impact:** 2 new string properties, 2 new DB columns, 4 lines changed in `CaptureRequest`/`CaptureResponse`.

#### 2. TimeLine — The Hidden Goldmine

Titanium's `SessionEventArgs` exposes `Dictionary<string, DateTime> TimeLine` with these keys set during the request lifecycle:

| Key | Set When |
|-----|----------|
| `"Session Created"` | Session constructor (connection received from client) |
| `"Connection Ready"` | TCP + TLS connection to server established |
| `"Request Sent"` | Request headers + body fully sent to server |
| `"Response Received"` | Response headers read from server |
| `"Response Sent"` | Response fully sent back to client |

This maps to HAR timings:
- `blocked` = `"Connection Ready"` − `"Session Created"` (includes queue + connection setup)
- `send` = `"Request Sent"` − `"Connection Ready"`
- `wait` = `"Response Received"` − `"Request Sent"` (TTFB)
- `receive` = `"Response Sent"` − `"Response Received"`

**Impact:** We should capture at least 3 computed durations from TimeLine (send, wait, receive) as nullable `double?` millisecond fields on `TrafficEntry`. Or store the raw TimeLine as JSON for maximum flexibility.

#### 3. Server IP Address — Available, Valuable

`e.HttpClient.UpStreamEndPoint?.Address.ToString()` gives the resolved server IP. The explore agent initially referenced `e.WebSession.ServerEndPoint` but the actual verified property path is `e.HttpClient.UpStreamEndPoint` (an `IPEndPoint` on `HttpWebClient`). Note: `e.WebSession` is a deprecated alias for `e.HttpClient`.

**Impact:** 1 new field on TrafficEntry, 1 new DB column.

#### 4. Header Sizes — Derivable with Caveats

We can reconstruct approximate header byte sizes from captured data at export time. The approximation is acceptable because:
- We capture all headers
- The only missing piece is the exact wire format (whitespace, ordering)
- HAR spec says "or -1 if unavailable" — so approximation is better than -1

### Recommendation

#### Phase 1: Model + Capture Changes (Before HAR Export Implementation)

Add to the data model and proxy capture:

1. **`CapturedRequest.HttpVersion`** (`string?`) — from `e.HttpClient.Request.HttpVersion`
2. **`CapturedResponse.HttpVersion`** (`string?`) — from `e.HttpClient.Response.HttpVersion`
3. **`TrafficEntry.ServerIpAddress`** (`string?`) — from `e.HttpClient.UpStreamEndPoint?.Address`
4. **`TrafficEntry.TimingSendMs`** (`double?`) — computed from TimeLine
5. **`TrafficEntry.TimingWaitMs`** (`double?`) — computed from TimeLine
6. **`TrafficEntry.TimingReceiveMs`** (`double?`) — computed from TimeLine
7. **`TrafficEntry.TimingBlockedMs`** (`double?`) — computed from TimeLine

**DB migration:** Add 7 columns to `traffic_entries` (`http_version` on both request/response sides, `server_ip_address`, `timing_send_ms`, `timing_wait_ms`, `timing_receive_ms`, `timing_blocked_ms`).

#### Phase 2: HAR Export (Pure Transformation, No Proxy Changes)

Build the HAR serializer using captured + derived data. Derivable fields (cookies, queryString NVPs, header sizes) computed at export time — no need to store separately.

### What We Don't Need

- `timings.dns`, `timings.connect`, `timings.ssl` → Use `-1` (not separable in Titanium)
- `cache` → Use `{}`
- `log.pages` → Omit initially; map ProxySession later if needed
- `log.browser` → Omit

### Verdict

**We can produce valid, high-quality HAR 1.2 files.** The 7 new capture fields are all available in Titanium's API and require minimal effort. The TimeLine discovery in particular elevates this from "good enough with defaults" to "genuinely useful timing data." Without the model changes we could still produce technically valid HAR by defaulting httpVersion to "HTTP/1.1" and putting Duration in `wait` — but with the changes, we produce *accurate* HAR that matches what Chrome DevTools would export.

---

## 2026-03-14: TLS Fingerprinting & Architectural Extensibility

**Authors:** Morpheus, Tank  
**Status:** 📋 Decision — Accept Detectability, No Core Changes

### Problem Statement

The proxy is detectable via **TLS fingerprinting** (JA3/JA4 analysis). Servers can identify that outbound connections originate from a .NET proxy rather than a browser, enabling bot detection and blocking.

### Key Findings

1. **Titanium.Web.Proxy limitations** — No clean extension point for socket-level TLS control; delegates to .NET's SslStream, which is tightly coupled to Schannel (Windows) / OpenSSL (Linux) with no per-connection customization APIs
2. **TlsClient.NET evaluation** — Theoretically could provide browser-like fingerprints, but architectural integration would require gutting Titanium's forwarding logic and managing native Go FFI layer
3. **5 integration approaches evaluated** — proxy-in-proxy (cleanest if needed), HttpClient forwarding (protocol complexity), fork Titanium (unsustainable), reflection injection (fragile), selective forwarding (hybrid)

### Recommendation

**Accept TLS detectability.** Do NOT integrate TlsClient.NET into core proxy.

#### Rationale

- **Out of scope:** We're a dev tool for debugging your own traffic, not bypassing anti-bot systems
- **Complexity explosion:** TlsClient.NET integration requires rewriting half the proxy's forwarding logic
- **Marginal benefit:** Even with perfect TLS fingerprints, MITM proxies are detectable via cert pinning, HSTS, OCSP, behavioral analysis
- **Maintenance burden:** Native Go library + FFI + browser profile updates as they evolve
- **Precedent:** Industry-standard proxies (mitmproxy, Fiddler, Burp Suite) are all detectable and thriving

#### Future Extensibility

If TLS fingerprinting becomes a requirement:

1. **Phase 1:** Build a **standalone TlsClient.NET forwarding proxy** (separate from HttpProxyMcp)
2. **Phase 2:** Allow users to chain HttpProxyMcp → TlsClient.NET proxy manually via upstream proxy config
3. **Phase 3:** Only integrate into core if TlsClient.NET gains Stream-level API or Titanium adds extensibility hooks

#### Current Actions

✅ Document the limitation in README (mention JA3/JA4 detectability, TLS stack being .NET SslStream)  
✅ Ensure `ExcludedHostnames` is prominently documented as escape hatch for aggressive bot detection scenarios

#### Architecture Decision

**IOutboundConnectionFactory abstraction:** Proposed for future (Phase 1-2 if needed), but not implemented now. When required, introduce this abstraction in ProxyEngine to allow:
- DirectConnectionFactory (default) — standard SslStream
- ProxyChainFactory — routes through external proxy
- TlsClientFactory (future) — uses TlsClient.NET if integrated

**Why not now:** Over-engineering without a blocking requirement. Current proxy architecture is sound for dev-tool use case.

---

## Decision-Making Notes

- **HAR 1.2 Export:** Owned by Morpheus (architecture). Phase 1 (model changes) and Phase 2 (serializer) will be picked up by Tank (proxy capture) and Mouse (MCP tools + storage).
- **TLS Fingerprinting:** Consensus decision (Morpheus + Tank) to accept detectability. Documented for future reference if requirements change.
- **Cross-agent alignment:** All agents aware of HAR upcoming work and TLS fingerprinting decision. No blocking dependencies.
