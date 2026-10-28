using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Protocol.Game;

internal static class ItemLifetimeOnTheWire
{
    // A21: zero selects the unlimited label and a negative value the expired one, so an
    // ended deadline must not clamp to zero or it would read as permanent.
    internal static int RemainingSeconds(ItemLifetime lifetime, DateTimeOffset preparedAt)
    {
        if (lifetime.IsPermanent) return 0;
        var remaining = (long)lifetime.Deadline - preparedAt.ToUnixTimeSeconds();
        return remaining <= 0 ? -1 : checked((int)remaining);
    }
}
