namespace Dfo.Server.GmWeb;

/// <summary>
/// How many of one account's player requests may wait on the gameplay executor at once (CH-1). Only the routes that
/// run as an executor CMD count: the login has its own limiter, and the catalog, icons and tooltips stay off the
/// executor. A request over the cap is refused before it is queued, so it never runs and is not audited.
/// </summary>
/// <remarks>
/// A slot is held until the request's handler returns. The CMD is queued with <see cref="CancellationToken.None"/>
/// and the handler awaits it even when the client goes away, so a slot is never freed while its CMD is still queued.
/// </remarks>
internal sealed class PlayerRequestLimiter
{
    internal const int PerAccount = 2;
    private readonly object sync = new();
    private readonly Dictionary<int, int> inFlight = [];

    public bool TryAcquire(int accountId)
    {
        lock (sync)
        {
            inFlight.TryGetValue(accountId, out var count);
            if (count >= PerAccount) return false;
            inFlight[accountId] = count + 1;
            return true;
        }
    }

    public void Release(int accountId)
    {
        lock (sync)
        {
            if (!inFlight.TryGetValue(accountId, out var count)) return;
            if (count <= 1) inFlight.Remove(accountId); else inFlight[accountId] = count - 1;
        }
    }

    internal int InFlight(int accountId)
    {
        lock (sync) return inFlight.GetValueOrDefault(accountId);
    }
}
