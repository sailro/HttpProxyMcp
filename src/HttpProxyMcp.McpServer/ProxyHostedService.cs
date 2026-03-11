using HttpProxyMcp.Core.Interfaces;
using HttpProxyMcp.Core.Models;

namespace HttpProxyMcp.McpServer;

// Hosted service that initializes storage, wires proxy traffic capture to the store,
// and manages the proxy lifecycle.
public sealed class ProxyHostedService(
    ITrafficStore store,
    ISessionManager sessionManager,
    IProxyEngine proxyEngine,
    ILogger<ProxyHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Initializing traffic store...");
        await store.InitializeAsync(stoppingToken);

        // Wire proxy capture events to the storage layer
        proxyEngine.TrafficCaptured += OnTrafficCaptured;

        logger.LogInformation("HttpProxyMcp server ready. Proxy can be started via MCP tools.");

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        proxyEngine.TrafficCaptured -= OnTrafficCaptured;

        if (proxyEngine.IsRunning)
        {
            logger.LogInformation("Stopping proxy engine...");
            await proxyEngine.StopAsync(cancellationToken);
        }

        // Close the active session so it doesn't stay "active" across restarts
        var activeId = sessionManager.ActiveSessionId;
        if (activeId is not null)
        {
            try
            {
                await sessionManager.CloseSessionAsync(activeId.Value, cancellationToken);
                logger.LogInformation("Closed active session {SessionId}", activeId.Value);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not close session {SessionId} on shutdown", activeId.Value);
            }
        }

        await base.StopAsync(cancellationToken);
    }

    private async void OnTrafficCaptured(object? sender, TrafficEntry entry)
    {
        try
        {
            // Assign the entry to the active session; if none exists, create a default one
            var activeId = sessionManager.ActiveSessionId;
            if (activeId is null)
            {
                var session = await sessionManager.CreateSessionAsync("default");
                activeId = session.Id;
            }

            entry.SessionId = activeId.Value;
            await store.SaveTrafficEntryAsync(entry);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save captured traffic for {Url}", entry.Request.Url);
        }
    }
}
