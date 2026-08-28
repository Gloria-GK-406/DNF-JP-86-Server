using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>
/// Reads the world-wide reward rates out of <c>Etc/ServerParameter.etc</c>.
/// </summary>
/// <remarks>
/// Parsed once and held: one file, read by every kill, rewritten by nothing while the process runs.
///
/// The file is long and most of it is about other subjects -- drops, abuse points, assault costs.
/// Only the five experience sections are taken, and a section this does not name is skipped rather
/// than collected, so a neighbouring array cannot arrive under the wrong heading.
/// </remarks>
internal sealed class PvfExperienceParameterSource(PvfArchiveProvider provider)
    : IExperienceParameterSource
{
    private const string ArchivePath = "Etc/ServerParameter.etc";
    private const string DifficultySection = "[dungeon difficulty exp bonusrate]";
    private const string PartySection = "[party user number exp bonusrate]";
    private const string MonsterSection = "[monster exp bonusrate]";
    private const string ClearSection = "[clear exp bonusrate]";
    private const string ClearRankSection = "[clear rank exp bonusrate]";

    private readonly Lock _gate = new();
    private ExperienceParameters? _parameters;

    public ExperienceParameters Load()
    {
        lock (_gate)
        {
            return _parameters ??= this.Read();
        }
    }

    private ExperienceParameters Read()
    {
        var difficulty = new List<double>();
        var party = new List<double>();
        var monster = new List<double>();
        var clear = new List<double>();
        var clearRank = new List<double>();
        string? section = null;

        var reader = provider.Archive.OpenScript(ArchivePath);
        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier)
            {
                section = provider.Archive.ResolveString(token.Value);
                continue;
            }

            // Both numeric forms are collected, because the sections disagree about which they
            // use: the five difficulty rates are all singles -- 2, 3 and 4 included, stored as
            // 2.0f and so on -- while the four party rates are all plain integers. A reader that
            // took only one kind would come away with one of the two tables empty, and an empty
            // table here is a parse failure wearing the costume of a rate of 1.
            var value = token.Kind switch
            {
                PvfTokenKind.Int32 => token.Value,
                PvfTokenKind.Single => (double)BitConverter.Int32BitsToSingle(token.Value),
                _ => (double?)null,
            };
            if (value is not { } rate)
                continue;

            if (Is(section, DifficultySection))
                difficulty.Add(rate);
            else if (Is(section, PartySection))
                party.Add(rate);
            else if (Is(section, MonsterSection))
                monster.Add(rate);
            else if (Is(section, ClearSection))
                clear.Add(rate);
            else if (Is(section, ClearRankSection))
                clearRank.Add(rate);
        }

        // A missing table is a broken archive, not a world where every kill is worth nothing.
        // Failing here names the file; defaulting quietly would make every reward wrong by a
        // factor nobody could trace back to a parse.
        if (difficulty.Count == 0 || party.Count == 0)
        {
            throw new InvalidDataException(
                $"'{ArchivePath}' declares no experience rate tables; dungeon rewards cannot be scaled.");
        }

        // The clear tables are not required the way the two above are. A dungeon whose clear pays
        // nothing is a dungeon that still works; a world with no difficulty rates is one where
        // every reward is silently wrong, which is why only those two refuse.
        return new ExperienceParameters(
            difficulty,
            party,
            monster.Count > 0 ? monster[0] : ExperienceParameters.UnlistedRate,
            clear.Count > 0 ? clear[0] : ExperienceParameters.UnlistedRate,
            clearRank);
    }

    private static bool Is(string? section, string name) =>
        section is not null && section.Equals(name, StringComparison.OrdinalIgnoreCase);
}
