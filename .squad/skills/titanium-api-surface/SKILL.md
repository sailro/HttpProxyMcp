---
name: "titanium-api-surface"
description: "Titanium.Web.Proxy API properties available but not captured in our model"
domain: "proxy-engine"
confidence: "high"
source: "earned — reflection inspection of Titanium.Web.Proxy 3.2.0 DLL + source code review"
---

## Context

When extending ProxyEngine capture (e.g., for HAR export, analytics, debugging features), agents need to know what data Titanium.Web.Proxy makes available beyond what we currently capture.

## Patterns

### Available Properties on SessionEventArgs (e)

**Request** (`e.HttpClient.Request`):
- `HttpVersion` → `System.Version` (e.g., 1.1, 2.0)
- `Method`, `Url`, `RequestUri`, `Host`
- `Headers` → `HeaderCollection`
- `ContentType`, `ContentLength`, `ContentEncoding`
- `HasBody`, `IsBodyRead`, `BodyLength`
- `IsHttps`, `IsChunked`, `IsMultipartFormData`
- `ExpectContinue`, `UpgradeToWebSocket`
- `HeaderText` → raw header text

**Response** (`e.HttpClient.Response`):
- `HttpVersion` → `System.Version`
- `StatusCode`, `StatusDescription`
- `Headers`, `ContentType`, `ContentLength`, `ContentEncoding`
- `HasBody`, `IsBodyRead`, `BodyLength`
- `KeepAlive`, `IsChunked`
- `HeaderText` → raw header text

**Connection/Endpoint** (`e.HttpClient`):
- `UpStreamEndPoint` → `IPEndPoint` (server IP + port)
- `IsHttps` → bool
- `ConnectRequest` → CONNECT tunnel info
- `UserData` → arbitrary state

**Session** (`e` / `SessionEventArgsBase`):
- `TimeLine` → `Dictionary<string, DateTime>` with keys:
  - `"Session Created"` — session constructor
  - `"Connection Ready"` — TCP+TLS established
  - `"Request Sent"` — request fully sent to server
  - `"Response Received"` — response headers received
  - `"Response Sent"` — response fully sent to client
- `ClientConnectionId` → `Guid`
- `ServerConnectionId` → `Guid`
- `ClientLocalEndPoint`, `ClientRemoteEndPoint` → `IPEndPoint`
- `ProxyEndPoint` → which listener handled this
- `IsHttps`, `IsTransparent`, `IsSocks`

### Deprecated Aliases
- `e.WebSession` → use `e.HttpClient` instead
- `e.ClientEndPoint` → use `e.ClientRemoteEndPoint` instead
- `e.LocalEndPoint` → use `e.ProxyEndPoint` instead

## Examples

```csharp
// Capture HTTP version
var httpVersion = e.HttpClient.Request.HttpVersion;
string versionString = $"HTTP/{httpVersion.Major}.{httpVersion.Minor}";

// Capture server IP
var serverIp = e.HttpClient.UpStreamEndPoint?.Address?.ToString();

// Compute HAR timings from TimeLine
var tl = e.TimeLine;
double? sendMs = tl.ContainsKey("Request Sent") && tl.ContainsKey("Connection Ready")
    ? (tl["Request Sent"] - tl["Connection Ready"]).TotalMilliseconds : null;
double? waitMs = tl.ContainsKey("Response Received") && tl.ContainsKey("Request Sent")
    ? (tl["Response Received"] - tl["Request Sent"]).TotalMilliseconds : null;
```

## Anti-Patterns

- Do NOT use `e.WebSession` — it is deprecated; use `e.HttpClient`
- Do NOT assume all TimeLine keys exist — connection reuse may skip `"Connection Ready"`
- Do NOT assume `UpStreamEndPoint` is always populated — it may be null for failed connections
- Do NOT try to get timing data from `.NET Stopwatch` separately — use TimeLine for consistency
