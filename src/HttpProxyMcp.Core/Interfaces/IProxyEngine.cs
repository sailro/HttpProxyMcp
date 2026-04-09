using HttpProxyMcp.Core.Models;

namespace HttpProxyMcp.Core.Interfaces;

// Controls the HTTP/HTTPS MITM proxy engine lifecycle.
public interface IProxyEngine
{
	bool IsRunning { get; }
	ProxyConfiguration Configuration { get; }

	Task StartAsync(ProxyConfiguration configuration, CancellationToken cancellationToken = default);
	Task StopAsync(CancellationToken cancellationToken = default);

	// Raised when a complete request/response pair has been captured.
	event EventHandler<TrafficEntry> TrafficCaptured;
}
