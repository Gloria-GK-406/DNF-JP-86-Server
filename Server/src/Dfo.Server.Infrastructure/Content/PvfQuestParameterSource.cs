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
/// The difficulty section alternates a quoted letter with an integer, and the two reward tables
/// alternate a value with <c>-1</c>. The <c>-1</c>s are dropped rather than kept: keeping them
/// would put every real value at twice its index and price every quest at half its level.
/// </remarks>
internal sealed class PvfQuestParameterSource(PvfArchiveProvider provider) : IQuestParameterSource
{
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
        string? section = null;
        char? pendingLetter = null;

        var reader = provider.Archive.OpenScript(ArchivePath);
        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier)
            {
                section = provider.Archive.ResolveString(token.Value);
                pendingLetter = null;
                continue;
            }

            if (section is null)
                continue;

            // The difficulty letters are the only quoted strings this file's numeric sections
            // carry. Held until the integer that belongs to them arrives.
            if (token.Kind == PvfTokenKind.QuotedString)
            {
                if (IsSection(section, "[difficulty]"))
                {
                    var text = provider.Archive.ResolveString(token.Value).Trim().Trim('`');
                    pendingLetter = text.Length == 1 ? char.ToUpperInvariant(text[0]) : null;
                }

                continue;
            }

            if (token.Kind != PvfTokenKind.Int32)
                continue;

            if (IsSection(section, "[difficulty]"))
            {
                if (pendingLetter is { } letter)
                    difficulty[letter] = token.Value;
                pendingLetter = null;
            }
            else if (IsSection(section, "[exp reward table]"))
            {
                if (token.Value >= 0)
                    experience.Add(token.Value);
            }
            else if (IsSection(section, "[gold reward table]"))
            {
                if (token.Value >= 0)
                    gold.Add(token.Value);
            }
            else if (IsPenalty(section))
            {
                penalties.TryAdd(section, token.Value);
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
            Penalty(penalties, "[green level penalty]", 80),
            Penalty(penalties, "[grey level penalty]", 30),
            Penalty(penalties, "[epic green level penalty]", 120),
            Penalty(penalties, "[epic grey level penalty]", 140),
            gold);
    }

    private static bool IsSection(string section, string name) =>
        section.Equals(name, StringComparison.OrdinalIgnoreCase);

    private static bool IsPenalty(string section) =>
        IsSection(section, "[green level penalty]")
        || IsSection(section, "[grey level penalty]")
        || IsSection(section, "[epic green level penalty]")
        || IsSection(section, "[epic grey level penalty]");

    /// <remarks>
    /// The fallbacks are the reference server's own defaults for a file that omits a band, kept so
    /// a partial file prices quests the way that server would rather than at zero.
    /// </remarks>
    private static int Penalty(IReadOnlyDictionary<string, int> source, string key, int fallback) =>
        source.TryGetValue(key, out var value) ? value : fallback;
}
