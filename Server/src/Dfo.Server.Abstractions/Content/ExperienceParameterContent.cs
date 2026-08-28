namespace Dfo.Server.Abstractions.Content;

/// <summary>
/// The server-wide rates a dungeon reward is scaled by, as the archive declares them.
/// </summary>
/// <remarks>
/// One file for the whole world: <c>Etc/ServerParameter.etc</c>. What a dungeon says about itself
/// -- its own experience weight, its basis level -- is on the dungeon; these are the rates every
/// dungeon shares.
/// </remarks>
/// <param name="DifficultyRates">
/// What each difficulty multiplies a reward by, indexed from the lowest. The archive's own values
/// start at 1.3 rather than at 1, so the easiest difficulty is already a bonus.
/// </param>
/// <param name="PartyMemberRates">
/// What a party of each size multiplies the shared reward by, indexed from one member. It is a
/// multiplier on what the party earns together, not on what a member takes home: the division by
/// party size happens after it.
/// </param>
/// <param name="MonsterOverallRate">
/// A single rate over every monster reward, which the archive currently sets to 1. It exists so an
/// operator can turn the whole world's monster experience up or down without touching a dungeon.
/// </param>
public sealed record ExperienceParameters(
    IReadOnlyList<double> DifficultyRates,
    IReadOnlyList<double> PartyMemberRates,
    double MonsterOverallRate)
{
    /// <summary>What a difficulty this table does not list is worth.</summary>
    /// <remarks>
    /// One, not the first entry. An unlisted difficulty is a difficulty this server does not know,
    /// and paying it the easiest difficulty's 1.3 would be inventing a bonus for a case nobody has
    /// described.
    /// </remarks>
    public const double UnlistedRate = 1.0;

    /// <summary>What this difficulty multiplies a reward by.</summary>
    public double DifficultyRate(int difficulty) =>
        difficulty >= 0 && difficulty < this.DifficultyRates.Count
            ? this.DifficultyRates[difficulty]
            : UnlistedRate;

    /// <summary>What a party of this size multiplies the shared reward by.</summary>
    public double PartyMemberRate(int partyMemberCount) =>
        partyMemberCount >= 1 && partyMemberCount <= this.PartyMemberRates.Count
            ? this.PartyMemberRates[partyMemberCount - 1]
            : UnlistedRate;
}

/// <summary>The archive's world-wide reward rates.</summary>
public interface IExperienceParameterSource
{
    ExperienceParameters Load();
}
