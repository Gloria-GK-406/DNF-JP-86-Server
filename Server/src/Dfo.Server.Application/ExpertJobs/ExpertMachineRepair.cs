using Dfo.Server.Abstractions.ExpertJobs;

namespace Dfo.Server.Application.ExpertJobs;

internal static class ExpertMachineRepair
{
    internal static (int Cost, int Endurance)? Quote(ExpertMachineRepairRule rule, int endurance, int gold)
    {
        if (endurance < 0 || endurance >= rule.MaximumEndurance || rule.FullCost <= 0 || gold <= 0)
            return null;
        var fullRepair = rule.FullCost - (int)((long)endurance * rule.FullCost / rule.MaximumEndurance);
        if (gold >= fullRepair) return (fullRepair, rule.MaximumEndurance);
        var unitCost = rule.FullCost / rule.MaximumEndurance;
        if (unitCost <= 0 || gold < unitCost) return null;
        var points = gold / unitCost;
        return (points * unitCost, (int)Math.Min(rule.MaximumEndurance, (long)endurance + points));
    }
}
