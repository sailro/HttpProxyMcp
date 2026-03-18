using System.Text.Json;
using Dapper;
using HttpProxyMcp.Core.Interfaces;
using HttpProxyMcp.Core.Models;
using Microsoft.Data.Sqlite;

namespace HttpProxyMcp.Storage;

// SQLite-backed implementation of ITrafficStore using Dapper.
public sealed class SqliteTrafficStore(string connectionString) : ITrafficStore
{
	static SqliteTrafficStore()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    private SqliteConnection CreateConnection() => new(connectionString);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        await conn.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS sessions (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                created_at TEXT NOT NULL,
                closed_at TEXT
            );

            CREATE TABLE IF NOT EXISTS traffic_entries (
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
                request_http_version TEXT,
                response_http_version TEXT,
                server_ip_address TEXT,
                timing_send_ms REAL,
                timing_wait_ms REAL,
                timing_receive_ms REAL,
                FOREIGN KEY (session_id) REFERENCES sessions(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_traffic_session_id ON traffic_entries(session_id);
            CREATE INDEX IF NOT EXISTS idx_traffic_hostname ON traffic_entries(hostname);
            CREATE INDEX IF NOT EXISTS idx_traffic_url ON traffic_entries(url);
            CREATE INDEX IF NOT EXISTS idx_traffic_status_code ON traffic_entries(status_code);
            CREATE INDEX IF NOT EXISTS idx_traffic_method ON traffic_entries(method);
            CREATE INDEX IF NOT EXISTS idx_traffic_started_at ON traffic_entries(started_at);
            """);

        // Auto-migrate older databases that lack the HAR 1.2 columns
        var existingColumns = await conn.QueryAsync<dynamic>("PRAGMA table_info(traffic_entries)");
        var columnNames = existingColumns.Select(c => (string)c.name).ToHashSet();

        if (!columnNames.Contains("request_http_version"))
            await conn.ExecuteAsync("ALTER TABLE traffic_entries ADD COLUMN request_http_version TEXT");
        if (!columnNames.Contains("response_http_version"))
            await conn.ExecuteAsync("ALTER TABLE traffic_entries ADD COLUMN response_http_version TEXT");
        if (!columnNames.Contains("server_ip_address"))
            await conn.ExecuteAsync("ALTER TABLE traffic_entries ADD COLUMN server_ip_address TEXT");
        if (!columnNames.Contains("timing_send_ms"))
            await conn.ExecuteAsync("ALTER TABLE traffic_entries ADD COLUMN timing_send_ms REAL");
        if (!columnNames.Contains("timing_wait_ms"))
            await conn.ExecuteAsync("ALTER TABLE traffic_entries ADD COLUMN timing_wait_ms REAL");
        if (!columnNames.Contains("timing_receive_ms"))
            await conn.ExecuteAsync("ALTER TABLE traffic_entries ADD COLUMN timing_receive_ms REAL");

        // Enable WAL mode and foreign keys for performance and integrity
        await conn.ExecuteAsync("PRAGMA journal_mode=WAL;");
        await conn.ExecuteAsync("PRAGMA foreign_keys=ON;");
    }

    public async Task<long> SaveTrafficEntryAsync(TrafficEntry entry, CancellationToken cancellationToken = default)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        var id = await conn.ExecuteScalarAsync<long>("""
            INSERT INTO traffic_entries (
                session_id, started_at, completed_at, duration_ms,
                method, url, hostname, path, query_string, scheme, port,
                request_headers, request_body, request_content_type, request_content_length,
                status_code, reason_phrase,
                response_headers, response_body, response_content_type, response_content_length,
                request_http_version, response_http_version,
                server_ip_address, timing_send_ms, timing_wait_ms, timing_receive_ms
            ) VALUES (
                @SessionId, @StartedAt, @CompletedAt, @DurationMs,
                @Method, @Url, @Hostname, @Path, @QueryString, @Scheme, @Port,
                @RequestHeaders, @RequestBody, @RequestContentType, @RequestContentLength,
                @StatusCode, @ReasonPhrase,
                @ResponseHeaders, @ResponseBody, @ResponseContentType, @ResponseContentLength,
                @RequestHttpVersion, @ResponseHttpVersion,
                @ServerIpAddress, @TimingSendMs, @TimingWaitMs, @TimingReceiveMs
            );
            SELECT last_insert_rowid();
            """, new
        {
            SessionId = entry.SessionId.ToString(),
            StartedAt = entry.StartedAt.ToString("O"),
            CompletedAt = entry.CompletedAt?.ToString("O"),
            DurationMs = entry.Duration?.TotalMilliseconds,
            entry.Request.Method,
            entry.Request.Url,
            entry.Request.Hostname,
            entry.Request.Path,
            entry.Request.QueryString,
            entry.Request.Scheme,
            entry.Request.Port,
            RequestHeaders = SerializeHeaders(entry.Request.Headers),
            RequestBody = entry.Request.Body,
            RequestContentType = entry.Request.ContentType,
            RequestContentLength = entry.Request.ContentLength,
            RequestHttpVersion = entry.Request.HttpVersion,
            entry.Response?.StatusCode,
            entry.Response?.ReasonPhrase,
            ResponseHeaders = entry.Response is not null ? SerializeHeaders(entry.Response.Headers) : null,
            ResponseBody = entry.Response?.Body,
            ResponseContentType = entry.Response?.ContentType,
            ResponseContentLength = entry.Response?.ContentLength,
            ResponseHttpVersion = entry.Response?.HttpVersion,
            entry.ServerIpAddress,
            entry.TimingSendMs,
            entry.TimingWaitMs,
            entry.TimingReceiveMs,
        });

        entry.Id = id;
        return id;
    }

    public async Task<TrafficEntry?> GetTrafficEntryAsync(long id, CancellationToken cancellationToken = default)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        var row = await conn.QuerySingleOrDefaultAsync<TrafficRow>(
            "SELECT * FROM traffic_entries WHERE id = @Id", new { Id = id });

        return row is not null ? MapToEntry(row, includeBodies: true) : null;
    }

    public async Task<IReadOnlyList<TrafficEntry>> QueryTrafficAsync(TrafficFilter filter, CancellationToken cancellationToken = default)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        var (where, parameters) = BuildWhereClause(filter);

        // Exclude bodies in list view for performance
        var sql = $"""
            SELECT id, session_id, started_at, completed_at, duration_ms,
                   method, url, hostname, path, query_string, scheme, port,
                   request_headers, request_content_type, request_content_length,
                   status_code, reason_phrase,
                   response_headers, response_content_type, response_content_length,
                   request_http_version, response_http_version,
                   server_ip_address, timing_send_ms, timing_wait_ms, timing_receive_ms
            FROM traffic_entries
            {where}
            ORDER BY started_at DESC
            LIMIT @Limit OFFSET @Offset
            """;

        parameters.Add("Limit", filter.Limit);
        parameters.Add("Offset", filter.Offset);

        var rows = await conn.QueryAsync<TrafficRow>(sql, parameters);
        return [.. rows.Select(r => MapToEntry(r, includeBodies: false))];
    }

    public async Task<int> CountTrafficAsync(TrafficFilter filter, CancellationToken cancellationToken = default)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        var (where, parameters) = BuildWhereClause(filter);
        var sql = $"SELECT COUNT(*) FROM traffic_entries {where}";

        return await conn.ExecuteScalarAsync<int>(sql, parameters);
    }

    public async Task<IReadOnlyList<TrafficEntry>> SearchBodiesAsync(
        string searchText, Guid? sessionId = null, int limit = 50,
        CancellationToken cancellationToken = default)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        var parameters = new DynamicParameters();
        parameters.Add("Search", $"%{searchText}%");
        parameters.Add("Limit", limit);

        var sessionFilter = "";
        if (sessionId.HasValue)
        {
            sessionFilter = "AND session_id = @SessionId";
            parameters.Add("SessionId", sessionId.Value.ToString());
        }

        var sql = $"""
            SELECT id, session_id, started_at, completed_at, duration_ms,
                   method, url, hostname, path, query_string, scheme, port,
                   request_headers, request_content_type, request_content_length,
                   status_code, reason_phrase,
                   response_headers, response_content_type, response_content_length,
                   request_http_version, response_http_version,
                   server_ip_address, timing_send_ms, timing_wait_ms, timing_receive_ms
            FROM traffic_entries
            WHERE (CAST(request_body AS TEXT) LIKE @Search
                   OR CAST(response_body AS TEXT) LIKE @Search)
            {sessionFilter}
            ORDER BY started_at DESC
            LIMIT @Limit
            """;

        var rows = await conn.QueryAsync<TrafficRow>(sql, parameters);
        return [.. rows.Select(r => MapToEntry(r, includeBodies: false))];
    }

    public async Task<TrafficStatistics> GetStatisticsAsync(
        Guid? sessionId = null, CancellationToken cancellationToken = default)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        var sessionWhere = sessionId.HasValue ? "WHERE session_id = @SessionId" : "";
        var sessionAnd = sessionId.HasValue ? "AND session_id = @SessionId" : "";
        object? param = sessionId.HasValue
            ? new { SessionId = sessionId.Value.ToString() }
            : null;

        var stats = new TrafficStatistics();

        // Aggregates
        var agg = await conn.QuerySingleAsync(
            $"""
            SELECT
                COUNT(*) AS total,
                COALESCE(SUM(request_content_length), 0) AS req_bytes,
                COALESCE(SUM(response_content_length), 0) AS res_bytes,
                AVG(duration_ms) AS avg_duration,
                MIN(started_at) AS earliest,
                MAX(started_at) AS latest
            FROM traffic_entries {sessionWhere}
            """, param);

        stats.TotalRequests = (int)(long)agg.total;
        stats.TotalRequestBytes = (long)agg.req_bytes;
        stats.TotalResponseBytes = (long)agg.res_bytes;
        stats.AverageDurationMs = agg.avg_duration is not null ? (double)agg.avg_duration : null;
        stats.EarliestRequest = agg.earliest is not null ? DateTimeOffset.Parse((string)agg.earliest) : null;
        stats.LatestRequest = agg.latest is not null ? DateTimeOffset.Parse((string)agg.latest) : null;

        // By method
        var methods = await conn.QueryAsync(
            $"SELECT method, COUNT(*) AS cnt FROM traffic_entries {sessionWhere} GROUP BY method",
            param);
        stats.RequestsByMethod = methods.ToDictionary(
            m => (string)m.method, m => (int)(long)m.cnt);

        // By status code
        var statuses = await conn.QueryAsync(
            $"SELECT status_code, COUNT(*) AS cnt FROM traffic_entries WHERE status_code IS NOT NULL {sessionAnd} GROUP BY status_code",
            param);
        stats.RequestsByStatusCode = statuses.ToDictionary(
            s => (int)(long)s.status_code, s => (int)(long)s.cnt);

        // By hostname
        var hosts = await conn.QueryAsync(
            $"SELECT hostname, COUNT(*) AS cnt FROM traffic_entries {sessionWhere} GROUP BY hostname",
            param);
        stats.RequestsByHostname = hosts.ToDictionary(
            h => (string)h.hostname, h => (int)(long)h.cnt);

        return stats;
    }

    public async Task ClearTrafficAsync(Guid? sessionId = null, CancellationToken cancellationToken = default)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        if (sessionId.HasValue)
        {
            await conn.ExecuteAsync(
                "DELETE FROM traffic_entries WHERE session_id = @SessionId",
                new { SessionId = sessionId.Value.ToString() });
        }
        else
        {
            await conn.ExecuteAsync("DELETE FROM traffic_entries");
        }

        // Reclaim disk space after bulk delete
        await conn.ExecuteAsync("VACUUM;");
    }

    #region Helpers

    private static (string sql, DynamicParameters parameters) BuildWhereClause(TrafficFilter filter)
    {
        var conditions = new List<string>();
        var parameters = new DynamicParameters();

        if (filter.SessionId.HasValue)
        {
            conditions.Add("session_id = @SessionId");
            parameters.Add("SessionId", filter.SessionId.Value.ToString());
        }

        if (!string.IsNullOrEmpty(filter.Hostname))
        {
            conditions.Add("hostname = @Hostname");
            parameters.Add("Hostname", filter.Hostname);
        }

        if (!string.IsNullOrEmpty(filter.UrlPattern))
        {
            conditions.Add("url LIKE @UrlPattern");
            parameters.Add("UrlPattern", $"%{filter.UrlPattern}%");
        }

        if (!string.IsNullOrEmpty(filter.Method))
        {
            conditions.Add("UPPER(method) = @Method");
            parameters.Add("Method", filter.Method.ToUpperInvariant());
        }

        if (filter.StatusCode.HasValue)
        {
            conditions.Add("status_code = @StatusCode");
            parameters.Add("StatusCode", filter.StatusCode.Value);
        }

        if (filter.MinStatusCode.HasValue)
        {
            conditions.Add("status_code >= @MinStatusCode");
            parameters.Add("MinStatusCode", filter.MinStatusCode.Value);
        }

        if (filter.MaxStatusCode.HasValue)
        {
            conditions.Add("status_code <= @MaxStatusCode");
            parameters.Add("MaxStatusCode", filter.MaxStatusCode.Value);
        }

        if (filter.After.HasValue)
        {
            conditions.Add("started_at >= @After");
            parameters.Add("After", filter.After.Value.ToString("O"));
        }

        if (filter.Before.HasValue)
        {
            conditions.Add("started_at <= @Before");
            parameters.Add("Before", filter.Before.Value.ToString("O"));
        }

        if (!string.IsNullOrEmpty(filter.ContentType))
        {
            conditions.Add("(request_content_type LIKE @ContentType OR response_content_type LIKE @ContentType)");
            parameters.Add("ContentType", $"%{filter.ContentType}%");
        }

        if (!string.IsNullOrEmpty(filter.BodySearchText))
        {
            conditions.Add("(CAST(request_body AS TEXT) LIKE @BodySearch OR CAST(response_body AS TEXT) LIKE @BodySearch)");
            parameters.Add("BodySearch", $"%{filter.BodySearchText}%");
        }

        var sql = conditions.Count > 0
            ? "WHERE " + string.Join(" AND ", conditions)
            : "";

        return (sql, parameters);
    }

    private static TrafficEntry MapToEntry(TrafficRow row, bool includeBodies)
    {
        var entry = new TrafficEntry
        {
            Id = row.Id,
            SessionId = Guid.Parse(row.SessionId),
            StartedAt = DateTimeOffset.Parse(row.StartedAt),
            CompletedAt = row.CompletedAt is not null ? DateTimeOffset.Parse(row.CompletedAt) : null,
            ServerIpAddress = row.ServerIpAddress,
            TimingSendMs = row.TimingSendMs,
            TimingWaitMs = row.TimingWaitMs,
            TimingReceiveMs = row.TimingReceiveMs,
            Request = new CapturedRequest
            {
                Method = row.Method,
                Url = row.Url,
                Hostname = row.Hostname,
                Path = row.Path,
                QueryString = row.QueryString,
                Scheme = row.Scheme,
                Port = row.Port,
                Headers = DeserializeHeaders(row.RequestHeaders),
                ContentType = row.RequestContentType,
                ContentLength = row.RequestContentLength,
                HttpVersion = row.RequestHttpVersion,
                Body = includeBodies ? row.RequestBody : null,
            }
        };

        if (row.StatusCode.HasValue)
        {
            entry.Response = new CapturedResponse
            {
                StatusCode = row.StatusCode.Value,
                ReasonPhrase = row.ReasonPhrase,
                Headers = DeserializeHeaders(row.ResponseHeaders),
                ContentType = row.ResponseContentType,
                ContentLength = row.ResponseContentLength,
                HttpVersion = row.ResponseHttpVersion,
                Body = includeBodies ? row.ResponseBody : null,
            };
        }

        return entry;
    }

    private static string? SerializeHeaders(Dictionary<string, string[]>? headers)
    {
        return headers is null or { Count: 0 }
            ? null
            : JsonSerializer.Serialize(headers);
    }

    private static Dictionary<string, string[]> DeserializeHeaders(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return [];

        return JsonSerializer.Deserialize<Dictionary<string, string[]>>(json)
               ?? [];
    }

    #endregion
}
