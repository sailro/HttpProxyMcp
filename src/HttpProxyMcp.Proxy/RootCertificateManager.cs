using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Titanium.Web.Proxy;

namespace HttpProxyMcp.Proxy;

// Manages the root CA certificate used for HTTPS MITM interception.
// Titanium.Web.Proxy handles per-host certificate generation internally;
// this class ensures the root CA is generated once and persisted to disk.
public sealed class RootCertificateManager(ILogger<RootCertificateManager> logger)
{
	// Configures Titanium's certificate manager with a persistent root CA.
    // If no PFX exists at the configured path, a new root CA is generated and saved.
    public void ConfigureCertificates(
        ProxyServer proxyServer,
        string? rootCertPath,
        string? rootCertPassword)
    {
        var certManager = proxyServer.CertificateManager;
        certManager.CertificateValidDays = 3650;

        if (!string.IsNullOrEmpty(rootCertPath))
        {
            certManager.PfxFilePath = rootCertPath;
        }

        if (!string.IsNullOrEmpty(rootCertPassword))
        {
            certManager.PfxPassword = rootCertPassword;
        }

        // Try to load existing root CA from the configured PFX path
        var pfxPath = certManager.PfxFilePath;
        if (!string.IsNullOrEmpty(pfxPath) && File.Exists(pfxPath))
        {
            logger.LogInformation("Loading existing root CA from {Path}", pfxPath);
            try
            {
                certManager.RootCertificate = X509CertificateLoader.LoadPkcs12FromFile(
                    pfxPath,
                    rootCertPassword);
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to load root CA from {Path}, generating new one", pfxPath);
            }
        }

        // Generate a new root CA — Titanium handles the heavy lifting
        logger.LogInformation("Generating new root CA certificate");
        certManager.EnsureRootCertificate();

        if (certManager.RootCertificate is not null)
        {
            logger.LogInformation(
                "Root CA generated: {Subject}, valid until {Expiry}",
                certManager.RootCertificate.Subject,
                certManager.RootCertificate.NotAfter);

            EnsurePfxPersisted(certManager, rootCertPassword);
        }
        else
        {
            logger.LogError("Failed to generate root CA certificate");
        }
    }

    private void EnsurePfxPersisted(
        Titanium.Web.Proxy.Network.CertificateManager certManager,
        string? password)
    {
        var pfxPath = certManager.PfxFilePath;
        if (string.IsNullOrEmpty(pfxPath) || File.Exists(pfxPath))
            return;

        try
        {
            var dir = Path.GetDirectoryName(pfxPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var pfxBytes = certManager.RootCertificate!.Export(X509ContentType.Pfx, password);
            File.WriteAllBytes(pfxPath, pfxBytes);
            logger.LogInformation("Root CA persisted to {Path}", pfxPath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not persist root CA to {Path}", pfxPath);
        }
    }
}
