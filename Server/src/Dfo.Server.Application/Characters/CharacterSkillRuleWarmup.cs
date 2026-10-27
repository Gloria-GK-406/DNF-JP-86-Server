using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Application.Characters;

/// <summary>
/// Warms the skill rules of every job the archive supports. The job set is a fact of the base-stat rules,
/// already prepared by then, and is handed to the skill rules rather than found by them.
/// </summary>
internal sealed class CharacterSkillRuleWarmup(ICharacterSkillRuleService skills, ICharacterStatRuleService stats) : IContentWarmup
{
    public string Name => "character-skill-rules";

    public ValueTask<int> WarmAsync(CancellationToken cancellationToken)
    {
        var rules = 0;
        foreach (var job in stats.SupportedCareers.Select(career => career.Job).Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            rules += skills.GetAll(job).Count;
        }
        return ValueTask.FromResult(rules);
    }
}
