namespace Dfo.Server.Application.Inventory;

/// <summary>The reinforcement attempt only compares the PVF failure threshold and the explicit draw.</summary>
internal static class EquipmentReinforcementRules
{
    public static bool Succeeds(int failureWeight, int draw)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(failureWeight, 100_000);
        ArgumentOutOfRangeException.ThrowIfNegative(failureWeight);
        ArgumentOutOfRangeException.ThrowIfNegative(draw);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(draw, 100_000);
        return draw >= failureWeight;
    }
}