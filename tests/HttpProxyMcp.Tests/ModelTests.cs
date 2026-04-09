using HttpProxyMcp.Core.Models;

namespace HttpProxyMcp.Tests;

public class ModelTests
{
	[Fact]
	public void TrafficEntry_Duration_CalculatedFromTimestamps()
	{
		var entry = new TrafficEntry
		{
			StartedAt = DateTimeOffset.UtcNow,
			CompletedAt = DateTimeOffset.UtcNow.AddMilliseconds(250)
		};

		Assert.NotNull(entry.Duration);
		Assert.InRange(entry.Duration.Value.TotalMilliseconds, 240, 260);
	}

	[Fact]
	public void TrafficEntry_Duration_NullWhenNotCompleted()
	{
		var entry = new TrafficEntry
		{
			StartedAt = DateTimeOffset.UtcNow
		};

		Assert.Null(entry.Duration);
	}

	[Fact]
	public void ProxySession_IsActive_WhenNotClosed()
	{
		var session = new ProxySession
		{
			Id = Guid.NewGuid(),
			Name = "test",
			CreatedAt = DateTimeOffset.UtcNow
		};

		Assert.True(session.IsActive);
	}

	[Fact]
	public void ProxySession_IsNotActive_WhenClosed()
	{
		var session = new ProxySession
		{
			Id = Guid.NewGuid(),
			Name = "test",
			CreatedAt = DateTimeOffset.UtcNow,
			ClosedAt = DateTimeOffset.UtcNow
		};

		Assert.False(session.IsActive);
	}

	[Fact]
	public void TrafficFilter_DefaultLimit_Is50()
	{
		var filter = new TrafficFilter();
		Assert.Equal(50, filter.Limit);
	}

	[Fact]
	public void ProxyConfiguration_Defaults()
	{
		var config = new ProxyConfiguration();
		Assert.Equal(8080, config.Port);
		Assert.True(config.EnableSsl);
		Assert.Equal(10 * 1024 * 1024, config.MaxBodyCaptureBytes);
	}
}
