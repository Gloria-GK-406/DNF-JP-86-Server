using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Marriage;

internal sealed partial class MarriageDomainService
{
    public async ValueTask InitializeAsync(AccountId account, CharacterId character, CancellationToken ct)
    {
        if (await ledger.ForParticipantAsync(account, character, ct).ConfigureAwait(false) is not { Stage: MarriageStage.Married } relation) return;
        await housingState.EnsureAsync(relation.Id, ct).ConfigureAwait(false);
        await ledger.DailyPointAsync(relation.Id, character, housing.Period, ct).ConfigureAwait(false);
    }
    public async ValueTask<HousingResult> ExecuteAsync(SelectedSessionIdentity actor, HousingRequest request,
        IReadOnlyList<AccountId> accounts, CancellationToken ct)
    {
        if (!eligibility.IsCurrent(actor) || await ledger.ForSessionAsync(actor, ct).ConfigureAwait(false) is not { Stage: MarriageStage.Married } relation
            || !accounts.SequenceEqual(CoupleAccounts(relation, actor))) return new(-4);
        var room = await housingState.EnsureAsync(relation.Id, ct).ConfigureAwait(false);
        HousingResult result;
        if (request.Action == HousingAction.UpgradeRing)
        {
            var level = await ledger.RingAsync(relation.Id, ct).ConfigureAwait(false);
            if (level >= 5 || request.Count != level + 1) return new(-6);
            var itemId = 10002611 + level;
            var spent = await housing.ConsumeAsync(actor, request.Slot, itemId, ct).ConfigureAwait(false);
            if (spent is null) return new(-7);
            if (!await ledger.UpgradeRingAsync(relation.Id, level, ct).ConfigureAwait(false))
                throw new HousingRejectedException(-6);
            result = new(0) { RingLevel = checked((byte)(level + 1)), ConsumedSlot = spent.Value.Slot, Remaining = spent.Value.Remaining };
        }
        else result = await housing.ExecuteAsync(actor, relation, room, request, ct).ConfigureAwait(false);
        return result with { Audience = [relation.Proposer.CharacterId, relation.Recipient.CharacterId] };
    }

    public async ValueTask<NpcShopPurchaseResult> PurchaseAsync(SelectedSessionIdentity actor,
        int itemId, int count, int shopId, IReadOnlyList<AccountId> accounts, CancellationToken ct)
    {
        if (!eligibility.IsCurrent(actor) || await ledger.ForSessionAsync(actor, ct).ConfigureAwait(false) is not { Stage: MarriageStage.Married } relation
            || !accounts.SequenceEqual(CoupleAccounts(relation, actor))) return NpcShopPurchaseResult.Refused(NpcShopRejection.ShopUnavailable);
        var room = await housingState.EnsureAsync(relation.Id, ct).ConfigureAwait(false);
        if (!housingState.IsInside(actor, room)) return NpcShopPurchaseResult.Refused(NpcShopRejection.ShopUnavailable);
        var prepared = await housing.PreparePurchaseAsync(actor, room, itemId, count, shopId, ct).ConfigureAwait(false);
        if (prepared.Plan is not { } plan) return NpcShopPurchaseResult.Refused(prepared.Rejection);
        if (await ledger.PointsAsync(relation.Id, actor.CharacterId, ct).ConfigureAwait(false) < plan.PointCost)
            return NpcShopPurchaseResult.Refused(NpcShopRejection.InsufficientMaterials);
        if (plan.PointCost > 0 && !await ledger.ChangePointsAsync(relation.Id, actor.CharacterId, -plan.PointCost, ct).ConfigureAwait(false))
            return NpcShopPurchaseResult.Refused(NpcShopRejection.InsufficientMaterials);
        var result = await housing.CompletePurchaseAsync(actor, plan, ct).ConfigureAwait(false);
        if (result.Receipt is null) throw new InvalidOperationException("A completed housing purchase needs its receipt.");
        return result with { Receipt = result.Receipt with
        {
            HousingAudience = plan.ChangedRoom is null ? [] : [relation.Proposer.CharacterId, relation.Recipient.CharacterId],
            LovePoints = await ledger.PointsAsync(relation.Id, actor.CharacterId, ct).ConfigureAwait(false),
        } };
    }
}
