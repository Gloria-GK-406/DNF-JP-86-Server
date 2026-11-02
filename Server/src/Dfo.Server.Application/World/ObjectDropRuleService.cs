using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World;

internal sealed class ObjectDropRuleService(
    IDropRateTableSource rates, IGoldDropTableSource gold,
    IDropItemPoolSource pools, DropRateOptions options) : IObjectDropRuleService
{
    // Fixed 10000 tickets; omit the legacy MT inclusive-endpoint quirk (tutorial weight 10000 is certain).
    internal static ObjectDropEntry? Select(IReadOnlyList<ObjectDropEntry> entries, int ticket)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ticket);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(ticket, 10000);
        long cumulative = 0;
        foreach (var entry in entries)
        {
            cumulative += entry.Weight;
            if (ticket < cumulative) return entry;
        }
        return null;
    }

    public IReadOnlyList<DungeonRoomDrop> Draw(
        IReadOnlyList<ObjectDropDefinition> definitions, DungeonCellCreationContext context)
    {
        var result = new List<DungeonRoomDrop>();
        foreach (var definition in definitions)
        {
            var seed = DropRandom.SeedFor(context.Layout.Seed, context.MapId,
                context.Cell.X, context.Cell.Y, definition.ObjectIndex)
                ^ 0x4F424A454354UL ^ ((ulong)(uint)definition.ActionIndex << 32);
            var random = new DropRandom(seed);
            var level = definition.LevelOverride == -1 ? context.Layout.BasisLevel : definition.LevelOverride;
            for (var attempt = 0; attempt < definition.RandomAttempts; attempt++)
                DrawRandom(definition.ObjectIndex, level, context.Difficulty, random, result, null);
            DrawSpecific(definition, random, result);
            var multipliers = context.DrawMultipliers;
            foreach (var (entry, isGold) in new[] { (DropDrawEntry.ObjectRandomGold, true), (DropDrawEntry.ObjectRandomItem, false) })
                for (var round = 1; round < multipliers.For(entry); round++)
                {
                    var extra = new DropRandom(DropRandom.ForRound(seed, new(entry, round)));
                    for (var attempt = 0; attempt < definition.RandomAttempts; attempt++)
                        DrawRandom(definition.ObjectIndex, level, context.Difficulty, extra, result, isGold);
                }
            for (var round = 1; round < multipliers.For(DropDrawEntry.ObjectSpecific); round++)
                DrawSpecific(definition, new DropRandom(
                    DropRandom.ForRound(seed, new(DropDrawEntry.ObjectSpecific, round))), result);
        }
        return result;
    }

    private static void DrawSpecific(ObjectDropDefinition definition, DropRandom random, List<DungeonRoomDrop> result)
    {
        for (var attempt = 0; attempt < definition.SpecificAttempts; attempt++)
            if (Select(definition.Entries, random.Next(10000) - 1) is { } item)
                result.Add(new(definition.ObjectIndex, item.ItemId, item.Durability));
    }

    /// <param name="onlyGold">Null draws every slot; otherwise only the gold slots, or only the others.</param>
    private void DrawRandom(byte objectIndex, int level, DungeonDifficulty difficulty,
        DropRandom random, List<DungeonRoomDrop> result, bool? onlyGold)
    {
        var table = rates.Load(DropRateTables.BreakableObject);
        if (table.BandFor(level) is not { } band) return;
        // Objects use their own table, rank 0 and one participant; no monster/Extra/world tap.
        var chances = DropRules.RatesFor(table, band, 0, difficulty, 1, options.RateBasis);
        if (onlyGold is { } wantGold)
            chances = chances with { Slots = chances.Slots
                .Where(slot => DropRules.KindOf(slot.Slot) == DropItemKind.Gold == wantGold).ToArray() };
        foreach (var slot in DropRules.Gate(chances, random))
        {
            var kind = DropRules.KindOf(slot);
            if (kind == DropItemKind.Gold)
            {
                var amount = DropRules.GoldAmount(gold.Load(DropRateTables.BreakableObject), level, 0, random);
                if (amount > 0) result.Add(new(objectIndex, RolledDrop.GoldItemId, 0, amount));
            }
            else if (kind != DropItemKind.Avatar && table.GradeBandFor(level) is { } grades)
            {
                var rarity = DropRules.RollRarity(table, slot, random);
                var family = kind == DropItemKind.Equipment ? DropItemFamily.Equipment : DropItemFamily.Stackable;
                if (DropRules.Pick(pools.Load(family), grades, rarity, random) is { } item)
                    result.Add(new(objectIndex, item.ItemId, item.Durability));
            }
        }
    }
}
