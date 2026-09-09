namespace Dfo.Server.Protocol.Game;

internal static class AvatarLifetimeOnTheWire
{
    // A21: zero selects the unlimited label; a negative value selects expired.
    // int.MaxValue was this server's former permanent placeholder. Keep existing
    // items usable without rewriting their stored facts or purchase history.
    internal static int RemainingSeconds(int expiresAt, DateTimeOffset preparedAt)
    {
        if (expiresAt <= 0 || expiresAt == int.MaxValue) return 0;
        var remaining = (long)expiresAt - preparedAt.ToUnixTimeSeconds();
        return remaining <= 0 ? -1 : checked((int)Math.Min(remaining, int.MaxValue));
    }
}
