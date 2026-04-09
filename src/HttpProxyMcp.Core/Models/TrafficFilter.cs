namespace HttpProxyMcp.Core.Models;

// Filter criteria for querying captured traffic.
public sealed class TrafficFilter
{
	public Guid? SessionId { get; set; }
	public string? Hostname { get; set; }
	public string? UrlPattern { get; set; }
	public string? Method { get; set; }
	public int? StatusCode { get; set; }
	public int? MinStatusCode { get; set; }
	public int? MaxStatusCode { get; set; }
	public DateTimeOffset? After { get; set; }
	public DateTimeOffset? Before { get; set; }
	public string? ContentType { get; set; }
	public string? BodySearchText { get; set; }
	public int Offset { get; set; }
	public int Limit { get; set; } = 50;
}
