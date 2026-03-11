using HttpProxyMcp.McpServer;

var builder = Host.CreateApplicationBuilder(args);

// MCP server over stdio (configured by the McpServer library)
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

// Core services — proxy engine, storage, session management
builder.Services.AddProxyServices();

builder.Services.AddHostedService<ProxyHostedService>();

var host = builder.Build();
host.Run();
