namespace HttpProxyMcp.Core.Models;

// Represents a proxy capture session (a logical grouping of traffic).
public sealed class ProxySession
{
	public Guid Id { get; set; }
	public string Name { get; set; } = string.Empty;
	public DateTimeOffset CreatedAt { get; set; }
	public DateTimeOffset? ClosedAt { get; set; }
	public bool IsActive => ClosedAt is null;
	public int EntryCount { get; set; }
}
