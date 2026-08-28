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
/// <param name="ClearOverallRate">
/// The same thing for clearing a dungeon, and also 1 in this archive. No capture can tell it apart
/// from not multiplying at all; it is read and applied because it is declared in the same file, in
/// the same shape, one line from the monster rate that is applied -- and a declaration this server
/// reads past is one an operator can change with no effect and no explanation.
/// </param>
/// <param name="ClearRankRates">
/// What each rank band adds to a clear, in the archive's own ascending order: the last entry is the
/// best clear. Five entries, and they pair with the top five grades of the rank table in reverse --
/// the highest grade earns the last rate. A clear below all five earns no rank bonus at all.
/// <para>
/// Read, emphatically, rather than transcribed: the reference server's fallback for this table is
/// <c>0, 0.01, 0.02, 0.03, 0.05</c> and this archive says <c>0.05, 0.1, 0.12, 0.15, 0.2</c>. Its
/// constants would pay an order of magnitude less and nothing anywhere would say so.
/// </para>
/// </param>
public sealed record ExperienceParameters(
    IReadOnlyList<double> DifficultyRates,
    IReadOnlyList<double> PartyMemberRates,
    double MonsterOverallRate,
    double ClearOverallRate = 1.0,
    IReadOnlyList<double>? ClearRankRates = null)
{
    /// <summary>What each rank band adds to a clear, or nothing when the archive listed none.</summary>
    public IReadOnlyList<double> ClearRankRates { get; init; } = ClearRankRates ?? [];

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
