using HttpProxyMcp.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace HttpProxyMcp.Proxy;

// DI registration for proxy engine services.
public static class ServiceCollectionExtensions
{
	// Registers the proxy engine and certificate manager as singletons.
	public static IServiceCollection AddProxyServices(this IServiceCollection services)
	{
		services.AddSingleton<RootCertificateManager>();
		services.AddSingleton<SystemProxyManager>();
		services.AddSingleton<IProxyEngine, ProxyEngine>();

		return services;
	}
}
