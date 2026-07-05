#nullable enable
using System;
using System.Collections.Generic;

namespace BTCPayServer.Ndeet.Plugins.AmbassadorToolbox;

public class MerchantReportSubmissionThrottle
{
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(1);

    private readonly Dictionary<string, DateTimeOffset> _nextAllowedSubmissions = new(StringComparer.Ordinal);
    private readonly object _lock = new();
    private DateTimeOffset _lastCleanup = DateTimeOffset.MinValue;

    public bool TryConsume(string key, DateTimeOffset now, out DateTimeOffset retryAt)
    {
        lock (_lock)
        {
            CleanupExpiredEntries(now);

            if (_nextAllowedSubmissions.TryGetValue(key, out retryAt) && retryAt > now)
                return false;

            retryAt = now.Add(Cooldown);
            _nextAllowedSubmissions[key] = retryAt;
            return true;
        }
    }

    private void CleanupExpiredEntries(DateTimeOffset now)
    {
        if (now - _lastCleanup < Cooldown)
            return;

        _lastCleanup = now;

        List<string>? expiredKeys = null;
        foreach (var entry in _nextAllowedSubmissions)
        {
            if (entry.Value > now)
                continue;

            expiredKeys ??= [];
            expiredKeys.Add(entry.Key);
        }

        if (expiredKeys is null)
            return;

        foreach (var key in expiredKeys)
            _nextAllowedSubmissions.Remove(key);
    }
}
