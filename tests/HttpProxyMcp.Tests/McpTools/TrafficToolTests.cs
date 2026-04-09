using FluentAssertions;
using HttpProxyMcp.Core.Interfaces;
using HttpProxyMcp.Core.Models;
using HttpProxyMcp.McpServer.Tools;
using HttpProxyMcp.Tests.Helpers;
using NSubstitute;

namespace HttpProxyMcp.Tests.McpTools;

// Tests for TrafficTools MCP tool methods.
// Verifies that tools correctly delegate to ITrafficStore and format output.
public class TrafficToolTests
{
	private readonly ITrafficStore _store;

	public TrafficToolTests()
	{
		_store = Substitute.For<ITrafficStore>();
	}

	#region ListTraffic

	[Fact]
	public async Task ListTraffic_NoFilters_ReturnsAllEntries()
	{
		List<TrafficEntry> entries =
		[
			TrafficEntryBuilder.Get("https://example.com/page1").WithId(1).Build(),
			TrafficEntryBuilder.Get("https://example.com/page2").WithId(2).Build()
		];

		_store.QueryTrafficAsync(Arg.Any<TrafficFilter>(), Arg.Any<CancellationToken>())
			.Returns(entries);
		_store.CountTrafficAsync(Arg.Any<TrafficFilter>(), Arg.Any<CancellationToken>())
			.Returns(2);

		var result = await TrafficTools.ListTraffic(_store);

		result.Should().Contain("2 of 2");
		result.Should().Contain("GET");
		result.Should().Contain("example.com");
	}

