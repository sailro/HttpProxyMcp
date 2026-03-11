using System.ComponentModel;
using HttpProxyMcp.Core.Interfaces;
using HttpProxyMcp.Core.Models;
using ModelContextProtocol.Server;

namespace HttpProxyMcp.McpServer.Tools;

// MCP tools for controlling the proxy engine and managing sessions.
[McpServerToolType]
public sealed class ProxyControlTools
{
    [McpServerTool, Description("Start the HTTP/HTTPS proxy on the specified port")]
    public static async Task<string> StartProxy(
        IProxyEngine engine,
        [Description("Port to listen on (default: 8080)")] int port = 8080,
        [Description("Enable HTTPS MITM interception")] bool enableSsl = true,
        [Description("Auto-configure Windows system proxy (default: true)")] bool setSystemProxy = true)
    {
        if (engine.IsRunning)
            return $"Proxy is already running on port {engine.Configuration.Port}.";

        var config = new ProxyConfiguration { Port = port, EnableSsl = enableSsl, SetSystemProxy = setSystemProxy };

        try
        {
            await engine.StartAsync(config);
        }
        catch (Exception ex)
        {
            return $"Failed to start proxy on port {port}: {ex.Message}";
        }

        var sysProxyMsg = setSystemProxy ? " System proxy configured." : "";
        return $"Proxy started on port {port} (SSL interception: {enableSsl}).{sysProxyMsg}";
    }

    [McpServerTool, Description("Stop the running proxy")]
    public static async Task<string> StopProxy(IProxyEngine engine)
    {
        if (!engine.IsRunning)
            return "Proxy is not running.";

        await engine.StopAsync();
        return "Proxy stopped.";
    }

    [McpServerTool, Description("Get current proxy status")]
    public static Task<string> GetProxyStatus(IProxyEngine engine)
    {
        return Task.FromResult(engine.IsRunning
            ? $"Proxy is running on port {engine.Configuration.Port} (SSL: {engine.Configuration.EnableSsl})."
            : "Proxy is not running.");
    }
}
