using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Data.Characters;

using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

internal static class ExperienceProgressionRules
{
    internal static ExperienceAwardDecision Decide(CharacterProgressionState before,
        ExperienceBreakdown breakdown, CharacterExperienceScale scale)
    {
        var current = scale.ProgressFor(before.Experience);
        var uncapped = (ulong)current.Experience + breakdown.Total;
        var next = scale.ProgressFor(checked((long)uncapped));
        return new(before, new(breakdown, next.Level, next.Experience, next.Level > current.Level)
        {
            NotificationTotal = checked((uint)uncapped),
            Overflow = checked((uint)(uncapped - next.Experience)),
        });
    }
}
