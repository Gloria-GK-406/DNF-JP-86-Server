using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Marriage;

internal sealed partial class MarriageDomainService
{
    public async ValueTask<MarriageCapsuleResult> CapsuleAsync(SelectedSessionIdentity actor, MarriageCapsuleAction action,
        IReadOnlyList<AccountId> accounts, IEffectFootprintRecorder footprint, CancellationToken ct)
    {
        footprint.DemandActive(actor);
        if (!eligibility.IsCurrent(actor) || await ledger.ForSessionAsync(actor, ct).ConfigureAwait(false) is not { Stage: MarriageStage.Married } relation
            || !accounts.SequenceEqual(CoupleAccounts(relation, actor))) return new(-1);
        footprint.DemandActive(actor);
        return await capsules.ExecuteAsync(actor, relation, action, footprint, ct).ConfigureAwait(false);
    }
}
