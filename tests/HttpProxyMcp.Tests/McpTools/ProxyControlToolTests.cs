using FluentAssertions;
using HttpProxyMcp.Core.Interfaces;
using HttpProxyMcp.Core.Models;
using HttpProxyMcp.McpServer.Tools;
using NSubstitute;

namespace HttpProxyMcp.Tests.McpTools;

// Tests for ProxyControlTools MCP tool methods.
public class ProxyControlToolTests
{
    private readonly IProxyEngine _engine;

    public ProxyControlToolTests()
    {
        _engine = Substitute.For<IProxyEngine>();
    }

    #region StartProxy

    [Fact]
    public async Task StartProxy_WhenNotRunning_StartsAndReturnsConfirmation()
    {
        _engine.IsRunning.Returns(false);

        var result = await ProxyControlTools.StartProxy(_engine);

        result.Should().Contain("started");
        result.Should().Contain("8080");
        await _engine.Received(1).StartAsync(
            Arg.Is<ProxyConfiguration>(c => c.Port == 8080 && c.EnableSsl),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartProxy_WithCustomPort_PassesPortToEngine()
    {
        _engine.IsRunning.Returns(false);

        var result = await ProxyControlTools.StartProxy(_engine, port: 9090);

        result.Should().Contain("9090");
        await _engine.Received(1).StartAsync(
            Arg.Is<ProxyConfiguration>(c => c.Port == 9090),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartProxy_WithSslDisabled_PassesConfigToEngine()
    {
        _engine.IsRunning.Returns(false);

        var result = await ProxyControlTools.StartProxy(_engine, enableSsl: false);

        result.Should().Contain("False");
        await _engine.Received(1).StartAsync(
            Arg.Is<ProxyConfiguration>(c => !c.EnableSsl),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartProxy_WhenEngineThrows_ReturnsFriendlyErrorMessage()
    {
        _engine.IsRunning.Returns(false);
        _engine.StartAsync(Arg.Any<ProxyConfiguration>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("Could not bind to port 8080: Address already in use")));

        var result = await ProxyControlTools.StartProxy(_engine, port: 8080);

        result.Should().Contain("Failed to start proxy on port 8080");
        result.Should().Contain("Address already in use");
    }

    [Fact]
    public async Task StartProxy_WhenAlreadyRunning_ReturnsAlreadyRunningMessage()
    {
        _engine.IsRunning.Returns(true);
        _engine.Configuration.Returns(new ProxyConfiguration { Port = 8080 });

        var result = await ProxyControlTools.StartProxy(_engine);

        result.Should().Contain("already running");
        await _engine.DidNotReceive().StartAsync(
            Arg.Any<ProxyConfiguration>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region StopProxy

    [Fact]
    public async Task StopProxy_WhenRunning_StopsAndReturnsConfirmation()
    {
        _engine.IsRunning.Returns(true);

        var result = await ProxyControlTools.StopProxy(_engine);

        result.Should().Contain("stopped");
        await _engine.Received(1).StopAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopProxy_WhenNotRunning_ReturnsNotRunningMessage()
    {
        _engine.IsRunning.Returns(false);

        var result = await ProxyControlTools.StopProxy(_engine);

        result.Should().Contain("not running");
        await _engine.DidNotReceive().StopAsync(Arg.Any<CancellationToken>());
    }

    #endregion

    #region GetProxyStatus

    [Fact]
    public async Task GetProxyStatus_WhenRunning_ReturnsRunningInfo()
    {
        _engine.IsRunning.Returns(true);
        _engine.Configuration.Returns(new ProxyConfiguration { Port = 8080, EnableSsl = true });

        var result = await ProxyControlTools.GetProxyStatus(_engine);

        result.Should().Contain("running");
        result.Should().Contain("8080");
    }

    [Fact]
    public async Task GetProxyStatus_WhenNotRunning_ReturnsNotRunning()
    {
        _engine.IsRunning.Returns(false);

        var result = await ProxyControlTools.GetProxyStatus(_engine);

        result.Should().Contain("not running");
    }

    #endregion
}
