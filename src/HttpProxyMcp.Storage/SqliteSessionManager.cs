using System.Threading;
using Dapper;
using HttpProxyMcp.Core.Interfaces;
using HttpProxyMcp.Core.Models;
using Microsoft.Data.Sqlite;

namespace HttpProxyMcp.Storage;

// SQLite-backed implementation of ISessionManager using Dapper.
// Tracks the active session in memory; persists session CRUD to the database.
public sealed class SqliteSessionManager(string connectionString) : ISessionManager
{
	private readonly Lock _lock = new();
    private Guid? _activeSessionId;

    static SqliteSessionManager()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public Guid? ActiveSessionId
    {
        get { lock (_lock) return _activeSessionId; }
    }

    private SqliteConnection CreateConnection() => new(connectionString);

    public async Task<ProxySession> CreateSessionAsync(string name, CancellationToken cancellationToken = default)
    {
        var session = new ProxySession
        {
            Id = Guid.NewGuid(),
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        await conn.ExecuteAsync(
            "INSERT INTO sessions (id, name, created_at) VALUES (@Id, @Name, @CreatedAt)",
            new
            {
                Id = session.Id.ToString(),
                session.Name,
                CreatedAt = session.CreatedAt.ToString("O"),
            });

        // Auto-activate the first created session
        lock (_lock) _activeSessionId ??= session.Id;

        return session;
    }

    public async Task<ProxySession?> GetSessionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        var row = await conn.QuerySingleOrDefaultAsync<SessionRow>(
            """
            SELECT s.id, s.name, s.created_at, s.closed_at,
                   (SELECT COUNT(*) FROM traffic_entries t WHERE t.session_id = s.id) AS entry_count
            FROM sessions s
            WHERE s.id = @Id
            """,
            new { Id = id.ToString() });

        return row is not null ? MapToSession(row) : null;
    }

    public async Task<IReadOnlyList<ProxySession>> ListSessionsAsync(CancellationToken cancellationToken = default)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        var rows = await conn.QueryAsync<SessionRow>(
            """
            SELECT s.id, s.name, s.created_at, s.closed_at,
                   (SELECT COUNT(*) FROM traffic_entries t WHERE t.session_id = s.id) AS entry_count
            FROM sessions s
            ORDER BY s.created_at DESC
            """);

        return [.. rows.Select(MapToSession)];
    }

    public async Task SetActiveSessionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(id, cancellationToken)
            ?? throw new ArgumentException($"Session {id} not found.");
        if (!session.IsActive)
            throw new InvalidOperationException($"Session {id} is closed.");

        lock (_lock) _activeSessionId = id;
    }

    public async Task CloseSessionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        var affected = await conn.ExecuteAsync(
            "UPDATE sessions SET closed_at = @ClosedAt WHERE id = @Id AND closed_at IS NULL",
            new { Id = id.ToString(), ClosedAt = DateTimeOffset.UtcNow.ToString("O") });

        if (affected == 0)
            throw new ArgumentException($"Session {id} not found or already closed.");

        // Clear active session if it was the one closed
        lock (_lock)
        {
            if (_activeSessionId == id)
                _activeSessionId = null;
        }
    }

    public async Task DeleteSessionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        // Foreign key cascade deletes traffic_entries
        await conn.ExecuteAsync("PRAGMA foreign_keys=ON;");
        var affected = await conn.ExecuteAsync(
            "DELETE FROM sessions WHERE id = @Id",
            new { Id = id.ToString() });

        if (affected == 0)
            throw new ArgumentException($"Session {id} not found.");

        // Reclaim disk space after deleting session and its cascaded traffic
        await conn.ExecuteAsync("VACUUM;");

        lock (_lock)
        {
            if (_activeSessionId == id)
                _activeSessionId = null;
        }
    }

    private static ProxySession MapToSession(SessionRow row) => new()
    {
        Id = Guid.Parse(row.Id),
        Name = row.Name,
        CreatedAt = DateTimeOffset.Parse(row.CreatedAt),
        ClosedAt = row.ClosedAt is not null ? DateTimeOffset.Parse(row.ClosedAt) : null,
        EntryCount = row.EntryCount,
    };
}
