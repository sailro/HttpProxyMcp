namespace HttpProxyMcp.Core.Models;

// Aggregated traffic statistics.
public sealed class TrafficStatistics
{
    public int TotalRequests { get; set; }
    public Dictionary<string, int> RequestsByMethod { get; set; } = [];
    public Dictionary<int, int> RequestsByStatusCode { get; set; } = [];
    public Dictionary<string, int> RequestsByHostname { get; set; } = [];
    public long TotalRequestBytes { get; set; }
    public long TotalResponseBytes { get; set; }
    public double? AverageDurationMs { get; set; }
    public DateTimeOffset? EarliestRequest { get; set; }
    public DateTimeOffset? LatestRequest { get; set; }
}
