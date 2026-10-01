using Dfo.Pvf.Schema;
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
    private static readonly PvfField<IReadOnlyList<double>> DifficultySection = PvfField.Numbers("[dungeon difficulty exp bonusrate]", optional: true);
    private static readonly PvfField<IReadOnlyList<double>> PartySection = PvfField.Numbers("[party user number exp bonusrate]", optional: true);
    private static readonly PvfField<double> MonsterSection = PvfField.Number("[monster exp bonusrate]", optional: true);
    private static readonly PvfField<double> ClearSection = PvfField.Number("[clear exp bonusrate]", optional: true);
    private static readonly PvfField<IReadOnlyList<double>> ClearRankSection = PvfField.Numbers("[clear rank exp bonusrate]", optional: true);

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
        var archive = provider.Archive;
        foreach (var section in PvfDocument.Open(archive, ArchivePath).Sections)
        {
            if (Is(section.Name, DifficultySection))
                difficulty.AddRange(DifficultySection.Decode(archive, section.Values, ArchivePath));
            else if (Is(section.Name, PartySection))
                party.AddRange(PartySection.Decode(archive, section.Values, ArchivePath));
            else if (Is(section.Name, MonsterSection))
                monster.Add(MonsterSection.Decode(archive, section.Values, ArchivePath));
            else if (Is(section.Name, ClearSection))
                clear.Add(ClearSection.Decode(archive, section.Values, ArchivePath));
            else if (Is(section.Name, ClearRankSection))
                clearRank.AddRange(ClearRankSection.Decode(archive, section.Values, ArchivePath));
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

    private static bool Is(string? section, PvfField name) =>
        section is not null && section.Equals(name.Name, StringComparison.OrdinalIgnoreCase);
}
