namespace HttpProxyMcp.Storage;

// Flat DTO mapping to the sessions table, including a computed entry count.
internal sealed class SessionRow
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string? ClosedAt { get; set; }
    public int EntryCount { get; set; }
}
