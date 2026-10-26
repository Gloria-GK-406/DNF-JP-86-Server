using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.StateSynchronization;

public sealed record PremiumSnapshotResult(AccountEntitlementEntryState Entitlements,
    IReadOnlyList<CharacterPremiumUsageSlotState> Usage, DateTimeOffset PreparedAt, bool IsCurrent = true);

internal static class PremiumSnapshotReader
{
    internal static async ValueTask<PremiumSnapshotResult> ReadAsync(ISelectedSessionDomainService sessions,
        IPremiumServiceDomainService premiums, IPremiumServiceUsageDomainService usage, TimeProvider clock,
        SelectedSessionIdentity session, CancellationToken token)
    {
        if (!sessions.IsInWorld(session)) return new(new([]), [], clock.GetUtcNow(), false);
        return new(new(await premiums.For(session.AccountId).ReadAsync(token).ConfigureAwait(false)),
            await usage.ReadAsync(session.CharacterId, token).ConfigureAwait(false), clock.GetUtcNow());
    }
}
