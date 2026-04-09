using HttpProxyMcp.Storage;

namespace HttpProxyMcp.McpServer;

// Registers core services (proxy engine, storage, session manager).
public static class ServiceRegistration
{
	public static IServiceCollection AddProxyServices(this IServiceCollection services)
	{
		// Proxy engine (IProxyEngine + certificate manager)
		Proxy.ServiceCollectionExtensions.AddProxyServices(services);

		// SQLite storage layer (ITrafficStore + ISessionManager)
		services.AddStorageServices();

		return services;
	}
}
