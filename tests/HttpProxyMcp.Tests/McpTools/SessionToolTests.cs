using FluentAssertions;
using HttpProxyMcp.Core.Interfaces;
using HttpProxyMcp.Core.Models;
using HttpProxyMcp.McpServer.Tools;
using NSubstitute;

namespace HttpProxyMcp.Tests.McpTools;

// Tests for SessionTools MCP tool methods.
public class SessionToolTests
{
	private readonly ISessionManager _sessions;

	public SessionToolTests()
	{
		_sessions = Substitute.For<ISessionManager>();
	}

	#region ListSessions

	[Fact]
	public async Task ListSessions_NoSessions_ReturnsNoSessionsMessage()
	{
		_sessions.ListSessionsAsync(Arg.Any<CancellationToken>())
			.Returns([]);

		var result = await SessionTools.ListSessions(_sessions);

		result.Should().Contain("No sessions");
	}

	[Fact]
	public async Task ListSessions_WithSessions_ReturnsFormattedList()
	{
		var session = new ProxySession
		{
			Id = Guid.NewGuid(),
			Name = "test-session",
			CreatedAt = DateTimeOffset.UtcNow,
			EntryCount = 15
		};

		_sessions.ListSessionsAsync(Arg.Any<CancellationToken>())
			.Returns([session]);

		var result = await SessionTools.ListSessions(_sessions);

		result.Should().Contain("test-session");
		result.Should().Contain("15 entries");
		result.Should().Contain("active");
	}

	[Fact]
	public async Task ListSessions_ClosedSession_ShowsClosedStatus()
	{
		var session = new ProxySession
		{
			Id = Guid.NewGuid(),
			Name = "closed-session",
			CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
			ClosedAt = DateTimeOffset.UtcNow,
			EntryCount = 5
		};

		_sessions.ListSessionsAsync(Arg.Any<CancellationToken>())
			.Returns([session]);

		var result = await SessionTools.ListSessions(_sessions);

		result.Should().Contain("closed");
	}

	#endregion

	#region CreateSession

	[Fact]
	public async Task CreateSession_ReturnsConfirmation()
	{
		var created = new ProxySession
		{
			Id = Guid.NewGuid(),
			Name = "new-session",
			CreatedAt = DateTimeOffset.UtcNow
		};

		_sessions.CreateSessionAsync("new-session", Arg.Any<CancellationToken>())
			.Returns(created);

		var result = await SessionTools.CreateSession(_sessions, "new-session");

		result.Should().Contain("Created");
		result.Should().Contain("new-session");
		result.Should().Contain(created.Id.ToString());
	}

	[Fact]
	public async Task CreateSession_DelegatesToManager()
	{
		_sessions.CreateSessionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(new ProxySession { Id = Guid.NewGuid(), Name = "test" });

		await SessionTools.CreateSession(_sessions, "my-session");

		await _sessions.Received(1).CreateSessionAsync("my-session", Arg.Any<CancellationToken>());
	}

	#endregion

	#region SetActiveSession

	[Fact]
	public async Task SetActiveSession_ReturnsConfirmation()
	{
		var id = Guid.NewGuid();

		var result = await SessionTools.SetActiveSession(_sessions, id.ToString());

		result.Should().Contain(id.ToString());
		await _sessions.Received(1).SetActiveSessionAsync(id, Arg.Any<CancellationToken>());
	}

	#endregion

	#region CloseSession

	[Fact]
	public async Task CloseSession_ReturnsConfirmation()
	{
		var id = Guid.NewGuid();

		var result = await SessionTools.CloseSession(_sessions, id.ToString());

		result.Should().Contain(id.ToString());
		result.Should().Contain("closed");
		await _sessions.Received(1).CloseSessionAsync(id, Arg.Any<CancellationToken>());
	}

	#endregion

	#region DeleteSession

	[Fact]
	public async Task DeleteSession_ReturnsConfirmation()
	{
		var id = Guid.NewGuid();

		var result = await SessionTools.DeleteSession(_sessions, id.ToString());

		result.Should().Contain(id.ToString());
		result.Should().Contain("deleted");
		await _sessions.Received(1).DeleteSessionAsync(id, Arg.Any<CancellationToken>());
	}

	#endregion
}
