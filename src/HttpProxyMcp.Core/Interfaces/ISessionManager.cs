using HttpProxyMcp.Core.Models;

namespace HttpProxyMcp.Core.Interfaces;

// Manages logical capture sessions (grouping of traffic).
public interface ISessionManager
{
    Task<ProxySession> CreateSessionAsync(string name, CancellationToken cancellationToken = default);
    Task<ProxySession?> GetSessionAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProxySession>> ListSessionsAsync(CancellationToken cancellationToken = default);
    Task CloseSessionAsync(Guid id, CancellationToken cancellationToken = default);
    Task DeleteSessionAsync(Guid id, CancellationToken cancellationToken = default);

    // The currently active session that new traffic is assigned to.
    Guid? ActiveSessionId { get; }
    Task SetActiveSessionAsync(Guid id, CancellationToken cancellationToken = default);
}
