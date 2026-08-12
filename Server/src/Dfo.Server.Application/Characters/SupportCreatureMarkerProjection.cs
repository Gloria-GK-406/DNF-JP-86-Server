using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Application.Characters;

/// <summary>Ports the legacy NOTI-2 creature lifetime precedence without leaking PVF into Networking.</summary>
internal static class SupportCreatureMarkerProjection
{
    public static uint Resolve(
        CreatureEquipmentLifetime lifetime,
        int coreExpireTime,
        int coreMarker16,
        DateTimeOffset preparedAt)
    {
        if (lifetime.DirectRemainingSeconds > 0)
            return lifetime.DirectRemainingSeconds;

        // The legacy loaded CreatureDetail has no persisted expiry field. Its projection
        // derives the effective expiry from core expiry, then an absolute marker, then
        // the equipment expiration policy (including [usable period]).
        var detailRemaining = coreExpireTime > 0
            ? Remaining(coreExpireTime, preparedAt)
            : coreMarker16 >= 1_000_000_000
                ? Remaining(coreMarker16, preparedAt)
                : lifetime.DetailFallbackRemainingSeconds;
        if (detailRemaining > 0) return detailRemaining;

        // Final writer fallback prefers any stored marker, then stored expiry.
        if (coreMarker16 > 0)
            return coreMarker16 >= 1_000_000_000
                ? Remaining(coreMarker16, preparedAt)
                : checked((uint)coreMarker16);
        return coreExpireTime > 0 ? Remaining(coreExpireTime, preparedAt) : 0;
    }

    private static uint Remaining(long expiresAt, DateTimeOffset preparedAt) =>
        checked((uint)Math.Clamp(expiresAt - preparedAt.ToUnixTimeSeconds(), 0, uint.MaxValue));
}
