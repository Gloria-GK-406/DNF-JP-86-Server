using Dfo.Pvf.Schema;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>
/// Reads <c>n_Quest/questParameter.etc</c>: the table every quest's reward is priced from.
/// </summary>
/// <remarks>
/// Parsed once and held, because it is one file that every quest completion reads and nothing
/// rewrites while the process runs.
///
/// The difficulty section alternates a quoted letter with an integer. Reward tables use a plain
/// nonnegative sequence or value/<c>-1</c> pairs (the archive's experience table). The separators are dropped: keeping them
/// would put every real value at twice its index and price every quest at half its level.
/// </remarks>
internal sealed class PvfQuestParameterSource(PvfArchiveProvider provider) : IQuestParameterSource
{
    private static class Fields
    {
        internal static readonly PvfField<IReadOnlyDictionary<char, int>> DifficultyField = PvfField.Custom<IReadOnlyDictionary<char, int>>("[difficulty]",
            (archive, values, path) =>
            {
                if (values.Count % 2 != 0) throw new InvalidDataException($"PVF '{path}', field '[difficulty]': incomplete letter/value pair.");
                var result = new Dictionary<char, int>();
                for (var i = 0; i < values.Count; i += 2)
                {
                    if (values[i].Kind != PvfTokenKind.QuotedString || values[i + 1].Kind != PvfTokenKind.Int32)
                        throw new InvalidDataException($"PVF '{path}', field '[difficulty]': expected quoted letter and Int32.");
                    var text = archive.ResolveString(values[i].Value).Trim().Trim('`');
                    if (text.Length != 1) throw new InvalidDataException($"PVF '{path}', field '[difficulty]': expected one letter.");
                    result[char.ToUpperInvariant(text[0])] = values[i + 1].Value;
                }
                return result;
            }, optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> ExpRewardTableField = PvfField.Custom<IReadOnlyList<int>>("[exp reward table]",
            DecodeExperienceTable, optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> GoldRewardTableField = PvfField.Custom<IReadOnlyList<int>>("[gold reward table]",
            DecodeGoldTable, optional: true);
        internal static readonly PvfField<int> GreenLevelPenaltyField = PvfField.Int32("[green level penalty]", optional: true);
        internal static readonly PvfField<int> GreyLevelPenaltyField = PvfField.Int32("[grey level penalty]", optional: true);
        internal static readonly PvfField<int> EpicGreenLevelPenaltyField = PvfField.Int32("[epic green level penalty]", optional: true);
        internal static readonly PvfField<int> EpicGreyLevelPenaltyField = PvfField.Int32("[epic grey level penalty]", optional: true);
    }

    private const string ArchivePath = "n_Quest/questParameter.etc";

    private readonly Lock _gate = new();
    private QuestParameters? _parameters;

    public QuestParameters Load()
    {
        lock (_gate)
        {
            return _parameters ??= this.Read();
        }
    }

    private QuestParameters Read()
    {
        var experience = new List<int>();
        var gold = new List<int>();
        var difficulty = new Dictionary<char, int>();
        var penalties = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var archive = provider.Archive;
        var document = PvfDocument.Open(archive, ArchivePath);
        foreach (var section in document.Sections)
        {
            if (IsSection(section.Name, Fields.DifficultyField))
                foreach (var pair in Fields.DifficultyField.Decode(archive, section.Values, ArchivePath)) difficulty[pair.Key] = pair.Value;
            else if (IsSection(section.Name, Fields.ExpRewardTableField))
                experience.AddRange(Fields.ExpRewardTableField.Decode(archive, section.Values, ArchivePath));
            else if (IsSection(section.Name, Fields.GoldRewardTableField))
                gold.AddRange(Fields.GoldRewardTableField.Decode(archive, section.Values, ArchivePath));
            else if (new[] { Fields.GreenLevelPenaltyField, Fields.GreyLevelPenaltyField,
                Fields.EpicGreenLevelPenaltyField, Fields.EpicGreyLevelPenaltyField }
                .FirstOrDefault(field => IsSection(section.Name, field)) is { } penalty)
            {
                var value = penalty.Decode(archive, section.Values, ArchivePath);
                if (!penalties.TryAdd(penalty.Name, value))
                    throw new InvalidDataException($"PVF '{ArchivePath}', field '{penalty.Name}': duplicate penalty.");
            }
        }

        // A missing table is a broken archive rather than a quest worth nothing: pricing every
        // completion at zero would look like a rule rather than like a failure to read the file.
        if (experience.Count == 0)
        {
            throw new InvalidDataException(
                $"'{ArchivePath}' declares no experience reward table; quest rewards cannot be priced.");
        }

        return new QuestParameters(
            experience,
            difficulty,
            Penalty(penalties, Fields.GreenLevelPenaltyField, 80),
            Penalty(penalties, Fields.GreyLevelPenaltyField, 30),
            Penalty(penalties, Fields.EpicGreenLevelPenaltyField, 120),
            Penalty(penalties, Fields.EpicGreyLevelPenaltyField, 140),
            gold);
    }

    private static IReadOnlyList<int> DecodeExperienceTable(PvfArchive archive, IReadOnlyList<PvfToken> values, string path) =>
        DecodeRewardTable(archive, values, path, Fields.ExpRewardTableField);

    private static IReadOnlyList<int> DecodeGoldTable(PvfArchive archive, IReadOnlyList<PvfToken> values, string path) =>
        DecodeRewardTable(archive, values, path, Fields.GoldRewardTableField);

    private static IReadOnlyList<int> DecodeRewardTable(PvfArchive archive, IReadOnlyList<PvfToken> values, string path, PvfField field)
    {
        var numbers = PvfField.Int32List(field.Name).Decode(archive, values, path);
        if (numbers.All(value => value >= 0)) return numbers;
        if (numbers.Count % 2 != 0)
            throw new InvalidDataException($"PVF '{path}', field '{field}': incomplete value/-1 pair.");
        var result = new List<int>(numbers.Count / 2);
        for (var i = 0; i < numbers.Count; i += 2)
        {
            if (numbers[i] < 0 || numbers[i + 1] != -1)
                throw new InvalidDataException($"PVF '{path}', field '{field}': expected nonnegative value followed by -1.");
            result.Add(numbers[i]);
        }
        return result.AsReadOnly();
    }

    private static bool IsSection(string section, PvfField name) =>
        section.Equals(name.Name, StringComparison.OrdinalIgnoreCase);

    /// <remarks>
    /// The fallbacks are the reference server's own defaults for a file that omits a band, kept so
    /// a partial file prices quests the way that server would rather than at zero.
    /// </remarks>
    private static int Penalty(IReadOnlyDictionary<string, int> source, PvfField<int> key, int fallback) =>
        source.TryGetValue(key.Name, out var value) ? value : fallback;
}