	[Fact]
	public async Task ListTraffic_WithHostnameFilter_PassesFilterToStore()
	{
		_store.QueryTrafficAsync(Arg.Any<TrafficFilter>(), Arg.Any<CancellationToken>())
			.Returns([]);
		_store.CountTrafficAsync(Arg.Any<TrafficFilter>(), Arg.Any<CancellationToken>())
			.Returns(0);

		await TrafficTools.ListTraffic(_store, hostname: "api.example.com");

		await _store.Received(1).QueryTrafficAsync(
			Arg.Is<TrafficFilter>(f => f.Hostname == "api.example.com"),
			Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task ListTraffic_WithMethodFilter_PassesFilterToStore()
	{
		_store.QueryTrafficAsync(Arg.Any<TrafficFilter>(), Arg.Any<CancellationToken>())
			.Returns([]);
		_store.CountTrafficAsync(Arg.Any<TrafficFilter>(), Arg.Any<CancellationToken>())
			.Returns(0);

		await TrafficTools.ListTraffic(_store, method: "POST");

		await _store.Received(1).QueryTrafficAsync(
			Arg.Is<TrafficFilter>(f => f.Method == "POST"),
			Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task ListTraffic_WithUrlPatternFilter_PassesFilterToStore()
	{
		_store.QueryTrafficAsync(Arg.Any<TrafficFilter>(), Arg.Any<CancellationToken>())
			.Returns([]);
		_store.CountTrafficAsync(Arg.Any<TrafficFilter>(), Arg.Any<CancellationToken>())
			.Returns(0);

		await TrafficTools.ListTraffic(_store, urlPattern: "/api/v1/");

		await _store.Received(1).QueryTrafficAsync(
			Arg.Is<TrafficFilter>(f => f.UrlPattern == "/api/v1/"),
			Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task ListTraffic_WithStatusCodeFilter_PassesFilterToStore()
	{
		_store.QueryTrafficAsync(Arg.Any<TrafficFilter>(), Arg.Any<CancellationToken>())
			.Returns([]);
		_store.CountTrafficAsync(Arg.Any<TrafficFilter>(), Arg.Any<CancellationToken>())
			.Returns(0);

		await TrafficTools.ListTraffic(_store, statusCode: 500);

		await _store.Received(1).QueryTrafficAsync(
			Arg.Is<TrafficFilter>(f => f.StatusCode == 500),
			Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task ListTraffic_WithPagination_PassesLimitAndOffset()
	{
		_store.QueryTrafficAsync(Arg.Any<TrafficFilter>(), Arg.Any<CancellationToken>())
			.Returns([]);
		_store.CountTrafficAsync(Arg.Any<TrafficFilter>(), Arg.Any<CancellationToken>())
			.Returns(0);

		await TrafficTools.ListTraffic(_store, limit: 10, offset: 20);

		await _store.Received(1).QueryTrafficAsync(
			Arg.Is<TrafficFilter>(f => f.Limit == 10 && f.Offset == 20),
			Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task ListTraffic_ShowsStatusCodes()
	{
		List<TrafficEntry> entries =
		[
			TrafficEntryBuilder.Get("https://example.com/ok").WithId(1).WithStatusCode(200).Build(),
			TrafficEntryBuilder.Get("https://example.com/fail").WithId(2).WithStatusCode(500).Build()
		];

		_store.QueryTrafficAsync(Arg.Any<TrafficFilter>(), Arg.Any<CancellationToken>())
			.Returns(entries);
		_store.CountTrafficAsync(Arg.Any<TrafficFilter>(), Arg.Any<CancellationToken>())
			.Returns(2);

		var result = await TrafficTools.ListTraffic(_store);

		result.Should().Contain("200");
		result.Should().Contain("500");
	}

	[Fact]
	public async Task ListTraffic_PendingResponse_ShowsPending()
	{
		var entry = TrafficEntryBuilder.Get("https://example.com/pending").WithId(1).Build();
		entry.Response = null;
		entry.CompletedAt = null;

		_store.QueryTrafficAsync(Arg.Any<TrafficFilter>(), Arg.Any<CancellationToken>())
			.Returns([entry]);
		_store.CountTrafficAsync(Arg.Any<TrafficFilter>(), Arg.Any<CancellationToken>())
			.Returns(1);

		var result = await TrafficTools.ListTraffic(_store);

		result.Should().Contain("pending");
	}

	#endregion

	#region GetTrafficEntry

	[Fact]
	public async Task GetTrafficEntry_ExistingId_ReturnsFormattedEntry()
	{
		var entry = TrafficEntryBuilder.Get("https://api.example.com/data")
			.WithId(42)
			.WithStatusCode(200)
			.WithResponseBody("{\"result\":\"ok\"}")
			.Build();

		_store.GetTrafficEntryAsync(42, Arg.Any<CancellationToken>())
			.Returns(entry);

		var result = await TrafficTools.GetTrafficEntry(_store, 42);

		result.Should().Contain("42");
		result.Should().Contain("GET");
		result.Should().Contain("api.example.com");
		result.Should().Contain("200");
	}

	[Fact]
	public async Task GetTrafficEntry_NonExistentId_ReturnsNotFound()
	{
		_store.GetTrafficEntryAsync(99999, Arg.Any<CancellationToken>())
			.Returns((TrafficEntry?)null);

		var result = await TrafficTools.GetTrafficEntry(_store, 99999);

		result.Should().Contain("not found");
	}

	[Fact]
	public async Task GetTrafficEntry_ShowsRequestHeaders()
	{
		var entry = TrafficEntryBuilder.Get("https://example.com/test")
			.WithId(1)
			.WithRequestHeaders(new Dictionary<string, string[]>
			{
				["Authorization"] = ["Bearer xyz"]
			})
			.Build();

		_store.GetTrafficEntryAsync(1, Arg.Any<CancellationToken>())
			.Returns(entry);

		var result = await TrafficTools.GetTrafficEntry(_store, 1);

		result.Should().Contain("Authorization");
		result.Should().Contain("Bearer xyz");
	}

	[Fact]
	public async Task GetTrafficEntry_ShowsTextBody()
	{
		var entry = TrafficEntryBuilder.Get("https://example.com/json")
			.WithId(1)
			.WithResponseBody("{\"data\":\"visible\"}", "application/json")
			.Build();

		_store.GetTrafficEntryAsync(1, Arg.Any<CancellationToken>())
			.Returns(entry);

		var result = await TrafficTools.GetTrafficEntry(_store, 1);

		result.Should().Contain("visible");
	}

	[Fact]
	public async Task GetTrafficEntry_BinaryBody_ShowsByteCount()
	{
		var binaryBody = new byte[500];
		Random.Shared.NextBytes(binaryBody);

		var entry = TrafficEntryBuilder.Get("https://example.com/binary")
			.WithId(1)
			.WithResponseBodyBytes(binaryBody, "application/octet-stream")
			.Build();

		_store.GetTrafficEntryAsync(1, Arg.Any<CancellationToken>())
			.Returns(entry);

		var result = await TrafficTools.GetTrafficEntry(_store, 1);

		result.Should().Contain("500 bytes");
	}

	#endregion

	#region SearchBodies

	[Fact]
	public async Task SearchBodies_ReturnsMatchingEntries()
	{
		List<TrafficEntry> entries =
		[
			TrafficEntryBuilder.Get("https://api.example.com/search").WithId(1).Build(),
		];

		_store.SearchBodiesAsync("api_key", null, 50, Arg.Any<CancellationToken>())
			.Returns(entries);

		var result = await TrafficTools.SearchBodies(_store, "api_key");

		result.Should().Contain("1 entries matching");
		result.Should().Contain("api_key");
	}

	[Fact]
	public async Task SearchBodies_NoResults_ReturnsZeroCount()
	{
		_store.SearchBodiesAsync("nonexistent", null, 50, Arg.Any<CancellationToken>())
			.Returns([]);

		var result = await TrafficTools.SearchBodies(_store, "nonexistent");

		result.Should().Contain("0 entries matching");
	}

	[Fact]
	public async Task SearchBodies_RespectsLimit()
	{
		_store.SearchBodiesAsync("test", null, 10, Arg.Any<CancellationToken>())
			.Returns([]);

		await TrafficTools.SearchBodies(_store, "test", limit: 10);

		await _store.Received(1).SearchBodiesAsync("test", null, 10, Arg.Any<CancellationToken>());
	}

	#endregion

	#region GetStatistics

	[Fact]
	public async Task GetStatistics_ReturnsFormattedStats()
	{
		var stats = new TrafficStatistics
		{
			TotalRequests = 100,
			TotalRequestBytes = 50000,
			TotalResponseBytes = 500000,
			AverageDurationMs = 150.0,
			RequestsByMethod = new Dictionary<string, int> { ["GET"] = 80, ["POST"] = 20 },
			RequestsByStatusCode = new Dictionary<int, int> { [200] = 90, [404] = 10 },
			RequestsByHostname = new Dictionary<string, int> { ["api.example.com"] = 100 }
		};

		_store.GetStatisticsAsync(null, Arg.Any<CancellationToken>())
			.Returns(stats);

		var result = await TrafficTools.GetStatistics(_store);

		result.Should().Contain("100");
		result.Should().Contain("GET");
		result.Should().Contain("POST");
		result.Should().Contain("200");
		result.Should().Contain("api.example.com");
	}

	[Fact]
	public async Task GetStatistics_WithSessionId_PassesSessionToStore()
	{
		var sessionId = Guid.NewGuid();

		_store.GetStatisticsAsync(sessionId, Arg.Any<CancellationToken>())
			.Returns(new TrafficStatistics());

		await TrafficTools.GetStatistics(_store, sessionId: sessionId.ToString());

		await _store.Received(1).GetStatisticsAsync(sessionId, Arg.Any<CancellationToken>());
	}

	#endregion

	#region ClearTraffic

	[Fact]
	public async Task ClearTraffic_NoSession_ClearsAll()
	{
		var result = await TrafficTools.ClearTraffic(_store);

		result.Should().Contain("all captured traffic");
		await _store.Received(1).ClearTrafficAsync(null, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task ClearTraffic_WithSession_ClearsSessionOnly()
	{
		var sessionId = Guid.NewGuid();

		var result = await TrafficTools.ClearTraffic(_store, sessionId: sessionId.ToString());

		result.Should().Contain(sessionId.ToString());
		await _store.Received(1).ClearTrafficAsync(sessionId, Arg.Any<CancellationToken>());
	}

	#endregion
}
