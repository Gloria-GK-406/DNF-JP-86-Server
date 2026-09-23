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
            var random = new DropRandom(DropRandom.SeedFor(context.Layout.Seed, context.MapId,
                context.Cell.X, context.Cell.Y, definition.ObjectIndex)
                ^ 0x4F424A454354UL ^ ((ulong)(uint)definition.ActionIndex << 32));
            var level = definition.LevelOverride == -1 ? context.Layout.BasisLevel : definition.LevelOverride;
            for (var attempt = 0; attempt < definition.RandomAttempts; attempt++)
                DrawRandom(definition.ObjectIndex, level, context.Difficulty, random, result);
            for (var attempt = 0; attempt < definition.SpecificAttempts; attempt++)
                if (Select(definition.Entries, random.Next(10000) - 1) is { } item)
                    result.Add(new(definition.ObjectIndex, item.ItemId, item.Durability));
        }
        return result;
    }

    private void DrawRandom(byte objectIndex, int level, DungeonDifficulty difficulty,
        DropRandom random, List<DungeonRoomDrop> result)
    {
        var table = rates.Load(DropRateTables.BreakableObject);
        if (table.BandFor(level) is not { } band) return;
        // Objects use their own table, rank 0 and one participant; no monster/Extra/world tap.
        var chances = DropRules.RatesFor(table, band, 0, difficulty, 1, options.RateBasis);
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
