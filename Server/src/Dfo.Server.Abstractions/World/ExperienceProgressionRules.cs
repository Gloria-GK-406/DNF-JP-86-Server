using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.World;

internal static class ExperienceProgressionRules
{
    internal static ExperienceAwardDecision Decide(CharacterProgressionState before,
        ExperienceBreakdown breakdown, IReadOnlyList<uint> thresholds)
    {
        if (breakdown.Total == 0) return new(before, ExperienceAward.Nothing(before.Level, before.Experience));
        var total = (ulong)before.Experience + breakdown.Total;
        if (thresholds.Count > 0) total = Math.Min(total, thresholds[^1]);
        var next = before.Level;
        while (next < thresholds.Count + 1 && total >= thresholds[next - 1]) next++;
        return new(before, new(breakdown, next, checked((uint)total), next > before.Level));
    }
}
