using System.Collections.Concurrent;

namespace SecureChatServer.Security;

public sealed class RateLimiterService
{
    private sealed class Counter
    {
        public int Count;
        public DateTime WindowStartUtc;
    }

    private readonly ConcurrentDictionary<string, Counter> _counters = new(StringComparer.Ordinal);

    public bool IsAllowed(string scope, string actor, int maxRequests, TimeSpan window)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        var now = DateTime.UtcNow;
        var key = $"{scope}:{actor}";

        var counter = _counters.GetOrAdd(key, _ => new Counter
        {
            Count = 0,
            WindowStartUtc = now
        });

        lock (counter)
        {
            if (now - counter.WindowStartUtc >= window)
            {
                counter.Count = 0;
                counter.WindowStartUtc = now;
            }

            counter.Count++;
            return counter.Count <= maxRequests;
        }
    }
}
