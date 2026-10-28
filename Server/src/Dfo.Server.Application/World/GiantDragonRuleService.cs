using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

internal sealed class GiantDragonRuleService(IDungeonDefinitionSource dungeons, ITownDefinitionSource towns,
    GiantDragonOptions options, IItemCatalogSource items) : IGiantDragonRuleService
{
    public bool Supports(int id) => id is >= 3200 and <= 3202 or >= 3536 and <= 3540;
    public bool IsOpen(int id) => Supports(id) && options.Enabled && (id < 3536 || options.RestoredVariantsEnabled);

    public async ValueTask<GiantDragonEntryRule> EntryAsync(int id, CancellationToken ct)
    {
        if (!Supports(id)) throw new ArgumentOutOfRangeException(nameof(id));
        var definition = await dungeons.GetAsync(id, ct).ConfigureAwait(false);
        var restored = id >= 3536;
        var townId = restored ? 100 : 18;
        var areaId = restored ? 2 : 0;
        var gate = (await towns.GetAsync(townId, ct).ConfigureAwait(false))?.AreaAt(areaId);
        if (definition is null || definition.EntryMode != DungeonEntryMode.Maze
            || definition.PartyLimit != (id == 3540 ? 1 : 3) || !definition.ProhibitsPractice
            || gate is not { Kind: TownAreaKind.DungeonGate } || gate.Operand1 != (restored ? 98 : 30))
            throw new InvalidDataException("Giant dragon dungeon or native entrance is missing/incompatible.");
        // Local restoration policy: 3538 must remain accessible while 20728 is active,
        // because completing that quest itself requires clearing 3538. WDM pairs alone
        // do not prove the client's historical quest-gate algorithm.
        int? quest = id switch { 3536 => 20720, 3537 => 20722, 3538 or 3539 => 20728, 3540 => 20727, _ => null };
        GiantDragonTimeRewardPlan? reward = null;
        if (!restored)
        {
            var material = await items.FindAsync(3326, ct).ConfigureAwait(false);
            if (material is not { ItemId: 3326, Category: "material", EquipmentType: null }
                || !Enum.IsDefined((DropRarity)(material.Rarity ?? 0)))
                throw new InvalidDataException("Giant dragon breath material is missing/incompatible.");
            // User-approved approximation: nearest HUD tick, with ties taking the lower award.
            // The 100-second green gauge has eight equal intervals; this is not the native award algorithm.
            // See Document/tasks/giant-dragon/TimeAttackReward.md.
            reward = new(new(3326, 1, (DropRarity)(material.Rarity ?? 0), DropItemKind.Stackable),
                9, 3, TimeSpan.FromMilliseconds(18750), TimeSpan.FromMilliseconds(12500));
        }
        return new(definition, townId, areaId, quest, id == 3538) { TimeReward = reward };
    }

    public IReadOnlyList<RolledDrop> ResolveTimeReward(GiantDragonTimeRewardPlan plan, TimeSpan elapsed,
        IReadOnlyList<RolledDrop> pending)
    {
        var count = GiantDragonTimeRewardRules.CountFor(plan, elapsed);
        return pending.Where(drop => drop.ItemId != plan.Material.ItemId
                || drop.OwnerCharacterId is not null || drop.Delivery != RolledDropDelivery.Ground)
            .Concat(Enumerable.Repeat(plan.Material with { Count = 1 }, count)).ToArray();
    }
}

internal static class GiantDragonTimeRewardRules
{
    internal static int CountFor(GiantDragonTimeRewardPlan plan, TimeSpan elapsed)
    {
        var lost = elapsed < plan.FirstDecreaseAt ? 0
            : 1 + (elapsed.Ticks - plan.FirstDecreaseAt.Ticks) / plan.StepPeriod.Ticks;
        return (int)Math.Max(plan.MinimumCount, plan.MaximumCount - lost);
    }
}
