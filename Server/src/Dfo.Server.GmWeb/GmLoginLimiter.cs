namespace Dfo.Server.GmWeb;

/// <remarks>
/// GmWeb keeps two instances: the GM login's (unkeyed) and the player login's (<see cref="PlayerKey"/>), so that
/// neither login can rate-limit the other, not even by filling the source slots (RQ-033).
/// </remarks>
internal sealed class GmLoginLimiter(TimeProvider clock)
{
    internal const string PlayerKey = "player";
    private readonly object sync = new();
    private readonly Dictionary<string, Window> sources = new(StringComparer.Ordinal);
    private static readonly TimeSpan Duration = TimeSpan.FromMinutes(15);
    internal const int SourceCapacity = 1024;
    internal const int NameCapacity = 128;

    public bool TryBegin(string source, string name, out Action<bool> complete)
    {
        complete = _ => { };
        lock (sync)
        {
            var now = clock.GetUtcNow();
            foreach (var expired in sources.Where(pair => pair.Value.Start + Duration <= now).Select(pair => pair.Key).ToArray()) sources.Remove(expired);
            if (!sources.TryGetValue(source, out var window))
            {
                if (sources.Count >= SourceCapacity) return false;
                sources.Add(source, window = new Window(now));
            }
            if (window.Attempts >= 20) return false;
            if (!window.Names.ContainsKey(name) && window.Names.Count >= NameCapacity) return false;
            window.Names.TryGetValue(name, out var attempts);
            if (attempts >= 5) return false;
            window.Attempts++; window.Names[name] = attempts + 1;
            var finished = false;
            complete = accepted =>
            {
                lock (sync)
                {
                    if (finished) return;
                    finished = true;
                    if (accepted) { window.Attempts--; window.Names[name]--; }
                }
            };
            return true;
        }
    }
    private sealed class Window(DateTimeOffset start)
    {
        public DateTimeOffset Start { get; } = start;
        public int Attempts { get; set; }
        public Dictionary<string, int> Names { get; } = new(StringComparer.Ordinal);
    }
}
