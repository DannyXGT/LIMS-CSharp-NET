using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Lims.Api.Authentication;

public sealed class LoginAttemptTracker(TimeProvider timeProvider)
{
    private static readonly TimeSpan ObservationWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(30);
    private const int MaximumFailures = 5;
    private readonly ConcurrentDictionary<string, AttemptState> _attempts = new(StringComparer.Ordinal);

    public bool IsAllowed(string identifier, string? ipAddress)
    {
        var now = timeProvider.GetUtcNow();
        var state = _attempts.GetOrAdd(CreateKey(identifier, ipAddress), static _ => new AttemptState());
        lock (state)
        {
            ResetExpiredWindow(state, now);
            return state.BlockedUntil is null || now >= state.BlockedUntil;
        }
    }

    public void RecordFailure(string identifier, string? ipAddress)
    {
        var now = timeProvider.GetUtcNow();
        var state = _attempts.GetOrAdd(CreateKey(identifier, ipAddress), static _ => new AttemptState());
        lock (state)
        {
            ResetExpiredWindow(state, now);
            state.WindowStartedAt ??= now;
            state.Failures++;
            if (state.Failures >= MaximumFailures)
            {
                state.BlockedUntil = now.Add(Cooldown);
            }
        }
    }

    public void Reset(string identifier, string? ipAddress) =>
        _attempts.TryRemove(CreateKey(identifier, ipAddress), out _);

    private static string CreateKey(string identifier, string? ipAddress)
    {
        var material = string.Concat(ipAddress ?? "unknown", "|", identifier.Trim().ToLowerInvariant());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    private static void ResetExpiredWindow(AttemptState state, DateTimeOffset now)
    {
        if (state.WindowStartedAt is not null && now - state.WindowStartedAt >= ObservationWindow)
        {
            state.WindowStartedAt = null;
            state.BlockedUntil = null;
            state.Failures = 0;
        }
    }

    private sealed class AttemptState
    {
        public DateTimeOffset? WindowStartedAt { get; set; }
        public DateTimeOffset? BlockedUntil { get; set; }
        public int Failures { get; set; }
    }
}
