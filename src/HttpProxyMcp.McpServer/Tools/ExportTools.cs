using System.ComponentModel;
using System.Text;
using HttpProxyMcp.Core.Interfaces;
using HttpProxyMcp.Core.Models;
using ModelContextProtocol.Server;

namespace HttpProxyMcp.McpServer.Tools;

// MCP tools for exporting captured traffic data.
[McpServerToolType]
public sealed class ExportTools
{
    [McpServerTool, Description("Export a capture session to HAR 1.2 format")]
    public static async Task<string> ExportHar(
        ITrafficStore store,
        [Description("Session ID (GUID) to export")] string sessionId,
        [Description("Output file path for the .har file")] string filePath)
    {
        var sid = Guid.Parse(sessionId);

        // Get total count so we can load all entries
        var filter = new TrafficFilter { SessionId = sid, Limit = 1 };
        var totalCount = await store.CountTrafficAsync(filter);

        if (totalCount == 0)
            return $"No traffic entries found for session {sessionId}.";

        // Load entry IDs (list query excludes bodies for performance)
        var listFilter = new TrafficFilter { SessionId = sid, Limit = totalCount };
        var summaries = await store.QueryTrafficAsync(listFilter);

        // Load full entries with bodies for HAR export
        var entries = new List<TrafficEntry>(summaries.Count);
        foreach (var summary in summaries)
        {
            var full = await store.GetTrafficEntryAsync(summary.Id);
            if (full is not null)
                entries.Add(full);
        }

        var harJson = HarConverter.ConvertToHarJson(entries);
        var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        await File.WriteAllTextAsync(filePath, harJson, utf8NoBom);

        return $"Exported {entries.Count} entries to {filePath}";
    }
}
