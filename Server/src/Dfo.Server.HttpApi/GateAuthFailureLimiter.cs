using System.Net;

namespace Dfo.Server.HttpApi;

/// <summary>
/// Fixed-window count of gate token failures per connection address. Unlike <c>GmLoginLimiter</c>
/// it never counts successes or account names, and it only decides between 401 and 429: a valid
/// token is never consulted against it.
/// </summary>
internal sealed class GateAuthFailureLimiter(TimeProvider clock, GateOptions options)
{
    internal const int Capacity = 4096;
    private const string UnknownSource = "unknown";

    private readonly object sync = new();
    private readonly Dictionary<string, Window> sources = new(StringComparer.Ordinal);

    public bool IsLimited(IPAddress? source)
    {
        var key = KeyOf(source);
        lock (sync)
        {
            return sources.TryGetValue(key, out var window)
                && !IsExpired(window, clock.GetUtcNow())
                && window.Failures >= options.AuthFailureLimit;
        }
    }

    public void RecordFailure(IPAddress? source)
    {
        var key = KeyOf(source);
        lock (sync)
        {
            var now = clock.GetUtcNow();
            if (sources.TryGetValue(key, out var window) && !IsExpired(window, now))
            {
                window.Failures++;
                return;
            }

            if (window is null && sources.Count >= Capacity) MakeRoom(now);
            sources[key] = new Window(now) { Failures = 1 };
        }
    }

    // Full means a flood of distinct sources; dropping the oldest window keeps memory bounded
    // without ever turning capacity into a reason to refuse a request.
    private void MakeRoom(DateTimeOffset now)
    {
        foreach (var expired in sources.Where(pair => IsExpired(pair.Value, now)).Select(pair => pair.Key).ToArray())
        {
            sources.Remove(expired);
        }

        if (sources.Count < Capacity) return;
        var earliest = sources.MinBy(pair => pair.Value.Start).Key;
        sources.Remove(earliest);
    }

    private bool IsExpired(Window window, DateTimeOffset now) => window.Start + options.AuthFailureWindow <= now;

    private static string KeyOf(IPAddress? source) =>
        source is null
            ? UnknownSource
            : (source.IsIPv4MappedToIPv6 ? source.MapToIPv4() : source).ToString();

    private sealed class Window(DateTimeOffset start)
    {
        public DateTimeOffset Start { get; } = start;
        public int Failures { get; set; }
    }
}
