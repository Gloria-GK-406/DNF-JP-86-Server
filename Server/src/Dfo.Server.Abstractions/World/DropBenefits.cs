using Dfo.Server.Abstractions.Benefits;

namespace Dfo.Server.Abstractions.World;

public sealed record DropDrawMultiplierBenefit(DropDrawMultipliers Multipliers) : IGameplayBenefitEffect;

public readonly record struct BossEquipmentDropBenefit(int Percent) : IGameplayBenefitEffect;

public readonly record struct QuestHelperDropBenefit : IGameplayBenefitEffect;

/// <summary>Additional shared equipment rounds in half-round units, for one rarity.</summary>
public readonly record struct SharedEquipmentDropBenefit : IGameplayBenefitEffect
{
    public SharedEquipmentDropBenefit(DropRarity rarity, int halfRounds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(halfRounds);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(halfRounds, DropDrawMultipliers.Maximum * 2);
        Rarity = rarity;
        HalfRounds = halfRounds;
    }

    public DropRarity Rarity { get; }
    public int HalfRounds { get; }
}
