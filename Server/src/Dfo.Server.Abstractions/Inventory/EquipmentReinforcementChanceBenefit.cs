using Dfo.Server.Abstractions.Benefits;

namespace Dfo.Server.Abstractions.Inventory;

/// <summary>Adds success units on the NPC reinforcement roll's 100,000-unit scale.</summary>
public readonly record struct EquipmentReinforcementChanceBenefit : IGameplayBenefitEffect
{
    public EquipmentReinforcementChanceBenefit(int successUnits, string? exclusiveGroup = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(successUnits);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(successUnits, 100_000);
        SuccessUnits = successUnits;
        ExclusiveGroup = exclusiveGroup;
    }

    public int SuccessUnits { get; }
    public string? ExclusiveGroup { get; }
}
