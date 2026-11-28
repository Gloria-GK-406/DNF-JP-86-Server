using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Characters;

internal static class CharacterSkillGrantRules
{
    internal static int MinimumLevel(ICharacterSkillRule? rule, CharacterCareer career, int level, byte grant) =>
        Math.Max(rule?.GetFreeLevel(career, level) ?? 0, grant);

    internal static short? Slot(ICharacterSkillRule? rule, IReadOnlySet<int> occupied, byte sourceGrant)
    {
        var preferred = rule?.FindAvailableSlot(occupied);
        if (preferred is not null || sourceGrant == 0) return preferred;
        // Profession grants can use another free slot when the usual group is unavailable.
        for (short candidate = 0; candidate < SkillHotkeySlots.SecondRowEnd; candidate++)
            if (!occupied.Contains(candidate)) return candidate;
        return null;
    }
}
