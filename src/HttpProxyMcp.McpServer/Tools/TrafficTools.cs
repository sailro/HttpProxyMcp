using System.ComponentModel;
using HttpProxyMcp.Core.Interfaces;
using HttpProxyMcp.Core.Models;
using ModelContextProtocol.Server;

namespace HttpProxyMcp.McpServer.Tools;

// MCP tools for querying and searching captured proxy traffic.
[McpServerToolType]
public sealed class TrafficTools
{
    [McpServerTool, Description("List captured traffic entries with optional filters")]
    public static async Task<string> ListTraffic(
        ITrafficStore store,
        [Description("Filter by hostname")] string? hostname = null,
        [Description("Filter by HTTP method")] string? method = null,
        [Description("Filter by URL pattern")] string? urlPattern = null,
        [Description("Filter by status code")] int? statusCode = null,
        [Description("Maximum results to return")] int limit = 50,
        [Description("Results offset for paging")] int offset = 0)
    {
        var filter = new TrafficFilter
        {
            Hostname = hostname,
            Method = method,
            UrlPattern = urlPattern,
            StatusCode = statusCode,
            Limit = limit,
            Offset = offset
        };
        var entries = await store.QueryTrafficAsync(filter);
        var count = await store.CountTrafficAsync(filter);

        var lines = entries.Select(e =>
            $"[{e.Id}] {e.Request.Method} {e.Request.Url} → {e.Response?.StatusCode.ToString() ?? "pending"} " +
            $"({e.Duration?.TotalMilliseconds:F0}ms)");

        return $"Showing {entries.Count} of {count} entries:\n" + string.Join("\n", lines);
    }

    [McpServerTool, Description("Get full details of a captured traffic entry by ID")]
    public static async Task<string> GetTrafficEntry(
        ITrafficStore store,
        [Description("Traffic entry ID")] long id)
    {
        var entry = await store.GetTrafficEntryAsync(id);
        if (entry is null) return $"Traffic entry {id} not found.";

        return FormatEntry(entry);
    }

    [McpServerTool, Description("Search request and response bodies for text")]
    public static async Task<string> SearchBodies(
        ITrafficStore store,
        [Description("Text to search for in bodies")] string searchText,
        [Description("Limit results")] int limit = 50)
    {
        var entries = await store.SearchBodiesAsync(searchText, limit: limit);
        var lines = entries.Select(e =>
            $"[{e.Id}] {e.Request.Method} {e.Request.Url} → {e.Response?.StatusCode.ToString() ?? "pending"}");

        return $"Found {entries.Count} entries matching \"{searchText}\":\n" + string.Join("\n", lines);
    }

    [McpServerTool, Description("Get traffic statistics (counts by method, status, host, timing)")]
    public static async Task<string> GetStatistics(
        ITrafficStore store,
        [Description("Optional session ID")] string? sessionId = null)
    {
        var sid = sessionId is not null ? Guid.Parse(sessionId) : (Guid?)null;
        var stats = await store.GetStatisticsAsync(sid);

        return $"""
            Total requests: {stats.TotalRequests}
            Request bytes: {stats.TotalRequestBytes:N0}
            Response bytes: {stats.TotalResponseBytes:N0}
            Avg duration: {stats.AverageDurationMs:F0}ms
            By method: {FormatDict(stats.RequestsByMethod)}
            By status: {FormatDict(stats.RequestsByStatusCode)}
            Top hosts: {FormatDict(stats.RequestsByHostname.OrderByDescending(kv => kv.Value).Take(10))}
            """;
    }

    [McpServerTool, Description("Clear all captured traffic data")]
    public static async Task<string> ClearTraffic(
        ITrafficStore store,
        [Description("Optional session ID to clear")] string? sessionId = null)
    {
        var sid = sessionId is not null ? Guid.Parse(sessionId) : (Guid?)null;
        await store.ClearTrafficAsync(sid);
        return sid.HasValue
            ? $"Cleared traffic for session {sid}."
            : "Cleared all captured traffic.";
    }

    private static string FormatEntry(TrafficEntry entry)
    {
        var reqHeaders = string.Join("\n  ", entry.Request.Headers.Select(h => $"{h.Key}: {string.Join(", ", h.Value)}"));
        var resHeaders = entry.Response is not null
            ? string.Join("\n  ", entry.Response.Headers.Select(h => $"{h.Key}: {string.Join(", ", h.Value)}"))
            : "";

        var reqBody = entry.Request.Body is not null
            ? TryDecodeBody(entry.Request.Body, entry.Request.ContentType)
            : "(no body)";

        var resBody = entry.Response?.Body is not null
            ? TryDecodeBody(entry.Response.Body, entry.Response.ContentType)
            : "(no body)";

        return $"""
            === Traffic Entry {entry.Id} ===
            Started: {entry.StartedAt:O}
            Duration: {entry.Duration?.TotalMilliseconds:F0}ms

            --- Request ---
            {entry.Request.Method} {entry.Request.Url}
            Headers:
              {reqHeaders}
            Body:
            {reqBody}

            --- Response ---
            {entry.Response?.StatusCode} {entry.Response?.ReasonPhrase}
            Headers:
              {resHeaders}
            Body:
            {resBody}
            """;
    }

    private static string TryDecodeBody(byte[] body, string? contentType)
    {
        if (IsTextContent(contentType))
        {
            return System.Text.Encoding.UTF8.GetString(body);
        }
        return $"({body.Length} bytes, binary)";
    }

    private static bool IsTextContent(string? contentType) =>
        contentType is not null && (
            contentType.Contains("text/", StringComparison.OrdinalIgnoreCase) ||
            contentType.Contains("json", StringComparison.OrdinalIgnoreCase) ||
            contentType.Contains("xml", StringComparison.OrdinalIgnoreCase) ||
            contentType.Contains("javascript", StringComparison.OrdinalIgnoreCase) ||
            contentType.Contains("html", StringComparison.OrdinalIgnoreCase));

    private static string FormatDict<TKey, TValue>(IEnumerable<KeyValuePair<TKey, TValue>> dict) =>
        string.Join(", ", dict.Select(kv => $"{kv.Key}={kv.Value}"));
}
