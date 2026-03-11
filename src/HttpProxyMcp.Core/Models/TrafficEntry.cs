namespace HttpProxyMcp.Core.Models;

// A complete captured traffic entry pairing request with response.
public sealed class TrafficEntry
{
    public long Id { get; set; }
    public Guid SessionId { get; set; }
    public CapturedRequest Request { get; set; } = new();
    public CapturedResponse? Response { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public TimeSpan? Duration => CompletedAt.HasValue ? CompletedAt.Value - StartedAt : null;
}
