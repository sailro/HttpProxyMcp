using FluentAssertions;
using HttpProxyMcp.Core.Interfaces;
using HttpProxyMcp.Core.Models;
using HttpProxyMcp.McpServer;
using HttpProxyMcp.Tests.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace HttpProxyMcp.Tests;

// Tests for ProxyHostedService: store initialization, event wiring,
// session assignment, and graceful shutdown behavior.
public class ProxyHostedServiceTests : IAsyncDisposable
{
    private readonly ITrafficStore _store;
    private readonly ISessionManager _sessionManager;
    private readonly IProxyEngine _proxyEngine;
    private readonly ILogger<ProxyHostedService> _logger;
    private readonly ProxyHostedService _service;

    public ProxyHostedServiceTests()
    {
        _store = Substitute.For<ITrafficStore>();
        _sessionManager = Substitute.For<ISessionManager>();
        _proxyEngine = Substitute.For<IProxyEngine>();
        _logger = Substitute.For<ILogger<ProxyHostedService>>();
        _service = new ProxyHostedService(_store, _sessionManager, _proxyEngine, _logger);
    }

    public async ValueTask DisposeAsync()
    {
        try { await _service.StopAsync(CancellationToken.None); } catch { }
        _service.Dispose();
    }

    // BackgroundService.ExecuteAsync runs asynchronously; give it time to initialize
    private async Task StartServiceAsync()
    {
        await _service.StartAsync(CancellationToken.None);
        await Task.Delay(100);
    }

    #region ExecuteAsync

    [Fact]
    public async Task ExecuteAsync_InitializesStore()
    {
        await StartServiceAsync();

        await _store.Received(1).InitializeAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WiresTrafficCapturedEvent()
    {
        var sessionId = Guid.NewGuid();
        _sessionManager.ActiveSessionId.Returns(sessionId);

        await StartServiceAsync();

        var entry = TrafficEntryBuilder.Get().Build();
        _proxyEngine.TrafficCaptured += Raise.Event<EventHandler<TrafficEntry>>(_proxyEngine, entry);

        await Task.Delay(100);

        await _store.Received(1).SaveTrafficEntryAsync(
            Arg.Is<TrafficEntry>(e => e.SessionId == sessionId),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region OnTrafficCaptured

    [Fact]
    public async Task OnTrafficCaptured_WhenNoActiveSession_CreatesDefaultSession()
    {
        var newSession = new ProxySession { Id = Guid.NewGuid(), Name = "default" };
        _sessionManager.ActiveSessionId.Returns((Guid?)null);
        _sessionManager.CreateSessionAsync("default", Arg.Any<CancellationToken>())
            .Returns(newSession);

        await StartServiceAsync();

        var entry = TrafficEntryBuilder.Get().Build();
        _proxyEngine.TrafficCaptured += Raise.Event<EventHandler<TrafficEntry>>(_proxyEngine, entry);

        await Task.Delay(100);

        await _sessionManager.Received(1).CreateSessionAsync("default", Arg.Any<CancellationToken>());
        await _store.Received(1).SaveTrafficEntryAsync(
            Arg.Is<TrafficEntry>(e => e.SessionId == newSession.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnTrafficCaptured_WithActiveSession_AssignsSessionIdAndSaves()
    {
        var sessionId = Guid.NewGuid();
        _sessionManager.ActiveSessionId.Returns(sessionId);

        await StartServiceAsync();

        var entry = TrafficEntryBuilder.Get().Build();
        _proxyEngine.TrafficCaptured += Raise.Event<EventHandler<TrafficEntry>>(_proxyEngine, entry);

        await Task.Delay(100);

        entry.SessionId.Should().Be(sessionId);
        await _store.Received(1).SaveTrafficEntryAsync(entry, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnTrafficCaptured_WithActiveSession_DoesNotCreateNewSession()
    {
        _sessionManager.ActiveSessionId.Returns(Guid.NewGuid());

        await StartServiceAsync();

        var entry = TrafficEntryBuilder.Get().Build();
        _proxyEngine.TrafficCaptured += Raise.Event<EventHandler<TrafficEntry>>(_proxyEngine, entry);

        await Task.Delay(100);

        await _sessionManager.DidNotReceive().CreateSessionAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnTrafficCaptured_WhenSaveThrows_DoesNotCrashService()
    {
        _sessionManager.ActiveSessionId.Returns(Guid.NewGuid());
        _store.SaveTrafficEntryAsync(Arg.Any<TrafficEntry>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("db error"));

        await StartServiceAsync();

        var entry = TrafficEntryBuilder.Get().Build();
        _proxyEngine.TrafficCaptured += Raise.Event<EventHandler<TrafficEntry>>(_proxyEngine, entry);

        await Task.Delay(100);

        // Service survived the error — verify the save was attempted
        await _store.Received(1).SaveTrafficEntryAsync(entry, Arg.Any<CancellationToken>());
    }

    #endregion

    #region StopAsync

    [Fact]
    public async Task StopAsync_WhenProxyRunning_StopsProxyEngine()
    {
        _proxyEngine.IsRunning.Returns(true);

        await StartServiceAsync();
        await _service.StopAsync(CancellationToken.None);

        await _proxyEngine.Received(1).StopAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopAsync_WhenProxyNotRunning_DoesNotStopEngine()
    {
        _proxyEngine.IsRunning.Returns(false);

        await StartServiceAsync();
        await _service.StopAsync(CancellationToken.None);

        await _proxyEngine.DidNotReceive().StopAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopAsync_WithActiveSession_ClosesSession()
    {
        var sessionId = Guid.NewGuid();
        _sessionManager.ActiveSessionId.Returns(sessionId);

        await StartServiceAsync();
        await _service.StopAsync(CancellationToken.None);

        await _sessionManager.Received(1).CloseSessionAsync(sessionId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopAsync_WithNoActiveSession_DoesNotCloseSession()
    {
        _sessionManager.ActiveSessionId.Returns((Guid?)null);

        await StartServiceAsync();
        await _service.StopAsync(CancellationToken.None);

        await _sessionManager.DidNotReceive().CloseSessionAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopAsync_WhenCloseSessionThrows_DoesNotThrow()
    {
        var sessionId = Guid.NewGuid();
        _sessionManager.ActiveSessionId.Returns(sessionId);
        _sessionManager.CloseSessionAsync(sessionId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("already closed"));

        await StartServiceAsync();

        var act = async () => await _service.StopAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StopAsync_UnwiresTrafficCapturedEvent()
    {
        _sessionManager.ActiveSessionId.Returns(Guid.NewGuid());

        await StartServiceAsync();
        await _service.StopAsync(CancellationToken.None);

        // Raise event after stop — handler was removed, so save should not be called
        var entry = TrafficEntryBuilder.Get().Build();
        _proxyEngine.TrafficCaptured += Raise.Event<EventHandler<TrafficEntry>>(_proxyEngine, entry);

        await Task.Delay(100);

        await _store.DidNotReceive().SaveTrafficEntryAsync(
            Arg.Any<TrafficEntry>(), Arg.Any<CancellationToken>());
    }

    #endregion
}
