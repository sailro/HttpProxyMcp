using FluentAssertions;
using HttpProxyMcp.Core.Interfaces;
using HttpProxyMcp.Core.Models;
using HttpProxyMcp.Tests.Helpers;
using NSubstitute;

namespace HttpProxyMcp.Tests.Storage;

// Tests for ITrafficStore implementations.
// Uses NSubstitute to verify store contract behavior.
// When a real implementation lands, these can be upgraded to integration tests.
public class TrafficStoreTests
{
    private readonly ITrafficStore _store;

    public TrafficStoreTests()
    {
        _store = Substitute.For<ITrafficStore>();
    }

    #region Save and Retrieve

    [Fact]
    public async Task SaveTrafficEntry_ReturnsPositiveId()
    {
        var entry = TrafficEntryBuilder.Get().Build();
        _store.SaveTrafficEntryAsync(Arg.Any<TrafficEntry>(), Arg.Any<CancellationToken>())
            .Returns(1L);

        var id = await _store.SaveTrafficEntryAsync(entry, TestContext.Current.CancellationToken);

        id.Should().BePositive();
    }

    [Fact]
    public async Task GetTrafficEntry_ReturnsSavedEntry()
    {
        var entry = TrafficEntryBuilder.Get("https://api.example.com/users")
            .WithId(42)
            .WithStatusCode(200)
            .Build();

        _store.GetTrafficEntryAsync(42, Arg.Any<CancellationToken>())
            .Returns(entry);

        var result = await _store.GetTrafficEntryAsync(42, TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        result!.Id.Should().Be(42);
        result.Request.Url.Should().Be("https://api.example.com/users");
        result.Request.Method.Should().Be("GET");
        result.Response!.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task GetTrafficEntry_NonExistentId_ReturnsNull()
    {
        _store.GetTrafficEntryAsync(99999, Arg.Any<CancellationToken>())
            .Returns((TrafficEntry?)null);

        var result = await _store.GetTrafficEntryAsync(99999, TestContext.Current.CancellationToken);
        result.Should().BeNull();
    }

    [Fact]
    public async Task SaveTrafficEntry_PreservesRequestHeaders()
    {
        var headers = new Dictionary<string, string[]>
        {
            ["Authorization"] = ["Bearer token123"],
            ["X-Custom-Header"] = ["value1", "value2"]
        };
        var entry = TrafficEntryBuilder.Get()
            .WithId(1)
            .WithRequestHeaders(headers)
            .Build();

        _store.SaveTrafficEntryAsync(Arg.Any<TrafficEntry>(), Arg.Any<CancellationToken>())
            .Returns(1L);
        _store.GetTrafficEntryAsync(1, Arg.Any<CancellationToken>())
            .Returns(entry);

        await _store.SaveTrafficEntryAsync(entry, TestContext.Current.CancellationToken);
        var result = await _store.GetTrafficEntryAsync(1, TestContext.Current.CancellationToken);

        result!.Request.Headers.Should().ContainKey("Authorization");
        result.Request.Headers["Authorization"].Should().Contain("Bearer token123");
        result.Request.Headers["X-Custom-Header"].Should().HaveCount(2);
    }

    [Fact]
    public async Task SaveTrafficEntry_PreservesRequestAndResponseBodies()
    {
        var entry = TrafficEntryBuilder.Post("https://api.example.com/data", "{\"name\":\"test\"}")
            .WithId(1)
            .WithResponseBody("{\"id\":1,\"name\":\"test\"}")
            .Build();

        _store.GetTrafficEntryAsync(1, Arg.Any<CancellationToken>())
            .Returns(entry);

        var result = await _store.GetTrafficEntryAsync(1, TestContext.Current.CancellationToken);

        result!.Request.Body.Should().NotBeNull();
        System.Text.Encoding.UTF8.GetString(result.Request.Body!).Should().Contain("test");
        result.Response!.Body.Should().NotBeNull();
        System.Text.Encoding.UTF8.GetString(result.Response.Body!).Should().Contain("test");
    }

    [Fact]
    public async Task SaveTrafficEntry_PreservesTimingData()
    {
        var startedAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        var completedAt = startedAt.AddMilliseconds(350);

        var entry = TrafficEntryBuilder.Get()
            .WithId(1)
            .WithStartedAt(startedAt)
            .WithCompletedAt(completedAt)
            .Build();

        _store.GetTrafficEntryAsync(1, Arg.Any<CancellationToken>())
            .Returns(entry);

        var result = await _store.GetTrafficEntryAsync(1, TestContext.Current.CancellationToken);

        result!.StartedAt.Should().BeCloseTo(startedAt, TimeSpan.FromSeconds(1));
        result.CompletedAt.Should().NotBeNull();
        result.Duration.Should().NotBeNull();
        result.Duration!.Value.TotalMilliseconds.Should().BeApproximately(350, 50);
    }

    #endregion

    #region Filter by hostname, URL, status, method

    [Fact]
    public async Task QueryTraffic_FilterByHostname_ReturnsMatchingEntries()
    {
        List<TrafficEntry> matchingEntries =
        [
            TrafficEntryBuilder.Get("https://api.example.com/users").Build(),
            TrafficEntryBuilder.Get("https://api.example.com/posts").Build()
        ];

        _store.QueryTrafficAsync(
            Arg.Is<TrafficFilter>(f => f.Hostname == "api.example.com"),
            Arg.Any<CancellationToken>())
            .Returns(matchingEntries);

        var filter = new TrafficFilter { Hostname = "api.example.com" };
        var results = await _store.QueryTrafficAsync(filter, TestContext.Current.CancellationToken);

        results.Should().HaveCount(2);
        results.Should().AllSatisfy(e => e.Request.Hostname.Should().Be("api.example.com"));
    }

    [Fact]
    public async Task QueryTraffic_FilterByMethod_ReturnsOnlyMatchingMethod()
    {
        List<TrafficEntry> postEntries =
        [
            TrafficEntryBuilder.Post("https://api.example.com/data").Build()
        ];

        _store.QueryTrafficAsync(
            Arg.Is<TrafficFilter>(f => f.Method == "POST"),
            Arg.Any<CancellationToken>())
            .Returns(postEntries);

        var filter = new TrafficFilter { Method = "POST" };
        var results = await _store.QueryTrafficAsync(filter, TestContext.Current.CancellationToken);

        results.Should().AllSatisfy(e => e.Request.Method.Should().Be("POST"));
    }

    [Fact]
    public async Task QueryTraffic_FilterByStatusCode_ReturnsMatchingStatusCode()
    {
        List<TrafficEntry> notFoundEntries =
        [
            TrafficEntryBuilder.Get("https://api.example.com/missing").WithStatusCode(404, "Not Found").Build()
        ];

        _store.QueryTrafficAsync(
            Arg.Is<TrafficFilter>(f => f.StatusCode == 404),
            Arg.Any<CancellationToken>())
            .Returns(notFoundEntries);

        var filter = new TrafficFilter { StatusCode = 404 };
        var results = await _store.QueryTrafficAsync(filter, TestContext.Current.CancellationToken);

        results.Should().AllSatisfy(e => e.Response!.StatusCode.Should().Be(404));
    }

    [Fact]
    public async Task QueryTraffic_FilterByStatusCodeRange_ReturnsEntriesInRange()
    {
        List<TrafficEntry> serverErrors =
        [
            TrafficEntryBuilder.Get().WithStatusCode(500, "Internal Server Error").Build(),
            TrafficEntryBuilder.Get().WithStatusCode(502, "Bad Gateway").Build()
        ];

        _store.QueryTrafficAsync(
            Arg.Is<TrafficFilter>(f => f.MinStatusCode == 500 && f.MaxStatusCode == 599),
            Arg.Any<CancellationToken>())
            .Returns(serverErrors);

        var filter = new TrafficFilter { MinStatusCode = 500, MaxStatusCode = 599 };
        var results = await _store.QueryTrafficAsync(filter, TestContext.Current.CancellationToken);

        results.Should().HaveCount(2);
        results.Should().AllSatisfy(e =>
            e.Response!.StatusCode.Should().BeInRange(500, 599));
    }

    [Fact]
    public async Task QueryTraffic_FilterByUrlPattern_ReturnsMatchingUrls()
    {
        List<TrafficEntry> apiEntries =
        [
            TrafficEntryBuilder.Get("https://example.com/api/v1/users").Build(),
            TrafficEntryBuilder.Get("https://example.com/api/v2/users").Build()
        ];

        _store.QueryTrafficAsync(
            Arg.Is<TrafficFilter>(f => f.UrlPattern == "/api/"),
            Arg.Any<CancellationToken>())
            .Returns(apiEntries);

        var filter = new TrafficFilter { UrlPattern = "/api/" };
        var results = await _store.QueryTrafficAsync(filter, TestContext.Current.CancellationToken);

        results.Should().HaveCount(2);
        results.Should().AllSatisfy(e => e.Request.Url.Should().Contain("/api/"));
    }

    #endregion

    #region Time range filtering

    [Fact]
    public async Task QueryTraffic_FilterByTimeRange_ReturnsEntriesInRange()
    {
        var now = DateTimeOffset.UtcNow;
        var inRangeEntry = TrafficEntryBuilder.Get()
            .WithStartedAt(now.AddMinutes(-5))
            .Build();

        _store.QueryTrafficAsync(
            Arg.Is<TrafficFilter>(f =>
                f.After == now.AddMinutes(-10) && f.Before == now),
            Arg.Any<CancellationToken>())
            .Returns([inRangeEntry]);

        var filter = new TrafficFilter
        {
            After = now.AddMinutes(-10),
            Before = now
        };
        var results = await _store.QueryTrafficAsync(filter, TestContext.Current.CancellationToken);

        results.Should().HaveCount(1);
    }

    [Fact]
    public async Task QueryTraffic_FilterAfterOnly_ReturnsEntriesAfterTime()
    {
        var cutoff = DateTimeOffset.UtcNow.AddHours(-1);
        List<TrafficEntry> recentEntries =
        [
            TrafficEntryBuilder.Get().WithStartedAt(DateTimeOffset.UtcNow.AddMinutes(-30)).Build(),
            TrafficEntryBuilder.Get().WithStartedAt(DateTimeOffset.UtcNow.AddMinutes(-15)).Build()
        ];

        _store.QueryTrafficAsync(
            Arg.Is<TrafficFilter>(f => f.After == cutoff),
            Arg.Any<CancellationToken>())
            .Returns(recentEntries);

        var filter = new TrafficFilter { After = cutoff };
        var results = await _store.QueryTrafficAsync(filter, TestContext.Current.CancellationToken);

        results.Should().HaveCount(2);
    }

    #endregion

    #region Body search

    [Fact]
    public async Task SearchBodies_ReturnsEntriesContainingText()
    {
        var matchingEntry = TrafficEntryBuilder.Get()
            .WithResponseBody("{\"error\":\"not_found\",\"message\":\"User not found\"}")
            .Build();

        _store.SearchBodiesAsync("not_found", null, 50, Arg.Any<CancellationToken>())
            .Returns([matchingEntry]);

        var results = await _store.SearchBodiesAsync("not_found", cancellationToken: TestContext.Current.CancellationToken);

        results.Should().HaveCount(1);
    }

    [Fact]
    public async Task SearchBodies_NoMatches_ReturnsEmpty()
    {
        _store.SearchBodiesAsync("nonexistent_text_xyz", null, 50, Arg.Any<CancellationToken>())
            .Returns([]);

        var results = await _store.SearchBodiesAsync("nonexistent_text_xyz", cancellationToken: TestContext.Current.CancellationToken);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchBodies_WithSessionFilter_OnlySearchesInSession()
    {
        var sessionId = Guid.NewGuid();
        var matchingEntry = TrafficEntryBuilder.Get()
            .WithSessionId(sessionId)
            .WithResponseBody("{\"key\":\"match\"}")
            .Build();

        _store.SearchBodiesAsync("match", sessionId, 50, Arg.Any<CancellationToken>())
            .Returns([matchingEntry]);

        var results = await _store.SearchBodiesAsync("match", sessionId, cancellationToken: TestContext.Current.CancellationToken);

        results.Should().HaveCount(1);
        results[0].SessionId.Should().Be(sessionId);
    }

    #endregion

    #region Pagination

    [Fact]
    public async Task QueryTraffic_Pagination_RespectsLimitAndOffset()
    {
        List<TrafficEntry> page1 = [.. TestData.CreateVariedEntries(10).Take(5)];
        List<TrafficEntry> page2 = [.. TestData.CreateVariedEntries(10).Skip(5).Take(5)];

        _store.QueryTrafficAsync(
            Arg.Is<TrafficFilter>(f => f.Offset == 0 && f.Limit == 5),
            Arg.Any<CancellationToken>())
            .Returns(page1);

        _store.QueryTrafficAsync(
            Arg.Is<TrafficFilter>(f => f.Offset == 5 && f.Limit == 5),
            Arg.Any<CancellationToken>())
            .Returns(page2);

        var firstPage = await _store.QueryTrafficAsync(new TrafficFilter { Offset = 0, Limit = 5 }, TestContext.Current.CancellationToken);
        var secondPage = await _store.QueryTrafficAsync(new TrafficFilter { Offset = 5, Limit = 5 }, TestContext.Current.CancellationToken);

        firstPage.Should().HaveCount(5);
        secondPage.Should().HaveCount(5);
    }

    [Fact]
    public async Task CountTraffic_ReturnsCorrectTotal()
    {
        _store.CountTrafficAsync(Arg.Any<TrafficFilter>(), Arg.Any<CancellationToken>())
            .Returns(42);

        var count = await _store.CountTrafficAsync(new TrafficFilter(), TestContext.Current.CancellationToken);

        count.Should().Be(42);
    }

    [Fact]
    public async Task CountTraffic_WithFilter_ReturnsFilteredCount()
    {
        _store.CountTrafficAsync(
            Arg.Is<TrafficFilter>(f => f.Hostname == "api.example.com"),
            Arg.Any<CancellationToken>())
            .Returns(15);

        var filter = new TrafficFilter { Hostname = "api.example.com" };
        var count = await _store.CountTrafficAsync(filter, TestContext.Current.CancellationToken);

        count.Should().Be(15);
    }

    #endregion

    #region Empty database

    [Fact]
    public async Task QueryTraffic_EmptyStore_ReturnsEmptyList()
    {
        _store.QueryTrafficAsync(Arg.Any<TrafficFilter>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var results = await _store.QueryTrafficAsync(new TrafficFilter(), TestContext.Current.CancellationToken);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task CountTraffic_EmptyStore_ReturnsZero()
    {
        _store.CountTrafficAsync(Arg.Any<TrafficFilter>(), Arg.Any<CancellationToken>())
            .Returns(0);

        var count = await _store.CountTrafficAsync(new TrafficFilter(), TestContext.Current.CancellationToken);

        count.Should().Be(0);
    }

    [Fact]
    public async Task GetStatistics_EmptyStore_ReturnsZeroStats()
    {
        _store.GetStatisticsAsync(null, Arg.Any<CancellationToken>())
            .Returns(new TrafficStatistics());

        var stats = await _store.GetStatisticsAsync(cancellationToken: TestContext.Current.CancellationToken);

        stats.TotalRequests.Should().Be(0);
        stats.RequestsByMethod.Should().BeEmpty();
        stats.RequestsByStatusCode.Should().BeEmpty();
        stats.RequestsByHostname.Should().BeEmpty();
        stats.AverageDurationMs.Should().BeNull();
    }

    [Fact]
    public async Task SearchBodies_EmptyStore_ReturnsEmptyList()
    {
        _store.SearchBodiesAsync(Arg.Any<string>(), null, 50, Arg.Any<CancellationToken>())
            .Returns([]);

        var results = await _store.SearchBodiesAsync("anything", cancellationToken: TestContext.Current.CancellationToken);

        results.Should().BeEmpty();
    }

    #endregion

    #region Large bodies

    [Fact]
    public async Task SaveTrafficEntry_LargeBody_HandledCorrectly()
    {
        var largeEntry = TestData.CreateLargeBodyEntry(1_000_000);
        largeEntry.Id = 1;

        _store.SaveTrafficEntryAsync(Arg.Any<TrafficEntry>(), Arg.Any<CancellationToken>())
            .Returns(1L);
        _store.GetTrafficEntryAsync(1, Arg.Any<CancellationToken>())
            .Returns(largeEntry);

        await _store.SaveTrafficEntryAsync(largeEntry, TestContext.Current.CancellationToken);
        var result = await _store.GetTrafficEntryAsync(1, TestContext.Current.CancellationToken);

        result!.Request.Body.Should().HaveCount(1_000_000);
        result.Response!.Body.Should().HaveCount(1_000_000);
    }

    #endregion

    #region Statistics

    [Fact]
    public async Task GetStatistics_ReturnsCorrectAggregates()
    {
        var stats = new TrafficStatistics
        {
            TotalRequests = 100,
            RequestsByMethod = new Dictionary<string, int>
            {
                ["GET"] = 60,
                ["POST"] = 30,
                ["PUT"] = 10
            },
            RequestsByStatusCode = new Dictionary<int, int>
            {
                [200] = 80,
                [404] = 15,
                [500] = 5
            },
            RequestsByHostname = new Dictionary<string, int>
            {
                ["api.example.com"] = 50,
                ["cdn.example.com"] = 30,
                ["auth.example.com"] = 20
            },
            TotalRequestBytes = 50_000,
            TotalResponseBytes = 500_000,
            AverageDurationMs = 150.5,
            EarliestRequest = DateTimeOffset.UtcNow.AddHours(-2),
            LatestRequest = DateTimeOffset.UtcNow
        };

        _store.GetStatisticsAsync(null, Arg.Any<CancellationToken>())
            .Returns(stats);

        var result = await _store.GetStatisticsAsync(cancellationToken: TestContext.Current.CancellationToken);

        result.TotalRequests.Should().Be(100);
        result.RequestsByMethod.Should().HaveCount(3);
        result.RequestsByMethod["GET"].Should().Be(60);
        result.RequestsByStatusCode[200].Should().Be(80);
        result.RequestsByHostname.Should().ContainKey("api.example.com");
        result.TotalRequestBytes.Should().Be(50_000);
        result.TotalResponseBytes.Should().Be(500_000);
        result.AverageDurationMs.Should().BeApproximately(150.5, 0.01);
        result.EarliestRequest.Should().NotBeNull();
        result.LatestRequest.Should().NotBeNull();
    }

    [Fact]
    public async Task GetStatistics_WithSessionId_FiltersToSession()
    {
        var sessionId = Guid.NewGuid();
        var sessionStats = new TrafficStatistics { TotalRequests = 25 };

        _store.GetStatisticsAsync(sessionId, Arg.Any<CancellationToken>())
            .Returns(sessionStats);

        var result = await _store.GetStatisticsAsync(sessionId, TestContext.Current.CancellationToken);

        result.TotalRequests.Should().Be(25);
    }

    #endregion

    #region Clear traffic

    [Fact]
    public async Task ClearTraffic_ClearsAllEntries()
    {
        await _store.ClearTrafficAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _store.Received(1).ClearTrafficAsync(null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClearTraffic_WithSessionId_ClearsOnlySessionEntries()
    {
        var sessionId = Guid.NewGuid();

        await _store.ClearTrafficAsync(sessionId, TestContext.Current.CancellationToken);

        await _store.Received(1).ClearTrafficAsync(sessionId, Arg.Any<CancellationToken>());
    }

    #endregion
}
