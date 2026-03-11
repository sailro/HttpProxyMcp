using FluentAssertions;
using HttpProxyMcp.Core.Interfaces;
using HttpProxyMcp.Core.Models;
using HttpProxyMcp.McpServer.Tools;
using HttpProxyMcp.Tests.Helpers;
using NSubstitute;

namespace HttpProxyMcp.Tests;

// Integration tests verifying the full flow:
// proxy captures → storage persists → MCP tools query.
// 
// These use mocks to simulate the proxy/storage interactions since
// the real implementations are being built by Tank and Mouse.
// Mark real integration tests with [Trait("Category", "Integration")]
// once we can spin up a real proxy.
[Trait("Category", "Integration")]
public class IntegrationTests
{
    // Simulates the full flow: proxy captures traffic → stored → queried via MCP tool.
    [Fact]
    public async Task FullFlow_CaptureTraffic_StoreAndQueryViaMcpTool()
    {
        // Arrange — set up store and engine
        var store = Substitute.For<ITrafficStore>();
        var sessionManager = Substitute.For<ISessionManager>();
        var engine = Substitute.For<IProxyEngine>();

        var sessionId = Guid.NewGuid();
        sessionManager.ActiveSessionId.Returns(sessionId);

        // Simulate: proxy captures a request
        var capturedEntry = TrafficEntryBuilder.Get("https://api.example.com/v1/users")
            .WithSessionId(sessionId)
            .WithId(1)
            .WithStatusCode(200)
            .WithResponseBody("[{\"id\":1,\"name\":\"Alice\"}]")
            .Build();

        // Store saves and returns the entry
        store.SaveTrafficEntryAsync(Arg.Any<TrafficEntry>(), Arg.Any<CancellationToken>())
            .Returns(1L);
        store.QueryTrafficAsync(Arg.Any<TrafficFilter>(), Arg.Any<CancellationToken>())
            .Returns([capturedEntry]);
        store.CountTrafficAsync(Arg.Any<TrafficFilter>(), Arg.Any<CancellationToken>())
            .Returns(1);
        store.GetTrafficEntryAsync(1, Arg.Any<CancellationToken>())
            .Returns(capturedEntry);

        // Act — simulate proxy event wiring
        TrafficEntry? eventEntry = null;
        engine.TrafficCaptured += (_, entry) =>
        {
            entry.SessionId = sessionManager.ActiveSessionId ?? Guid.Empty;
            eventEntry = entry;
        };
        engine.TrafficCaptured += Raise.Event<EventHandler<TrafficEntry>>(engine, capturedEntry);

        // Store the captured entry
        var storedId = await store.SaveTrafficEntryAsync(eventEntry!, TestContext.Current.CancellationToken);

        // Query via MCP tool
        var listResult = await TrafficTools.ListTraffic(store, hostname: "api.example.com");
        var detailResult = await TrafficTools.GetTrafficEntry(store, storedId);

        // Assert
        storedId.Should().Be(1);
        listResult.Should().Contain("api.example.com");
        listResult.Should().Contain("1 of 1");
        detailResult.Should().Contain("api.example.com");
        detailResult.Should().Contain("200");
    }

    // Simulates multiple requests, then filters by hostname to get correct subset.
    [Fact]
    public async Task FullFlow_MultipleRequests_FilterByHostname()
    {
        var store = Substitute.For<ITrafficStore>();
        var sessionId = Guid.NewGuid();

        // Create mixed traffic from multiple hosts
        List<TrafficEntry> apiEntries =
        [
            TrafficEntryBuilder.Get("https://api.example.com/users").WithSessionId(sessionId).WithId(1).Build(),
            TrafficEntryBuilder.Post("https://api.example.com/users", "{\"name\":\"Bob\"}").WithSessionId(sessionId).WithId(2).WithStatusCode(201).Build()
        ];

        List<TrafficEntry> cdnEntries =
        [
            TrafficEntryBuilder.Get("https://cdn.example.com/image.png").WithSessionId(sessionId).WithId(3).Build()
        ];

        // Wire up filtered responses
        store.QueryTrafficAsync(
            Arg.Is<TrafficFilter>(f => f.Hostname == "api.example.com"),
            Arg.Any<CancellationToken>())
            .Returns(apiEntries);
        store.CountTrafficAsync(
            Arg.Is<TrafficFilter>(f => f.Hostname == "api.example.com"),
            Arg.Any<CancellationToken>())
            .Returns(2);

        store.QueryTrafficAsync(
            Arg.Is<TrafficFilter>(f => f.Hostname == "cdn.example.com"),
            Arg.Any<CancellationToken>())
            .Returns(cdnEntries);
        store.CountTrafficAsync(
            Arg.Is<TrafficFilter>(f => f.Hostname == "cdn.example.com"),
            Arg.Any<CancellationToken>())
            .Returns(1);

        // Query via MCP tools
        var apiResult = await TrafficTools.ListTraffic(store, hostname: "api.example.com");
        var cdnResult = await TrafficTools.ListTraffic(store, hostname: "cdn.example.com");

        // Assert
        apiResult.Should().Contain("2 of 2");
        cdnResult.Should().Contain("1 of 1");
    }

