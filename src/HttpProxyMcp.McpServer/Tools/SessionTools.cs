using System.ComponentModel;
using HttpProxyMcp.Core.Interfaces;
using ModelContextProtocol.Server;

namespace HttpProxyMcp.McpServer.Tools;

// MCP tools for managing proxy capture sessions.
[McpServerToolType]
public sealed class SessionTools
{
	[McpServerTool, Description("List all capture sessions")]
	public static async Task<string> ListSessions(ISessionManager sessions)
	{
		var list = await sessions.ListSessionsAsync();
		if (list.Count == 0) return "No sessions found.";

		var lines = list.Select(s =>
			$"[{s.Id}] \"{s.Name}\" — {s.EntryCount} entries " +
			$"({(s.IsActive ? "active" : $"closed {s.ClosedAt:g}")}) created {s.CreatedAt:g}");

		return string.Join("\n", lines);
	}

	[McpServerTool, Description("Create a new capture session")]
	public static async Task<string> CreateSession(
		ISessionManager sessions,
		[Description("Session name")] string name)
	{
		var session = await sessions.CreateSessionAsync(name);
		return $"Created session \"{session.Name}\" ({session.Id}).";
	}

	[McpServerTool, Description("Set the active session for capturing new traffic")]
	public static async Task<string> SetActiveSession(
		ISessionManager sessions,
		[Description("Session ID")] string sessionId)
	{
		await sessions.SetActiveSessionAsync(Guid.Parse(sessionId));
		return $"Active session set to {sessionId}.";
	}

	[McpServerTool, Description("Close a capture session")]
	public static async Task<string> CloseSession(
		ISessionManager sessions,
		[Description("Session ID")] string sessionId)
	{
		await sessions.CloseSessionAsync(Guid.Parse(sessionId));
		return $"Session {sessionId} closed.";
	}

	[McpServerTool, Description("Delete a capture session and its traffic")]
	public static async Task<string> DeleteSession(
		ISessionManager sessions,
		[Description("Session ID")] string sessionId)
	{
		await sessions.DeleteSessionAsync(Guid.Parse(sessionId));
		return $"Session {sessionId} deleted.";
	}
}
