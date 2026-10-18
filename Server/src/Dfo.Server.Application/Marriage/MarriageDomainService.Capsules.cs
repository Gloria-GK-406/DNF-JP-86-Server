using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Marriage;

internal sealed partial class MarriageDomainService
{
    public async ValueTask<MarriageCapsuleResult> CapsuleAsync(SelectedSessionIdentity actor, MarriageCapsuleAction action,
        IReadOnlyList<AccountId> accounts, CancellationToken ct)
    {
        if (!eligibility.IsCurrent(actor) || await ledger.ForSessionAsync(actor, ct).ConfigureAwait(false) is not { Stage: MarriageStage.Married } relation
            || !accounts.SequenceEqual(CoupleAccounts(relation, actor))) return new(-1);
        return await capsules.ExecuteAsync(actor, relation, action, ct).ConfigureAwait(false);
    }
}
