namespace HttpProxyMcp.Storage;

// Flat DTO that maps 1:1 to the traffic_entries table columns.
// Used by Dapper for row mapping — the domain model (TrafficEntry) has nested objects
// that don't map directly to a flat row.
internal sealed class TrafficRow
{
    public long Id { get; set; }
    public string SessionId { get; set; } = "";
    public string StartedAt { get; set; } = "";
    public string? CompletedAt { get; set; }
    public double? DurationMs { get; set; }
    public string Method { get; set; } = "";
    public string Url { get; set; } = "";
    public string Hostname { get; set; } = "";
    public string Path { get; set; } = "";
    public string? QueryString { get; set; }
    public string Scheme { get; set; } = "http";
    public int Port { get; set; }
    public string? RequestHeaders { get; set; }
    public byte[]? RequestBody { get; set; }
    public string? RequestContentType { get; set; }
    public long? RequestContentLength { get; set; }
    public int? StatusCode { get; set; }
    public string? ReasonPhrase { get; set; }
    public string? ResponseHeaders { get; set; }
    public byte[]? ResponseBody { get; set; }
    public string? ResponseContentType { get; set; }
    public long? ResponseContentLength { get; set; }
    public string? RequestHttpVersion { get; set; }
    public string? ResponseHttpVersion { get; set; }
    public string? ServerIpAddress { get; set; }
    public double? TimingSendMs { get; set; }
    public double? TimingWaitMs { get; set; }
    public double? TimingReceiveMs { get; set; }
}
