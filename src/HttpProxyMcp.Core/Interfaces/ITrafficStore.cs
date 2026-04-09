using HttpProxyMcp.Core.Models;

namespace HttpProxyMcp.Core.Interfaces;

// Persistence layer for captured traffic data.
public interface ITrafficStore
{
	Task InitializeAsync(CancellationToken cancellationToken = default);

	// Write
	Task<long> SaveTrafficEntryAsync(TrafficEntry entry, CancellationToken cancellationToken = default);

	// Read — single
	Task<TrafficEntry?> GetTrafficEntryAsync(long id, CancellationToken cancellationToken = default);

	// Read — filtered list
	Task<IReadOnlyList<TrafficEntry>> QueryTrafficAsync(TrafficFilter filter, CancellationToken cancellationToken = default);
	Task<int> CountTrafficAsync(TrafficFilter filter, CancellationToken cancellationToken = default);

	// Search request/response bodies
	Task<IReadOnlyList<TrafficEntry>> SearchBodiesAsync(string searchText, Guid? sessionId = null, int limit = 50, CancellationToken cancellationToken = default);

	// Statistics
	Task<TrafficStatistics> GetStatisticsAsync(Guid? sessionId = null, CancellationToken cancellationToken = default);

	// Maintenance
	Task ClearTrafficAsync(Guid? sessionId = null, CancellationToken cancellationToken = default);
}
