using Dfo.Server.Abstractions.PlayerPortal;
using Dfo.Server.Data.PlayerPortal;

namespace Dfo.Server.Application.PlayerPortal;

/// <summary>
/// The one reading of "used today": what <c>/me</c> shows and what the limits are judged by. Cera is always read per
/// currency, because each currency has its own daily limit; only mail counts across the kind.
/// </summary>
internal static class PlayerPortalDailyUsage
{
    internal static async ValueTask<long> CeraAsync(IPlayerPortalPersistence portal, int accountId, string currency, DateOnly day,
        CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrEmpty(currency);
        return (await portal.UsageAsync(accountId, PlayerPortalOperationKind.Cera, currency, day, token).ConfigureAwait(false)).Amount;
    }

    internal static async ValueTask<long> MailsAsync(IPlayerPortalPersistence portal, int accountId, DateOnly day, CancellationToken token) =>
        (await portal.UsageAsync(accountId, PlayerPortalOperationKind.Mail, null, day, token).ConfigureAwait(false)).Count;
}
