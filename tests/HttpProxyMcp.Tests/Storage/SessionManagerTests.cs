using FluentAssertions;
using HttpProxyMcp.Core.Interfaces;
using HttpProxyMcp.Core.Models;
using NSubstitute;

namespace HttpProxyMcp.Tests.Storage;

// Tests for ISessionManager contract — CRUD and active session management.
public class SessionManagerTests
{
    private readonly ISessionManager _sessions;

    public SessionManagerTests()
    {
        _sessions = Substitute.For<ISessionManager>();
    }

    #region Create Session

    [Fact]
    public async Task CreateSession_ReturnsSessionWithNameAndId()
    {
        var expected = new ProxySession
        {
            Id = Guid.NewGuid(),
            Name = "test-session",
            CreatedAt = DateTimeOffset.UtcNow
        };

        _sessions.CreateSessionAsync("test-session", Arg.Any<CancellationToken>())
            .Returns(expected);

        var session = await _sessions.CreateSessionAsync("test-session", TestContext.Current.CancellationToken);

        session.Should().NotBeNull();
        session.Id.Should().NotBeEmpty();
        session.Name.Should().Be("test-session");
        session.IsActive.Should().BeTrue();
        session.ClosedAt.Should().BeNull();
    }

    [Fact]
    public async Task CreateSession_CreatedAtIsSet()
    {
        var now = DateTimeOffset.UtcNow;
        var expected = new ProxySession
        {
            Id = Guid.NewGuid(),
            Name = "timed-session",
            CreatedAt = now
        };

        _sessions.CreateSessionAsync("timed-session", Arg.Any<CancellationToken>())
            .Returns(expected);

        var session = await _sessions.CreateSessionAsync("timed-session", TestContext.Current.CancellationToken);

        session.CreatedAt.Should().BeCloseTo(now, TimeSpan.FromSeconds(5));
    }

    #endregion

    #region Get Session

    [Fact]
    public async Task GetSession_ExistingId_ReturnsSession()
    {
        var id = Guid.NewGuid();
        var expected = new ProxySession
        {
            Id = id,
            Name = "found-session",
            CreatedAt = DateTimeOffset.UtcNow
        };

        _sessions.GetSessionAsync(id, Arg.Any<CancellationToken>())
            .Returns(expected);

        var session = await _sessions.GetSessionAsync(id, TestContext.Current.CancellationToken);

        session.Should().NotBeNull();
        session!.Id.Should().Be(id);
    }

    [Fact]
    public async Task GetSession_NonExistentId_ReturnsNull()
    {
        _sessions.GetSessionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((ProxySession?)null);

        var result = await _sessions.GetSessionAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }

    #endregion

    #region List Sessions

    [Fact]
    public async Task ListSessions_ReturnsAllSessions()
    {
        List<ProxySession> sessions =
        [
            new() { Id = Guid.NewGuid(), Name = "session-1", CreatedAt = DateTimeOffset.UtcNow },
            new() { Id = Guid.NewGuid(), Name = "session-2", CreatedAt = DateTimeOffset.UtcNow },
            new() { Id = Guid.NewGuid(), Name = "session-3", CreatedAt = DateTimeOffset.UtcNow }
        ];

        _sessions.ListSessionsAsync(Arg.Any<CancellationToken>())
            .Returns(sessions);

        var result = await _sessions.ListSessionsAsync(TestContext.Current.CancellationToken);

        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task ListSessions_Empty_ReturnsEmptyList()
    {
        _sessions.ListSessionsAsync(Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await _sessions.ListSessionsAsync(TestContext.Current.CancellationToken);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ListSessions_IncludesEntryCount()
    {
        List<ProxySession> sessions =
        [
            new() { Id = Guid.NewGuid(), Name = "busy-session", CreatedAt = DateTimeOffset.UtcNow, EntryCount = 42 }
        ];

        _sessions.ListSessionsAsync(Arg.Any<CancellationToken>())
            .Returns(sessions);

        var result = await _sessions.ListSessionsAsync(TestContext.Current.CancellationToken);

        result[0].EntryCount.Should().Be(42);
    }

    #endregion

    #region Close Session

    [Fact]
    public async Task CloseSession_CallsManager()
    {
        var id = Guid.NewGuid();

        await _sessions.CloseSessionAsync(id, TestContext.Current.CancellationToken);

        await _sessions.Received(1).CloseSessionAsync(id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClosedSession_IsNotActive()
    {
        var session = new ProxySession
        {
            Id = Guid.NewGuid(),
            Name = "closed",
            CreatedAt = DateTimeOffset.UtcNow,
            ClosedAt = DateTimeOffset.UtcNow
        };

        session.IsActive.Should().BeFalse();
    }

    #endregion

    #region Delete Session

    [Fact]
    public async Task DeleteSession_CallsManager()
    {
        var id = Guid.NewGuid();

        await _sessions.DeleteSessionAsync(id, TestContext.Current.CancellationToken);

        await _sessions.Received(1).DeleteSessionAsync(id, Arg.Any<CancellationToken>());
    }

    #endregion

    #region Active Session

    [Fact]
    public async Task SetActiveSession_SetsActiveId()
    {
        var id = Guid.NewGuid();

        _sessions.ActiveSessionId.Returns(id);

        await _sessions.SetActiveSessionAsync(id, TestContext.Current.CancellationToken);

        _sessions.ActiveSessionId.Should().Be(id);
    }

    [Fact]
    public void ActiveSessionId_InitiallyNull()
    {
        _sessions.ActiveSessionId.Returns((Guid?)null);

        _sessions.ActiveSessionId.Should().BeNull();
    }

    #endregion
}
