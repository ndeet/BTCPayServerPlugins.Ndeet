using System;
using BTCPayServer.Ndeet.Plugins.AmbassadorToolbox;
using Xunit;

namespace BTCPayServer.Plugins.Tests.AmbassadorToolbox;

public class MerchantReportSubmissionThrottleTests
{
    [Fact]
    public void TryConsume_RejectsSameKeyDuringCooldown()
    {
        var throttle = new MerchantReportSubmissionThrottle();
        var now = DateTimeOffset.Parse("2026-07-05T10:00:00Z");

        Assert.True(throttle.TryConsume("203.0.113.1", now, out var firstRetryAt));
        Assert.Equal(now.Add(MerchantReportSubmissionThrottle.Cooldown), firstRetryAt);

        Assert.False(throttle.TryConsume("203.0.113.1", now.AddSeconds(30), out var secondRetryAt));
        Assert.Equal(firstRetryAt, secondRetryAt);
    }

    [Fact]
    public void TryConsume_AllowsSameKeyAfterCooldown()
    {
        var throttle = new MerchantReportSubmissionThrottle();
        var now = DateTimeOffset.Parse("2026-07-05T10:00:00Z");

        Assert.True(throttle.TryConsume("203.0.113.1", now, out _));

        var afterCooldown = now.Add(MerchantReportSubmissionThrottle.Cooldown);
        Assert.True(throttle.TryConsume("203.0.113.1", afterCooldown, out var retryAt));
        Assert.Equal(afterCooldown.Add(MerchantReportSubmissionThrottle.Cooldown), retryAt);
    }

    [Fact]
    public void TryConsume_TracksKeysIndependently()
    {
        var throttle = new MerchantReportSubmissionThrottle();
        var now = DateTimeOffset.Parse("2026-07-05T10:00:00Z");

        Assert.True(throttle.TryConsume("203.0.113.1", now, out _));
        Assert.True(throttle.TryConsume("203.0.113.2", now.AddSeconds(10), out _));
    }
}
