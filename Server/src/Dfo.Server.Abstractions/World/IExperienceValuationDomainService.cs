namespace Dfo.Server.Abstractions.World;

/// <summary>
/// What an occasion is worth to the character it happened to.
/// </summary>
/// <remarks>Both kill and clear valuation use the character level read inside the award transaction.</remarks>
public interface IExperienceValuationDomainService
{
    /// <summary>What one kill is worth before anything the character carries.</summary>
    uint WorthOfKill(in KillCircumstances kill);

    /// <summary>What this clear pays, and what its rank adds.</summary>
    ClearExperienceWorth WorthOfClear(in ClearCircumstances clear);
}
