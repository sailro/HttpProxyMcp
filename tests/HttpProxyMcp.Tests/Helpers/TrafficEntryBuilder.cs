using System.Text;
using HttpProxyMcp.Core.Models;

namespace HttpProxyMcp.Tests.Helpers;

// Fluent builder for creating test TrafficEntry objects with sensible defaults.
public sealed class TrafficEntryBuilder
{
    private long _id;
    private Guid _sessionId = Guid.NewGuid();
    private string _method = "GET";
    private string _url = "https://example.com/api/data";
    private string _hostname = "example.com";
    private string _path = "/api/data";
    private string? _queryString;
    private string _scheme = "https";
    private int _port = 443;
    private Dictionary<string, string[]> _requestHeaders = new()
    {
        ["Host"] = ["example.com"],
        ["Accept"] = ["application/json"]
    };
    private byte[]? _requestBody;
    private string? _requestContentType;
    private int _statusCode = 200;
    private string _reasonPhrase = "OK";
    private Dictionary<string, string[]> _responseHeaders = new()
    {
        ["Content-Type"] = ["application/json"]
    };
    private byte[]? _responseBody;
    private string? _responseContentType = "application/json";
    private DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private DateTimeOffset? _completedAt;
    private TimeSpan _duration = TimeSpan.FromMilliseconds(150);

    public TrafficEntryBuilder WithId(long id) { _id = id; return this; }
    public TrafficEntryBuilder WithSessionId(Guid sessionId) { _sessionId = sessionId; return this; }
    public TrafficEntryBuilder WithMethod(string method) { _method = method; return this; }
    public TrafficEntryBuilder WithUrl(string url) { _url = url; return this; }
    public TrafficEntryBuilder WithHostname(string hostname) { _hostname = hostname; return this; }
    public TrafficEntryBuilder WithPath(string path) { _path = path; return this; }
    public TrafficEntryBuilder WithQueryString(string? qs) { _queryString = qs; return this; }
    public TrafficEntryBuilder WithScheme(string scheme) { _scheme = scheme; return this; }
    public TrafficEntryBuilder WithPort(int port) { _port = port; return this; }

    public TrafficEntryBuilder WithRequestHeaders(Dictionary<string, string[]> headers)
    {
        _requestHeaders = headers;
        return this;
    }

    public TrafficEntryBuilder WithRequestBody(string body, string contentType = "application/json")
    {
        _requestBody = Encoding.UTF8.GetBytes(body);
        _requestContentType = contentType;
        return this;
    }

    public TrafficEntryBuilder WithRequestBodyBytes(byte[] body, string contentType)
    {
        _requestBody = body;
        _requestContentType = contentType;
        return this;
    }

    public TrafficEntryBuilder WithStatusCode(int statusCode, string reasonPhrase = "OK")
    {
        _statusCode = statusCode;
        _reasonPhrase = reasonPhrase;
        return this;
    }

    public TrafficEntryBuilder WithResponseHeaders(Dictionary<string, string[]> headers)
    {
        _responseHeaders = headers;
        return this;
    }

    public TrafficEntryBuilder WithResponseBody(string body, string contentType = "application/json")
    {
        _responseBody = Encoding.UTF8.GetBytes(body);
        _responseContentType = contentType;
        return this;
    }

    public TrafficEntryBuilder WithResponseBodyBytes(byte[] body, string contentType)
    {
        _responseBody = body;
        _responseContentType = contentType;
        return this;
    }

    public TrafficEntryBuilder WithStartedAt(DateTimeOffset startedAt) { _startedAt = startedAt; return this; }
    public TrafficEntryBuilder WithDuration(TimeSpan duration) { _duration = duration; return this; }

    public TrafficEntryBuilder WithCompletedAt(DateTimeOffset? completedAt)
    {
        _completedAt = completedAt;
        return this;
    }

    public TrafficEntryBuilder WithNoResponse()
    {
        _completedAt = null;
        return this;
    }

    public TrafficEntryBuilder AsPost(string body = "{\"key\":\"value\"}")
    {
        _method = "POST";
        _requestBody = Encoding.UTF8.GetBytes(body);
        _requestContentType = "application/json";
        return this;
    }

    public TrafficEntryBuilder AsHttps()
    {
        _scheme = "https";
        _port = 443;
        return this;
    }

    public TrafficEntryBuilder AsHttp()
    {
        _scheme = "http";
        _port = 80;
        return this;
    }

    public TrafficEntry Build()
    {
        var completedAt = _completedAt ?? _startedAt.Add(_duration);

        return new TrafficEntry
        {
            Id = _id,
            SessionId = _sessionId,
            StartedAt = _startedAt,
            CompletedAt = completedAt,
            Request = new CapturedRequest
            {
                Method = _method,
                Url = _url,
                Hostname = _hostname,
                Path = _path,
                QueryString = _queryString,
                Scheme = _scheme,
                Port = _port,
                Headers = _requestHeaders,
                Body = _requestBody,
                ContentType = _requestContentType,
                ContentLength = _requestBody?.Length
            },
            Response = new CapturedResponse
            {
                StatusCode = _statusCode,
                ReasonPhrase = _reasonPhrase,
                Headers = _responseHeaders,
                Body = _responseBody,
                ContentType = _responseContentType,
                ContentLength = _responseBody?.Length
            }
        };
    }

    // Creates a standard GET request to a given URL.
    public static TrafficEntryBuilder Get(string url = "https://example.com/api/data")
    {
        var uri = new Uri(url);
        return new TrafficEntryBuilder()
            .WithMethod("GET")
            .WithUrl(url)
            .WithHostname(uri.Host)
            .WithPath(uri.AbsolutePath)
            .WithQueryString(string.IsNullOrEmpty(uri.Query) ? null : uri.Query)
            .WithScheme(uri.Scheme)
            .WithPort(uri.Port);
    }

    // Creates a standard POST request to a given URL with a JSON body.
    public static TrafficEntryBuilder Post(string url = "https://example.com/api/data", string body = "{\"key\":\"value\"}")
    {
        var uri = new Uri(url);
        return new TrafficEntryBuilder()
            .WithMethod("POST")
            .WithUrl(url)
            .WithHostname(uri.Host)
            .WithPath(uri.AbsolutePath)
            .WithScheme(uri.Scheme)
            .WithPort(uri.Port)
            .WithRequestBody(body);
    }
}
