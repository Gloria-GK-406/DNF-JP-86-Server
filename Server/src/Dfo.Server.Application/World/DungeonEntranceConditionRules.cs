using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

/// <summary>
/// Whether a run has hunted every conditional monster its <c>[boss room entrance condition]</c>
/// placed, read off the run's committed kills.
/// </summary>
/// <remarks>
/// A target counts only when its code dies in the room it was placed in (ServerS4A21
/// <c>SpecialDungeonMechanismApplicationService</c> 528-573). The A21 client keeps no count of its
/// own: the only reader of its parsed condition type tests for destroy-passive-object
/// (0x0177920A), so the hunt is the server's to judge. Called only through
/// <see cref="IDungeonSettlementRuleService"/>.
/// </remarks>
internal static class DungeonEntranceConditionRules
{
    public static bool AreMet(DungeonEntrancePlan plan, IReadOnlyCollection<DungeonEntranceKill> kills)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(kills);
        // Targets sharing a code and a room each need their own kill there.
        return plan.Targets.Count > 0 && plan.Targets
            .GroupBy(target => (target.Code, target.Cell))
            .All(group => kills.Count(kill => kill.Code == group.Key.Code && kill.Cell == group.Key.Cell) >= group.Count());
    }

    /// <summary>Whether <paramref name="kill"/> is the one that turns an unmet plan into a met one.</summary>
    public static bool IsCompletedBy(DungeonEntrancePlan plan, IReadOnlyCollection<DungeonEntranceKill> before,
        DungeonEntranceKill kill) =>
        !AreMet(plan, before) && AreMet(plan, [.. before, kill]);
}
