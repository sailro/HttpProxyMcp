namespace HttpProxyMcp.Core.Models;

public sealed class CapturedResponse
{
    public int StatusCode { get; set; }
    public string? ReasonPhrase { get; set; }
    public Dictionary<string, string[]> Headers { get; set; } = [];
    public byte[]? Body { get; set; }
    public string? ContentType { get; set; }
    public long? ContentLength { get; set; }
    public string? HttpVersion { get; set; }
}
