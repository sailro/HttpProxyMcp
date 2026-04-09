using FluentAssertions;
using HttpProxyMcp.Storage;
using HttpProxyMcp.Tests.Helpers;

namespace HttpProxyMcp.Tests.Storage;

// Integration tests verifying that the 6 new HAR 1.2 fields
// (request/response HttpVersion, ServerIpAddress, timing send/wait/receive)
// round-trip correctly through SqliteTrafficStore.
[Trait("Category", "Integration")]
public class HarFieldsTests : IAsyncLifetime
{
	private readonly string _dbPath;
	private readonly string _connectionString;
	private readonly SqliteTrafficStore _store;
	private readonly SqliteSessionManager _sessionManager;
	private Guid _sessionId;

	public HarFieldsTests()
	{
		_dbPath = Path.Combine(Path.GetTempPath(), $"har_fields_test_{Guid.NewGuid()}.db");
		_connectionString = $"Data Source={_dbPath}";
		_store = new SqliteTrafficStore(_connectionString);
		_sessionManager = new SqliteSessionManager(_connectionString);
	}

	public async ValueTask InitializeAsync()
	{
		await _store.InitializeAsync();
		var session = await _sessionManager.CreateSessionAsync("har-test");
		_sessionId = session.Id;
	}

	public ValueTask DisposeAsync()
	{
		foreach (var suffix in new[] { "", "-wal", "-shm" })
		{
			try { File.Delete(_dbPath + suffix); } catch { }
		}
		return default;
	}

	[Fact]
	public async Task SaveTrafficEntry_WithAllHarFields_RoundTripsCorrectly()
	{
		var entry = TrafficEntryBuilder.Get("https://api.example.com/users")
			.WithSessionId(_sessionId)
			.WithHttpVersion("HTTP/1.1", "HTTP/2")
			.WithServerIpAddress("93.184.216.34")
			.WithTimings(1.5, 42.3, 8.7)
			.Build();

		var id = await _store.SaveTrafficEntryAsync(entry, TestContext.Current.CancellationToken);
		var result = await _store.GetTrafficEntryAsync(id, TestContext.Current.CancellationToken);

		result.Should().NotBeNull();
		result!.Request.HttpVersion.Should().Be("HTTP/1.1");
		result.Response!.HttpVersion.Should().Be("HTTP/2");
		result.ServerIpAddress.Should().Be("93.184.216.34");
		result.TimingSendMs.Should().BeApproximately(1.5, 0.001);
		result.TimingWaitMs.Should().BeApproximately(42.3, 0.001);
		result.TimingReceiveMs.Should().BeApproximately(8.7, 0.001);
	}

	[Fact]
	public async Task SaveTrafficEntry_WithNullHarFields_SavesAndRetrievesWithoutError()
	{
		// Simulates pre-HAR capture data — no HTTP version, no IP, no timings
		var entry = TrafficEntryBuilder.Get("https://example.com/legacy")
			.WithSessionId(_sessionId)
			.Build();

		var id = await _store.SaveTrafficEntryAsync(entry, TestContext.Current.CancellationToken);
		var result = await _store.GetTrafficEntryAsync(id, TestContext.Current.CancellationToken);

		result.Should().NotBeNull();
		result!.Request.HttpVersion.Should().BeNull();
		result.Response!.HttpVersion.Should().BeNull();
		result.ServerIpAddress.Should().BeNull();
		result.TimingSendMs.Should().BeNull();
		result.TimingWaitMs.Should().BeNull();
		result.TimingReceiveMs.Should().BeNull();
	}

	[Fact]
	public async Task SaveTrafficEntry_WithPartialHarFields_PreservesPopulatedFields()
	{
		// Only HTTP version and IP set; timings left null
		var entry = TrafficEntryBuilder.Get("https://example.com/partial")
			.WithSessionId(_sessionId)
			.WithHttpVersion("HTTP/1.1", null)
			.WithServerIpAddress("::1")
			.Build();

		var id = await _store.SaveTrafficEntryAsync(entry, TestContext.Current.CancellationToken);
		var result = await _store.GetTrafficEntryAsync(id, TestContext.Current.CancellationToken);

		result.Should().NotBeNull();
		result!.Request.HttpVersion.Should().Be("HTTP/1.1");
		result.Response!.HttpVersion.Should().BeNull();
		result.ServerIpAddress.Should().Be("::1");
		result.TimingSendMs.Should().BeNull();
		result.TimingWaitMs.Should().BeNull();
		result.TimingReceiveMs.Should().BeNull();
	}

	[Fact]
	public async Task SaveTrafficEntry_WithZeroTimings_PreservesZeroValues()
	{
		// Zero is a valid timing value distinct from null
		var entry = TrafficEntryBuilder.Get("https://example.com/fast")
			.WithSessionId(_sessionId)
			.WithTimings(0.0, 0.0, 0.0)
			.Build();

		var id = await _store.SaveTrafficEntryAsync(entry, TestContext.Current.CancellationToken);
		var result = await _store.GetTrafficEntryAsync(id, TestContext.Current.CancellationToken);

		result.Should().NotBeNull();
		result!.TimingSendMs.Should().Be(0.0);
		result.TimingWaitMs.Should().Be(0.0);
		result.TimingReceiveMs.Should().Be(0.0);
	}

	[Fact]
	public async Task SaveTrafficEntry_WithIpv6Address_RoundTripsCorrectly()
	{
		var entry = TrafficEntryBuilder.Get("https://example.com/ipv6")
			.WithSessionId(_sessionId)
			.WithServerIpAddress("2001:0db8:85a3:0000:0000:8a2e:0370:7334")
			.Build();

		var id = await _store.SaveTrafficEntryAsync(entry, TestContext.Current.CancellationToken);
		var result = await _store.GetTrafficEntryAsync(id, TestContext.Current.CancellationToken);

		result.Should().NotBeNull();
		result!.ServerIpAddress.Should().Be("2001:0db8:85a3:0000:0000:8a2e:0370:7334");
	}

	[Fact]
	public async Task SaveTrafficEntry_ExistingFieldsUnaffectedByNewHarFields()
	{
		// Verify existing fields still round-trip when HAR fields are populated
		var entry = TrafficEntryBuilder.Post("https://api.example.com/data", "{\"test\":true}")
			.WithSessionId(_sessionId)
			.WithStatusCode(201, "Created")
			.WithResponseBody("{\"id\":42}")
			.WithHttpVersion("HTTP/1.1", "HTTP/1.1")
			.WithServerIpAddress("10.0.0.1")
			.WithTimings(2.0, 50.0, 10.0)
			.Build();

		var id = await _store.SaveTrafficEntryAsync(entry, TestContext.Current.CancellationToken);
		var result = await _store.GetTrafficEntryAsync(id, TestContext.Current.CancellationToken);

		result.Should().NotBeNull();
		result!.Request.Method.Should().Be("POST");
		result.Request.Url.Should().Be("https://api.example.com/data");
		result.Response!.StatusCode.Should().Be(201);
		result.Response.ReasonPhrase.Should().Be("Created");

		// Bodies preserved alongside new fields
		System.Text.Encoding.UTF8.GetString(result.Request.Body!).Should().Contain("test");
		System.Text.Encoding.UTF8.GetString(result.Response.Body!).Should().Contain("42");
	}
}
