namespace HttpProxyMcp.Core.Models;

// Configuration for the proxy engine.
public sealed class ProxyConfiguration
{
    public int Port { get; set; } = 8080;
    public bool EnableSsl { get; set; } = true;
    public string? RootCertificatePath { get; set; }
    public string? RootCertificatePassword { get; set; }
    public bool SetSystemProxy { get; set; } = true;
    public int MaxBodyCaptureBytes { get; set; } = 10 * 1024 * 1024; // 10 MB
    public List<string> ExcludedHostnames { get; set; } = [];
}
