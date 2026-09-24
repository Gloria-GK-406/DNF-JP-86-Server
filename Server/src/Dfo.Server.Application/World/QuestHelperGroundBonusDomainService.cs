using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

internal sealed class QuestHelperGroundBonusDomainService(
    IPremiumServiceDomainService premiums,
    IPremiumServiceRuleService rules,
    IQuestMaterialRequirementDomainService quests) : IQuestHelperGroundBonusDomainService
{
    public async ValueTask<DungeonCellSeed> ApplyAsync(DungeonCellCreationContext context,
        DungeonCellSeed cell, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var active = false;
        foreach (var account in context.Participants.Select(p => p.AccountId).Distinct())
            if (await premiums.For(account).HasBenefitAsync(PremiumBenefit.QuestItemDropBonus, cancellationToken).ConfigureAwait(false))
            {
                active = true;
                break;
            }
        if (!active) return cell;
        var materials = new HashSet<int>();
        foreach (var member in context.Participants)
            materials.UnionWith(await quests.GetOutstandingMaterialIdsAsync(member.AccountId, member.CharacterId,
                cancellationToken).ConfigureAwait(false));
        if (materials.Count == 0) return cell;

        int Bonus(int identity, int itemId, ulong channel) => rules.DrawQuestHelperBonusCount(
            DropRandom.SeedFor(context.Layout.Seed, context.MapId, context.Cell.X, context.Cell.Y, identity)
            ^ channel ^ ((ulong)(uint)itemId * 0x9E3779B97F4A7C15UL));

        // One extra roll per material and source, not per participant or duplicate drop entry.
        var actors = cell.Actors.Select(actor =>
        {
            var seen = new HashSet<int>();
            return actor with { PendingLoot = actor.PendingLoot.Select(drop =>
                drop.Delivery == RolledDropDelivery.Ground && drop.OwnerCharacterId is null
                    && drop.Kind == DropItemKind.Stackable && drop.Count > 0 && drop.ItemId > 0
                    && materials.Contains(drop.ItemId) && seen.Add(drop.ItemId)
                ? drop with { Count = checked(drop.Count + Bonus(actor.Sequence, drop.ItemId, 0x51484143544FUL)) }
                : drop).ToArray() };
        }).ToArray();
        var objectMaterials = new HashSet<(DungeonGroundItemSource, int)>();
        var ground = cell.GroundItems.Select(drop =>
            drop.Source.Kind == DungeonGroundItemSourceKind.DeclaredObject && !drop.IsEquipment
                && drop.OwnerCharacterId is null && drop.Count > 0 && drop.ItemId > 0
                && materials.Contains(drop.ItemId) && objectMaterials.Add((drop.Source, drop.ItemId))
            ? drop with { Count = checked(drop.Count + Bonus(drop.Source.ObjectIndex ?? drop.Number,
                drop.ItemId, 0x51484F424AUL)) }
            : drop).ToArray();
        // Object personal rewards remain untouched, just like actor DirectQuestItem rewards.
        return cell with { Actors = actors, GroundItems = ground };
    }
}