    // Full session lifecycle: create → set active → capture → close → list.
    [Fact]
    public async Task FullFlow_SessionLifecycle()
    {
        var sessionManager = Substitute.For<ISessionManager>();

        var sessionId = Guid.NewGuid();
        var session = new ProxySession
        {
            Id = sessionId,
            Name = "test-flow",
            CreatedAt = DateTimeOffset.UtcNow,
            EntryCount = 0
        };

        sessionManager.CreateSessionAsync("test-flow", Arg.Any<CancellationToken>())
            .Returns(session);
        sessionManager.ListSessionsAsync(Arg.Any<CancellationToken>())
            .Returns([session]);

        // Create session
        var createResult = await SessionTools.CreateSession(sessionManager, "test-flow");
        createResult.Should().Contain("Created");
        createResult.Should().Contain("test-flow");

        // Set active
        var activeResult = await SessionTools.SetActiveSession(sessionManager, sessionId.ToString());
        activeResult.Should().Contain(sessionId.ToString());

        // List sessions
        var listResult = await SessionTools.ListSessions(sessionManager);
        listResult.Should().Contain("test-flow");

        // Close session
        var closeResult = await SessionTools.CloseSession(sessionManager, sessionId.ToString());
        closeResult.Should().Contain("closed");
    }

    // Verifies search across request/response bodies works end-to-end.
    [Fact]
    public async Task FullFlow_SearchBodiesAcrossRequests()
    {
        var store = Substitute.For<ITrafficStore>();

        var matchingEntry = TrafficEntryBuilder.Post("https://api.example.com/login", "{\"username\":\"admin\"}")
            .WithId(5)
            .WithResponseBody("{\"token\":\"abc123\"}")
            .Build();

        store.SearchBodiesAsync("admin", null, 50, Arg.Any<CancellationToken>())
            .Returns([matchingEntry]);

        var result = await TrafficTools.SearchBodies(store, "admin");

        result.Should().Contain("1 entries matching");
        result.Should().Contain("admin");
    }

    // Verifies statistics are computed correctly across multiple traffic entries.
    [Fact]
    public async Task FullFlow_StatisticsAcrossMultipleRequests()
    {
        var store = Substitute.For<ITrafficStore>();

        var stats = new TrafficStatistics
        {
            TotalRequests = 50,
            RequestsByMethod = new Dictionary<string, int>
            {
                ["GET"] = 35,
                ["POST"] = 10,
                ["PUT"] = 5
            },
            RequestsByStatusCode = new Dictionary<int, int>
            {
                [200] = 40,
                [404] = 5,
                [500] = 5
            },
            RequestsByHostname = new Dictionary<string, int>
            {
                ["api.example.com"] = 30,
                ["cdn.example.com"] = 20
            },
            TotalRequestBytes = 25000,
            TotalResponseBytes = 250000,
            AverageDurationMs = 120.0
        };

        store.GetStatisticsAsync(null, Arg.Any<CancellationToken>())
            .Returns(stats);

        var result = await TrafficTools.GetStatistics(store);

        result.Should().Contain("50");
        result.Should().Contain("GET=35");
        result.Should().Contain("POST=10");
        result.Should().Contain("200=40");
        result.Should().Contain("api.example.com");
    }

    // Verifies proxy start → capture → stop lifecycle.
    [Fact]
    public async Task FullFlow_ProxyStartCaptureStop()
    {
        var engine = Substitute.For<IProxyEngine>();

        // Start proxy
        engine.IsRunning.Returns(false);
        var startResult = await ProxyControlTools.StartProxy(engine);
        startResult.Should().Contain("started");

        // Simulate proxy is now running
        engine.IsRunning.Returns(true);
        engine.Configuration.Returns(new ProxyConfiguration { Port = 8080, EnableSsl = true });

        // Check status
        var statusResult = await ProxyControlTools.GetProxyStatus(engine);
        statusResult.Should().Contain("running");
        statusResult.Should().Contain("8080");

        // Stop proxy
        var stopResult = await ProxyControlTools.StopProxy(engine);
        stopResult.Should().Contain("stopped");
    }
}
