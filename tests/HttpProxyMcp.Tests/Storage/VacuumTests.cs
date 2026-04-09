using FluentAssertions;
using HttpProxyMcp.Core.Models;
using HttpProxyMcp.Storage;
using HttpProxyMcp.Tests.Helpers;
using Microsoft.Data.Sqlite;

namespace HttpProxyMcp.Tests.Storage;

// Tests verifying that ClearTrafficAsync and DeleteSessionAsync execute VACUUM
// to reclaim disk space after bulk deletes. Uses page_count to verify compaction
// because WAL mode makes raw file size comparisons unreliable.
[Trait("Category", "Integration")]
public class VacuumTests : IAsyncLifetime
{
	private readonly string _dbPath;
	private readonly string _connectionString;
	private readonly SqliteTrafficStore _store;
	private readonly SqliteSessionManager _sessionManager;

	public VacuumTests()
	{
		_dbPath = Path.Combine(Path.GetTempPath(), $"vacuum_test_{Guid.NewGuid()}.db");
		_connectionString = $"Data Source={_dbPath}";
		_store = new SqliteTrafficStore(_connectionString);
		_sessionManager = new SqliteSessionManager(_connectionString);
	}

	public async ValueTask InitializeAsync()
	{
		await _store.InitializeAsync();
	}

	public ValueTask DisposeAsync()
	{
		foreach (var suffix in new[] { "", "-wal", "-shm" })
		{
			try { File.Delete(_dbPath + suffix); } catch { }
		}
		return default;
	}

	private async Task<long> GetPageCount()
	{
		using var conn = new SqliteConnection(_connectionString);
		await conn.OpenAsync();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = "PRAGMA page_count;";
		return Convert.ToInt64(await cmd.ExecuteScalarAsync());
	}

	private async Task ForceWalCheckpoint()
	{
		using var conn = new SqliteConnection(_connectionString);
		await conn.OpenAsync();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
		await cmd.ExecuteNonQueryAsync();
	}

	private async Task<ProxySession> InsertBulkTestData(int count = 100, int bodySize = 10_000)
	{
		var session = await _sessionManager.CreateSessionAsync("vacuum-test");

		for (int i = 0; i < count; i++)
		{
			var entry = TrafficEntryBuilder.Get($"https://example.com/page/{i}")
				.WithSessionId(session.Id)
				.WithResponseBody(new string('x', bodySize))
				.Build();
			await _store.SaveTrafficEntryAsync(entry);
		}

		return session;
	}

	[Fact]
	public async Task ClearTrafficAsync_WithData_VacuumsCompactingDatabase()
	{
		var session = await InsertBulkTestData();

		// Checkpoint WAL so page_count reflects the full data set
		await ForceWalCheckpoint();
		var pagesBefore = await GetPageCount();
		pagesBefore.Should().BeGreaterThan(50, "bulk data should occupy many pages");

		await _store.ClearTrafficAsync(session.Id, TestContext.Current.CancellationToken);

		// After DELETE + VACUUM, page count should drop dramatically.
		// Without VACUUM, freed pages stay allocated (page_count unchanged).
		await ForceWalCheckpoint();
		var pagesAfter = await GetPageCount();
		pagesAfter.Should().BeLessThan(pagesBefore,
			"VACUUM should compact the database after deleting traffic entries");

		// Verify data is actually gone
		var count = await _store.CountTrafficAsync(new TrafficFilter { SessionId = session.Id }, TestContext.Current.CancellationToken);
		count.Should().Be(0);
	}

	[Fact]
	public async Task DeleteSessionAsync_WithData_VacuumsCompactingDatabase()
	{
		var session = await InsertBulkTestData();

		// Checkpoint WAL so page_count reflects the full data set
		await ForceWalCheckpoint();
		var pagesBefore = await GetPageCount();
		pagesBefore.Should().BeGreaterThan(50, "bulk data should occupy many pages");

		await _sessionManager.DeleteSessionAsync(session.Id, TestContext.Current.CancellationToken);

		// After cascaded DELETE + VACUUM, page count should drop dramatically
		await ForceWalCheckpoint();
		var pagesAfter = await GetPageCount();
		pagesAfter.Should().BeLessThan(pagesBefore,
			"VACUUM should compact the database after deleting session and cascaded traffic");
	}
}
