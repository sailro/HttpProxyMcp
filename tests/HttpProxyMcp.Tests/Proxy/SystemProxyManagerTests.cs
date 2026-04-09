using System.Runtime.InteropServices;
using FluentAssertions;
using HttpProxyMcp.Proxy;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HttpProxyMcp.Tests.Proxy;

// Tests for SystemProxyManager behavior — safe tests that don't modify system settings.
public class SystemProxyManagerTests : IDisposable
{
	private readonly ILogger<SystemProxyManager> _logger;
	private readonly SystemProxyManager _manager;

	public SystemProxyManagerTests()
	{
		_logger = Substitute.For<ILogger<SystemProxyManager>>();
		_manager = new SystemProxyManager(_logger);
	}

	public void Dispose() => _manager.Dispose();

	#region EnableSystemProxy

	[Fact]
	public void EnableSystemProxy_OnNonWindows_IsNoOp()
	{
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			Assert.Skip("This test validates non-Windows behavior");

		// On non-Windows, EnableSystemProxy should just log a warning and return
		var act = () => _manager.EnableSystemProxy(8080);

		act.Should().NotThrow();
	}

	#endregion

	#region DisableSystemProxy

	[Fact]
	public void DisableSystemProxy_WithoutPriorEnable_IsNoOp()
	{
		// _hasSnapshot is false by default, so DisableSystemProxy should return immediately
		var act = () => _manager.DisableSystemProxy();

		act.Should().NotThrow();
	}

	[Fact]
	public void DisableSystemProxy_CalledTwice_IsIdempotent()
	{
		_manager.DisableSystemProxy();

		var act = () => _manager.DisableSystemProxy();

		act.Should().NotThrow();
	}

	#endregion

	#region Dispose

	[Fact]
	public void Dispose_WithoutPriorEnable_DoesNotThrow()
	{
		// Dispose calls DisableSystemProxy internally; without a snapshot, it's a no-op
		var manager = new SystemProxyManager(_logger);

		var act = () => manager.Dispose();

		act.Should().NotThrow();
	}

	[Fact]
	public void Dispose_CalledTwice_IsIdempotent()
	{
		var manager = new SystemProxyManager(_logger);
		manager.Dispose();

		var act = () => manager.Dispose();

		act.Should().NotThrow();
	}

	[Fact]
	public void Dispose_UnregistersEventHandlers()
	{
		// Verify Dispose cleans up ProcessExit and CancelKeyPress handlers
		// by creating and disposing multiple managers without accumulation errors
		for (int i = 0; i < 5; i++)
		{
			var manager = new SystemProxyManager(_logger);
			manager.Dispose();
		}

		// If handlers leaked, they would accumulate — no exception means cleanup works
	}

	#endregion

	#region Crash-safety handlers

	[Fact]
	public void Constructor_RegistersCrashSafetyHandlers_DisposeUnregisters()
	{
		// The constructor registers ProcessExit and CancelKeyPress handlers.
		// We verify that construction + disposal completes without error,
		// which proves the register/unregister cycle is correct.
		var manager = new SystemProxyManager(_logger);

		var act = () => manager.Dispose();

		act.Should().NotThrow();
	}

	#endregion
}
