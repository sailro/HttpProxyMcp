using HttpProxyMcp.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace HttpProxyMcp.Storage;

// DI registration for the SQLite storage layer.
public static class StorageServiceExtensions
{
    // Registers ITrafficStore and ISessionManager backed by SQLite.
    // Both share the same connection string / database file.
    public static IServiceCollection AddStorageServices(
        this IServiceCollection services,
        string connectionString = "Data Source=traffic.db")
    {
        var store = new SqliteTrafficStore(connectionString);
        var sessionManager = new SqliteSessionManager(connectionString);

        services.AddSingleton<ITrafficStore>(store);
        services.AddSingleton<ISessionManager>(sessionManager);

        return services;
    }
}
