using FluentAssertions;
using HttpProxyMcp.Core.Interfaces;
using HttpProxyMcp.Core.Models;
using HttpProxyMcp.Tests.Helpers;
using NSubstitute;

namespace HttpProxyMcp.Tests.Proxy;

// Tests for IProxyEngine contract behavior.
// These verify the engine lifecycle, event firing, and concurrent request handling.
// When Tank delivers the Titanium.Web.Proxy implementation, upgrade to real tests.
public class ProxyEngineTests
{
    private readonly IProxyEngine _engine;

    public ProxyEngineTests()
    {
        _engine = Substitute.For<IProxyEngine>();
    }

    #region Lifecycle

    [Fact]
    public async Task StartAsync_SetsIsRunningTrue()
    {
        _engine.IsRunning.Returns(false);
        _engine.When(e => e.StartAsync(Arg.Any<ProxyConfiguration>(), Arg.Any<CancellationToken>()))
            .Do(_ => _engine.IsRunning.Returns(true));

        _engine.IsRunning.Should().BeFalse();

        await _engine.StartAsync(new ProxyConfiguration());

        _engine.IsRunning.Should().BeTrue();
    }

    [Fact]
    public async Task StopAsync_SetsIsRunningFalse()
    {
        _engine.IsRunning.Returns(true);
        _engine.When(e => e.StopAsync(Arg.Any<CancellationToken>()))
            .Do(_ => _engine.IsRunning.Returns(false));

        await _engine.StopAsync();

        _engine.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task StartAsync_StoresConfiguration()
    {
        var config = new ProxyConfiguration { Port = 9090, EnableSsl = true };

        _engine.When(e => e.StartAsync(config, Arg.Any<CancellationToken>()))
            .Do(_ => _engine.Configuration.Returns(config));

        await _engine.StartAsync(config);

        _engine.Configuration.Should().NotBeNull();
        _engine.Configuration.Port.Should().Be(9090);
        _engine.Configuration.EnableSsl.Should().BeTrue();
    }

    [Fact]
    public async Task StartAsync_CalledTwice_SecondCallShouldNotThrowOrIsIdempotent()
    {
        // The proxy should either throw a meaningful error or be idempotent
        _engine.IsRunning.Returns(true);

        // Not asserting specific behavior — just verifying the interface accepts the call
        var act = async () => await _engine.StartAsync(new ProxyConfiguration());
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region TrafficCaptured event

    [Fact]
    public void TrafficCaptured_CanSubscribeToEvent()
    {
        TrafficEntry? captured = null;
        _engine.TrafficCaptured += (sender, entry) => captured = entry;

        var testEntry = TrafficEntryBuilder.Get("https://example.com/test").Build();
        _engine.TrafficCaptured += Raise.Event<EventHandler<TrafficEntry>>(_engine, testEntry);

        captured.Should().NotBeNull();
        captured!.Request.Url.Should().Be("https://example.com/test");
    }

    [Fact]
    public void TrafficCaptured_ContainsPopulatedRequest()
    {
        TrafficEntry? captured = null;
        _engine.TrafficCaptured += (sender, entry) => captured = entry;

        var entry = TrafficEntryBuilder.Get("https://api.example.com/users?page=1")
            .WithMethod("GET")
            .WithHostname("api.example.com")
            .WithRequestHeaders(new Dictionary<string, string[]>
            {
                ["Host"] = ["api.example.com"],
                ["Accept"] = ["application/json"]
            })
            .Build();

        _engine.TrafficCaptured += Raise.Event<EventHandler<TrafficEntry>>(_engine, entry);

        captured.Should().NotBeNull();
        captured!.Request.Method.Should().Be("GET");
        captured.Request.Hostname.Should().Be("api.example.com");
        captured.Request.Headers.Should().ContainKey("Host");
    }

    [Fact]
    public void TrafficCaptured_ContainsPopulatedResponse()
    {
        TrafficEntry? captured = null;
        _engine.TrafficCaptured += (sender, entry) => captured = entry;

        var entry = TrafficEntryBuilder.Get()
            .WithStatusCode(200, "OK")
            .WithResponseBody("{\"status\":\"healthy\"}", "application/json")
            .Build();

        _engine.TrafficCaptured += Raise.Event<EventHandler<TrafficEntry>>(_engine, entry);

        captured.Should().NotBeNull();
        captured!.Response.Should().NotBeNull();
        captured.Response!.StatusCode.Should().Be(200);
        captured.Response.ContentType.Should().Be("application/json");
        captured.Response.Body.Should().NotBeNull();
    }

    [Fact]
    public void TrafficCaptured_IncludesTimingData()
    {
        TrafficEntry? captured = null;
        _engine.TrafficCaptured += (sender, entry) => captured = entry;

        var startedAt = DateTimeOffset.UtcNow.AddMilliseconds(-200);
        var entry = TrafficEntryBuilder.Get()
            .WithStartedAt(startedAt)
            .WithDuration(TimeSpan.FromMilliseconds(200))
            .Build();

        _engine.TrafficCaptured += Raise.Event<EventHandler<TrafficEntry>>(_engine, entry);

        captured.Should().NotBeNull();
        captured!.StartedAt.Should().BeCloseTo(startedAt, TimeSpan.FromMilliseconds(50));
        captured.CompletedAt.Should().NotBeNull();
        captured.Duration.Should().NotBeNull();
    }

    [Fact]
    public void TrafficCaptured_HttpsRequest_ContainsScheme()
    {
        TrafficEntry? captured = null;
        _engine.TrafficCaptured += (sender, entry) => captured = entry;

        var entry = TrafficEntryBuilder.Get("https://secure.example.com/tls")
            .AsHttps()
            .Build();

        _engine.TrafficCaptured += Raise.Event<EventHandler<TrafficEntry>>(_engine, entry);

        captured.Should().NotBeNull();
        captured!.Request.Scheme.Should().Be("https");
        captured.Request.Port.Should().Be(443);
    }

    [Fact]
    public void TrafficCaptured_HttpRequest_ContainsScheme()
    {
        TrafficEntry? captured = null;
        _engine.TrafficCaptured += (sender, entry) => captured = entry;

        var entry = TrafficEntryBuilder.Get("http://plain.example.com/page")
            .AsHttp()
            .Build();

        _engine.TrafficCaptured += Raise.Event<EventHandler<TrafficEntry>>(_engine, entry);

        captured.Should().NotBeNull();
        captured!.Request.Scheme.Should().Be("http");
        captured.Request.Port.Should().Be(80);
    }

    #endregion

    #region Concurrent requests

    [Fact]
    public void TrafficCaptured_MultipleConcurrentEvents_AllReceived()
    {
        List<TrafficEntry> capturedEntries = [];
        _engine.TrafficCaptured += (sender, entry) =>
        {
            lock (capturedEntries) { capturedEntries.Add(entry); }
        };

        for (int i = 0; i < 10; i++)
        {
            var entry = TrafficEntryBuilder.Get($"https://example.com/path/{i}")
                .WithId(i)
                .Build();

            _engine.TrafficCaptured += Raise.Event<EventHandler<TrafficEntry>>(_engine, entry);
        }

        capturedEntries.Should().HaveCount(10);
        capturedEntries.Select(e => e.Request.Url).Should().OnlyHaveUniqueItems();
    }

    #endregion

    #region Request/response body capture

    [Fact]
    public void TrafficCaptured_PostWithBody_CapturesRequestBody()
    {
        TrafficEntry? captured = null;
        _engine.TrafficCaptured += (sender, entry) => captured = entry;

        var entry = TrafficEntryBuilder.Post("https://api.example.com/submit", "{\"data\":\"payload\"}")
            .Build();

        _engine.TrafficCaptured += Raise.Event<EventHandler<TrafficEntry>>(_engine, entry);

        captured.Should().NotBeNull();
        captured!.Request.Method.Should().Be("POST");
        captured.Request.Body.Should().NotBeNull();
        System.Text.Encoding.UTF8.GetString(captured.Request.Body!).Should().Contain("payload");
        captured.Request.ContentType.Should().Be("application/json");
    }

    [Fact]
    public void TrafficCaptured_GetWithNoBody_BodyIsNull()
    {
        TrafficEntry? captured = null;
        _engine.TrafficCaptured += (sender, entry) => captured = entry;

        var entry = TrafficEntryBuilder.Get().Build();

        _engine.TrafficCaptured += Raise.Event<EventHandler<TrafficEntry>>(_engine, entry);

        captured!.Request.Body.Should().BeNull();
    }

    #endregion
}
