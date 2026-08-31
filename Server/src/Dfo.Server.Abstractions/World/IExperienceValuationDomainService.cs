namespace Dfo.Server.Abstractions.World;

/// <summary>
/// What an occasion is worth to the character it happened to.
/// </summary>
/// <remarks>
/// The seam the two award flows reach the experience rules through. A kill is priced against the
/// character's level, which is why its valuation is asked for inside the award rather than before
/// it; a clear is not, which is why its two figures are worked out first. Both are asked here so
/// that neither flow holds the rules it prices with.
/// </remarks>
public interface IExperienceValuationDomainService
{
    /// <summary>What one kill is worth before anything the character carries.</summary>
    uint WorthOfKill(in KillCircumstances kill);

    /// <summary>What this clear pays, and what its rank adds.</summary>
    ClearExperienceWorth WorthOfClear(in ClearCircumstances clear);
}
