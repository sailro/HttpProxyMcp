using HttpProxyMcp.Core.Models;

namespace HttpProxyMcp.Tests.Helpers;

// Helpers to generate batches of traffic entries for testing.
public static class TestData
{
    public static Guid DefaultSessionId { get; } = Guid.NewGuid();

    // Creates N traffic entries with varied hostnames, methods, and status codes.
    public static List<TrafficEntry> CreateVariedEntries(int count, Guid? sessionId = null)
    {
        var sid = sessionId ?? DefaultSessionId;
        var methods = new[] { "GET", "POST", "PUT", "DELETE", "PATCH" };
        var hosts = new[] { "api.example.com", "cdn.example.com", "auth.example.com", "web.example.com" };
        var statusCodes = new[] { 200, 201, 204, 301, 400, 404, 500 };
        var baseTime = DateTimeOffset.UtcNow.AddMinutes(-count);

        return [.. Enumerable.Range(0, count).Select(i =>
        {
            var method = methods[i % methods.Length];
            var host = hosts[i % hosts.Length];
            var status = statusCodes[i % statusCodes.Length];

            return TrafficEntryBuilder.Get($"https://{host}/path/{i}")
                .WithSessionId(sid)
                .WithMethod(method)
                .WithStatusCode(status)
                .WithStartedAt(baseTime.AddMinutes(i))
                .WithDuration(TimeSpan.FromMilliseconds(50 + i * 10))
                .WithResponseBody($"{{\"index\":{i}}}")
                .Build();
        })];
    }

    // Creates an entry with a large body (for testing body size handling).
    public static TrafficEntry CreateLargeBodyEntry(int bodySizeBytes = 1_000_000)
    {
        var largeBody = new byte[bodySizeBytes];
        Random.Shared.NextBytes(largeBody);

        return new TrafficEntryBuilder()
            .WithRequestBodyBytes(largeBody, "application/octet-stream")
            .WithResponseBodyBytes(largeBody, "application/octet-stream")
            .Build();
    }
}
