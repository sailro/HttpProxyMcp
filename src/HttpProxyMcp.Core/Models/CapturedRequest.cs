namespace HttpProxyMcp.Core.Models;

public sealed class CapturedRequest
{
	public string Method { get; set; } = string.Empty;
	public string Url { get; set; } = string.Empty;
	public string Hostname { get; set; } = string.Empty;
	public string Path { get; set; } = string.Empty;
	public string? QueryString { get; set; }
	public string Scheme { get; set; } = "http";
	public int Port { get; set; }
	public Dictionary<string, string[]> Headers { get; set; } = [];
	public byte[]? Body { get; set; }
	public string? ContentType { get; set; }
	public long? ContentLength { get; set; }
	public string? HttpVersion { get; set; }
}
