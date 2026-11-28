using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.World;

internal sealed class DungeonFatigueRuleService
{
    public ushort? BaseCost(DungeonLayout layout, bool isEntry) =>
        layout.ConsumesFatigue && (isEntry || layout.EntryOnlyFatigue is null)
            ? layout.EntryOnlyFatigue ?? 1
            : null;

    public DungeonFatigueChargeRule Quote(ushort baseCost, ImmutableArray<FatigueCostMultiplier> effects)
    {
        decimal regular = baseCost;
        decimal fullChargeFactor = 1;
        var requiresFullCharge = false;
        foreach (var effect in effects)
        {
            if (effect.Factor <= 0)
                throw new InvalidOperationException("Fatigue cost multipliers must be positive.");
            if (effect.RequireFullCharge)
            {
                fullChargeFactor *= effect.Factor;
                requiresFullCharge = true;
            }
            else
                regular *= effect.Factor;
        }

        var fullCost = decimal.Ceiling(regular * fullChargeFactor);
        return new(decimal.Ceiling(regular),
            requiresFullCharge && fullCost is > 0 and <= ushort.MaxValue ? (ushort)fullCost : null);
    }
}

internal readonly record struct DungeonFatigueChargeRule(decimal RegularCost, ushort? FullCost)
{
    public CharacterFatigueCharge Resolve(CharacterId character, ushort? remaining) =>
        FullCost is { } full && remaining >= full
            ? new(character, full, RequireFullCharge: true)
            : new(character, checked((ushort)RegularCost));
}
