using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

/// <summary>What an occasion is worth to the character it happened to.</summary>
internal sealed class ExperienceValuationDomainService(
    IExperienceRuleService experienceRules) : IExperienceValuationDomainService
{
    public uint WorthOfKill(in KillCircumstances kill) => experienceRules.WorthOfKill(kill);

    public ClearExperienceWorth WorthOfClear(in ClearCircumstances clear) =>
        experienceRules.WorthOfClear(clear);
}
