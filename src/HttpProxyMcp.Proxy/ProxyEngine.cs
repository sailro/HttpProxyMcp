using System.Net;
using HttpProxyMcp.Core.Interfaces;
using HttpProxyMcp.Core.Models;
using Microsoft.Extensions.Logging;
using Titanium.Web.Proxy;
using Titanium.Web.Proxy.EventArguments;
using Titanium.Web.Proxy.Http;
using Titanium.Web.Proxy.Models;

namespace HttpProxyMcp.Proxy;

// MITM proxy engine backed by Titanium.Web.Proxy.
// Intercepts HTTP and HTTPS traffic, captures request/response pairs,
// and raises TrafficCaptured for each completed exchange.
public sealed class ProxyEngine(
	ILogger<ProxyEngine> logger,
	RootCertificateManager rootCertManager,
	SystemProxyManager systemProxyManager)
	: IProxyEngine, IDisposable
{
	private ProxyServer? _proxyServer;
    private ExplicitProxyEndPoint? _endPoint;
    private ProxyConfiguration _configuration = new();

    public bool IsRunning { get; private set; }
    public ProxyConfiguration Configuration => _configuration;

    public event EventHandler<TrafficEntry>? TrafficCaptured;

    public Task StartAsync(ProxyConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (IsRunning)
            throw new InvalidOperationException("Proxy is already running.");

        _configuration = configuration;
        _proxyServer = new ProxyServer();

        // SSL configuration
        if (configuration.EnableSsl)
        {
            rootCertManager.ConfigureCertificates(
                _proxyServer,
                configuration.RootCertificatePath,
                configuration.RootCertificatePassword);
        }

        // Wire up event handlers
        _proxyServer.BeforeRequest += OnBeforeRequest;
        _proxyServer.BeforeResponse += OnBeforeResponse;
        _proxyServer.ServerCertificateValidationCallback += OnServerCertificateValidation;

        // Create explicit proxy endpoint (clients must configure this as their proxy)
        _endPoint = new ExplicitProxyEndPoint(IPAddress.Any, configuration.Port, configuration.EnableSsl);

        if (configuration.EnableSsl)
        {
            _endPoint.BeforeTunnelConnectRequest += OnBeforeTunnelConnectRequest;
        }

        _proxyServer.AddEndPoint(_endPoint);

        try
        {
            _proxyServer.Start();
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            // Port is likely already in use — clean up the partially-created proxy server
            _proxyServer.Dispose();
            _proxyServer = null;
            _endPoint = null;
            throw new InvalidOperationException(
                $"Could not bind to port {configuration.Port}: {ex.Message}", ex);
        }

        IsRunning = true;
        logger.LogInformation(
            "Proxy started on port {Port}, SSL={EnableSsl}",
            configuration.Port,
            configuration.EnableSsl);

        if (configuration.SetSystemProxy)
        {
            systemProxyManager.EnableSystemProxy(configuration.Port);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning || _proxyServer is not { } proxyServer)
            return Task.CompletedTask;

        proxyServer.BeforeRequest -= OnBeforeRequest;
        proxyServer.BeforeResponse -= OnBeforeResponse;
        proxyServer.ServerCertificateValidationCallback -= OnServerCertificateValidation;

        if (_endPoint is { } endPoint)
        {
            endPoint.BeforeTunnelConnectRequest -= OnBeforeTunnelConnectRequest;
        }

        proxyServer.Stop();
        proxyServer.Dispose();
        _proxyServer = null;
        _endPoint = null;

        if (_configuration.SetSystemProxy)
        {
            systemProxyManager.DisableSystemProxy();
        }

        IsRunning = false;
        logger.LogInformation("Proxy stopped");

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (IsRunning)
        {
            StopAsync().GetAwaiter().GetResult();
        }
    }

    // ── Event handlers ───────────────────────────────────────────────

    private async Task OnBeforeRequest(object sender, SessionEventArgs e)
    {
        CancellationCheck();

        var capturedRequest = CaptureRequest(e);

        // Read request body before forwarding (body is only available here)
        if (e.HttpClient.Request.HasBody)
        {
            try
            {
                var bodyBytes = await e.GetRequestBody();
                if (bodyBytes is not null && bodyBytes.Length <= _configuration.MaxBodyCaptureBytes)
                {
                    capturedRequest.Body = bodyBytes;
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not read request body for {Url}", capturedRequest.Url);
            }
        }

        // Store request data and timing on the session's UserData so we can pair it in OnBeforeResponse
        e.UserData = new TrafficCapture
        {
            Request = capturedRequest,
            StartedAt = DateTimeOffset.UtcNow
        };
    }

    private async Task OnBeforeResponse(object sender, SessionEventArgs e)
    {
        CancellationCheck();

        if (e.UserData is not TrafficCapture capture)
            return;

        var capturedResponse = CaptureResponse(e);

        // Read response body
        if (e.HttpClient.Response.HasBody)
        {
            try
            {
                var bodyBytes = await e.GetResponseBody();
                if (bodyBytes is not null && bodyBytes.Length <= _configuration.MaxBodyCaptureBytes)
                {
                    capturedResponse.Body = bodyBytes;
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not read response body for {Url}", capture.Request.Url);
            }
        }

        var completedAt = DateTimeOffset.UtcNow;

        var entry = new TrafficEntry
        {
            Request = capture.Request,
            Response = capturedResponse,
            StartedAt = capture.StartedAt,
            CompletedAt = completedAt
        };

        try
        {
            TrafficCaptured?.Invoke(this, entry);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in TrafficCaptured handler for {Url}", capture.Request.Url);
        }
    }

    private Task OnBeforeTunnelConnectRequest(object sender, TunnelConnectSessionEventArgs e)
    {
        var hostname = e.HttpClient.Request.RequestUri.Host;

        // Allow excluded hostnames to pass through without MITM
        if (_configuration.ExcludedHostnames.Count > 0 &&
            _configuration.ExcludedHostnames.Any(h =>
                hostname.Equals(h, StringComparison.OrdinalIgnoreCase) ||
                hostname.EndsWith("." + h, StringComparison.OrdinalIgnoreCase)))
        {
            e.DecryptSsl = false;
        }

        return Task.CompletedTask;
    }

    private static Task OnServerCertificateValidation(
        object sender,
        CertificateValidationEventArgs e)
    {
        // Accept all upstream certificates — we're a debugging proxy, not enforcing security
        e.IsValid = true;
        return Task.CompletedTask;
    }

    // ── Capture helpers ──────────────────────────────────────────────

    private static CapturedRequest CaptureRequest(SessionEventArgs e)
    {
        var request = e.HttpClient.Request;
        var uri = request.RequestUri;

        return new CapturedRequest
        {
            Method = request.Method,
            Url = request.Url,
            Hostname = uri.Host,
            Path = uri.AbsolutePath,
            QueryString = string.IsNullOrEmpty(uri.Query) ? null : uri.Query,
            Scheme = uri.Scheme,
            Port = uri.Port,
            Headers = ConvertHeaders(request.Headers),
            ContentType = request.ContentType,
            ContentLength = request.ContentLength
        };
    }

    private static CapturedResponse CaptureResponse(SessionEventArgs e)
    {
        var response = e.HttpClient.Response;

        return new CapturedResponse
        {
            StatusCode = response.StatusCode,
            ReasonPhrase = response.StatusDescription,
            Headers = ConvertHeaders(response.Headers),
            ContentType = response.ContentType,
            ContentLength = response.ContentLength
        };
    }

    // Converts Titanium's HeaderCollection to the dictionary format our models expect.
    // HTTP allows multiple values per header name, so we group them.
    private static Dictionary<string, string[]> ConvertHeaders(HeaderCollection headers)
    {
        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in headers.GetAllHeaders())
        {
            if (result.TryGetValue(header.Name, out var existing))
            {
                var newArr = new string[existing.Length + 1];
                existing.CopyTo(newArr, 0);
                newArr[existing.Length] = header.Value;
                result[header.Name] = newArr;
            }
            else
            {
                result[header.Name] = [header.Value];
            }
        }

        return result;
    }

    private static void CancellationCheck()
    {
        // Titanium.Web.Proxy doesn't pass CancellationTokens to handlers,
        // so we just let the proxy's own lifecycle handle cancellation.
    }

    // ── Internal capture context ─────────────────────────────────────

    private sealed class TrafficCapture
    {
        public required CapturedRequest Request { get; init; }
        public required DateTimeOffset StartedAt { get; init; }
    }
}
