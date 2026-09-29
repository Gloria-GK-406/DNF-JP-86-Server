using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed record EquipmentReinforcementRuleInput(
    byte OldLevel,
    int FailureWeight,
    int PenaltyType,
    int PenaltyValue,
    int? ProtectionRetainLevel);

internal sealed record EquipmentReinforcementDecision(
    EquipmentReinforcementOutcome Outcome,
    byte NewLevel,
    bool ConsumeProtection);

/// <summary>
/// Resolves the normal PVF table's 100000-scale failure threshold. It has no asset effects.
/// </summary>
internal static class EquipmentReinforcementRules
{
    private const int WeightScale = 100_000;

    public static EquipmentReinforcementDecision Decide(
        EquipmentReinforcementRuleInput input, int draw)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(input.FailureWeight, WeightScale);
        ArgumentOutOfRangeException.ThrowIfNegative(input.FailureWeight);
        ArgumentOutOfRangeException.ThrowIfNegative(draw);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(draw, WeightScale);
        if (input.OldLevel >= byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(input), "No room for another reinforcement level.");
        if (input.PenaltyType is < 1 or > 3 || input.PenaltyValue < 0)
            throw new ArgumentOutOfRangeException(nameof(input), "Unsupported PVF penalty.");
        if (input.ProtectionRetainLevel is < 0)
            throw new ArgumentOutOfRangeException(nameof(input), "Invalid protection level.");

        if (draw >= input.FailureWeight)
            return new(EquipmentReinforcementOutcome.Succeeded,
                checked((byte)(input.OldLevel + 1)), false);

        return input.PenaltyType switch
        {
            1 => new(EquipmentReinforcementOutcome.Retained, input.OldLevel, false),
            2 => new(EquipmentReinforcementOutcome.Downgraded,
                (byte)Math.Max(0, input.OldLevel - input.PenaltyValue), false),
            3 when input.ProtectionRetainLevel is { } retain =>
                new(EquipmentReinforcementOutcome.Downgraded,
                    (byte)Math.Clamp(retain, 0, input.OldLevel), true),
            _ => new(EquipmentReinforcementOutcome.Destroyed, 0, false),
        };
    }
}
