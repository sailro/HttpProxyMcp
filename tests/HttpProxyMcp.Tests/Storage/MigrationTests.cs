using FluentAssertions;
using HttpProxyMcp.Storage;
using Microsoft.Data.Sqlite;

namespace HttpProxyMcp.Tests.Storage;

// Tests verifying that InitializeAsync correctly handles schema migration
// for the 6 new HAR 1.2 columns added to the traffic_entries table.
[Trait("Category", "Integration")]
public class MigrationTests : IAsyncLifetime
{
    private readonly string _dbPath;
    private readonly string _connectionString;

    public MigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"migration_test_{Guid.NewGuid()}.db");
        _connectionString = $"Data Source={_dbPath}";
    }

    public ValueTask InitializeAsync() => default;

    public ValueTask DisposeAsync()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(_dbPath + suffix); } catch { }
        }
        return default;
    }

    private static readonly string[] HarColumns =
    [
        "request_http_version",
        "response_http_version",
        "server_ip_address",
        "timing_send_ms",
        "timing_wait_ms",
        "timing_receive_ms"
    ];

    private async Task<List<string>> GetColumnNames(string tableName)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = await cmd.ExecuteReaderAsync();

        var columns = new List<string>();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(1)); // column name is at index 1
        }
        return columns;
    }

    [Fact]
    public async Task InitializeAsync_FreshDatabase_CreatesAllColumnsIncludingHarFields()
    {
        var store = new SqliteTrafficStore(_connectionString);
        await store.InitializeAsync(TestContext.Current.CancellationToken);

        var columns = await GetColumnNames("traffic_entries");

        foreach (var harColumn in HarColumns)
        {
            columns.Should().Contain(harColumn,
                $"fresh database should include HAR column '{harColumn}'");
        }
    }

    [Fact]
    public async Task InitializeAsync_OldSchemaWithoutHarColumns_AddsNewColumns()
    {
        // Create the old schema WITHOUT the 6 new HAR columns
        using (var conn = new SqliteConnection(_connectionString))
        {
            await conn.OpenAsync(TestContext.Current.CancellationToken);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE sessions (
                    id TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    closed_at TEXT
                );

                CREATE TABLE traffic_entries (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    session_id TEXT NOT NULL,
                    started_at TEXT NOT NULL,
                    completed_at TEXT,
                    duration_ms REAL,
                    method TEXT NOT NULL,
                    url TEXT NOT NULL,
                    hostname TEXT NOT NULL,
                    path TEXT NOT NULL,
                    query_string TEXT,
                    scheme TEXT NOT NULL DEFAULT 'http',
                    port INTEGER NOT NULL DEFAULT 80,
                    request_headers TEXT,
                    request_body BLOB,
                    request_content_type TEXT,
                    request_content_length INTEGER,
                    status_code INTEGER,
                    reason_phrase TEXT,
                    response_headers TEXT,
                    response_body BLOB,
                    response_content_type TEXT,
                    response_content_length INTEGER,
                    FOREIGN KEY (session_id) REFERENCES sessions(id) ON DELETE CASCADE
                );
                """;
            await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        // Verify old schema does NOT have the new columns
        var columnsBefore = await GetColumnNames("traffic_entries");
        foreach (var harColumn in HarColumns)
        {
            columnsBefore.Should().NotContain(harColumn,
                $"old schema should not have '{harColumn}' before migration");
        }

        // Run InitializeAsync — it should detect missing columns and add them
        var store = new SqliteTrafficStore(_connectionString);
        await store.InitializeAsync(TestContext.Current.CancellationToken);

        // Verify all 6 new columns now exist
        var columnsAfter = await GetColumnNames("traffic_entries");
        foreach (var harColumn in HarColumns)
        {
            columnsAfter.Should().Contain(harColumn,
                $"migration should have added column '{harColumn}'");
        }
    }

    [Fact]
    public async Task InitializeAsync_Idempotent_RunTwiceWithoutError()
    {
        var store = new SqliteTrafficStore(_connectionString);

        // First run creates everything
        await store.InitializeAsync(TestContext.Current.CancellationToken);

        // Second run should be idempotent — no errors
        var act = () => store.InitializeAsync(TestContext.Current.CancellationToken);
        await act.Should().NotThrowAsync("InitializeAsync must be idempotent");

        // Verify columns still intact after second run
        var columns = await GetColumnNames("traffic_entries");
        foreach (var harColumn in HarColumns)
        {
            columns.Should().Contain(harColumn);
        }
    }

    [Fact]
    public async Task InitializeAsync_OldSchemaWithData_PreservesExistingRows()
    {
        // Create old schema and insert a row
        using (var conn = new SqliteConnection(_connectionString))
        {
            await conn.OpenAsync(TestContext.Current.CancellationToken);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE sessions (
                    id TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    closed_at TEXT
                );

                CREATE TABLE traffic_entries (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    session_id TEXT NOT NULL,
                    started_at TEXT NOT NULL,
                    completed_at TEXT,
                    duration_ms REAL,
                    method TEXT NOT NULL,
                    url TEXT NOT NULL,
                    hostname TEXT NOT NULL,
                    path TEXT NOT NULL,
                    query_string TEXT,
                    scheme TEXT NOT NULL DEFAULT 'http',
                    port INTEGER NOT NULL DEFAULT 80,
                    request_headers TEXT,
                    request_body BLOB,
                    request_content_type TEXT,
                    request_content_length INTEGER,
                    status_code INTEGER,
                    reason_phrase TEXT,
                    response_headers TEXT,
                    response_body BLOB,
                    response_content_type TEXT,
                    response_content_length INTEGER,
                    FOREIGN KEY (session_id) REFERENCES sessions(id) ON DELETE CASCADE
                );
                """;
            await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

            // Insert a session and a traffic entry in old schema
            cmd.CommandText = """
                INSERT INTO sessions (id, name, created_at) VALUES ('11111111-1111-1111-1111-111111111111', 'old-session', '2025-01-01T00:00:00Z');
                INSERT INTO traffic_entries (session_id, started_at, method, url, hostname, path, scheme, port, status_code)
                VALUES ('11111111-1111-1111-1111-111111111111', '2025-01-01T00:00:00Z', 'GET', 'https://example.com/old', 'example.com', '/old', 'https', 443, 200);
                """;
            await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        // Run migration
        var store = new SqliteTrafficStore(_connectionString);
        await store.InitializeAsync(TestContext.Current.CancellationToken);

        // Old row should still be readable with new columns as NULL
        var result = await store.GetTrafficEntryAsync(1, TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        result!.Request.Url.Should().Be("https://example.com/old");
        result.Request.HttpVersion.Should().BeNull();
        result.Response!.HttpVersion.Should().BeNull();
        result.ServerIpAddress.Should().BeNull();
        result.TimingSendMs.Should().BeNull();
        result.TimingWaitMs.Should().BeNull();
        result.TimingReceiveMs.Should().BeNull();
    }
}
