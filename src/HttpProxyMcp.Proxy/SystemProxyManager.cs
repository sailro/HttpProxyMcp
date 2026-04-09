using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace HttpProxyMcp.Proxy;

// Manages the Windows system proxy settings (Internet Settings registry keys).
// Saves original values on enable and restores them on disable.
// Registers process-exit handlers to ensure cleanup even on ungraceful termination.
public sealed partial class SystemProxyManager : IDisposable
{
	private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

	private readonly ILogger<SystemProxyManager> _logger;
	private bool _wasEnabled;
	private string? _previousServer;
	private bool _hasSnapshot;
	private bool _disposed;

	public SystemProxyManager(ILogger<SystemProxyManager> logger)
	{
		_logger = logger;

		// Safety net: restore proxy on any form of process exit
		AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
		Console.CancelKeyPress += OnCancelKeyPress;
	}

	public void EnableSystemProxy(int port)
	{
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			_logger.LogWarning("System proxy auto-configuration is only supported on Windows");
			return;
		}

		try
		{
			using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: true);
			if (key is null)
			{
				_logger.LogWarning("Could not open Internet Settings registry key");
				return;
			}

			// Snapshot current values so we can restore them later
			_wasEnabled = (int)key.GetValue("ProxyEnable", 0) == 1;
			_previousServer = key.GetValue("ProxyServer") as string;
			_hasSnapshot = true;

			var proxyServer = $"localhost:{port}";
			key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
			key.SetValue("ProxyServer", proxyServer, RegistryValueKind.String);

			NotifySettingsChanged();

			_logger.LogInformation("System proxy set to {ProxyServer}", proxyServer);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to set system proxy");
		}
	}

	public void DisableSystemProxy()
	{
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || !_hasSnapshot)
			return;

		try
		{
			using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: true);
			if (key is null) return;

			key.SetValue("ProxyEnable", _wasEnabled ? 1 : 0, RegistryValueKind.DWord);

			if (_previousServer is not null)
				key.SetValue("ProxyServer", _previousServer, RegistryValueKind.String);
			else
				key.DeleteValue("ProxyServer", throwOnMissingValue: false);

			NotifySettingsChanged();

			_hasSnapshot = false;
			_logger.LogInformation("System proxy restored to original settings");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to restore system proxy");
		}
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		DisableSystemProxy();

		AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
		Console.CancelKeyPress -= OnCancelKeyPress;
	}

	private void OnProcessExit(object? sender, EventArgs e) => DisableSystemProxy();

	private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e) => DisableSystemProxy();

	// Tells WinInet to re-read proxy settings without needing a browser restart
	private static void NotifySettingsChanged()
	{
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			return;

		const int internetOptionSettingsChanged = 39;
		const int internetOptionRefresh = 37;

		_ = InternetSetOption(nint.Zero, internetOptionSettingsChanged, nint.Zero, 0);
		_ = InternetSetOption(nint.Zero, internetOptionRefresh, nint.Zero, 0);
	}

	[LibraryImport("wininet.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool InternetSetOption(nint hInternet, int dwOption, nint lpBuffer, int dwBufferLength);
}
