using Dfo.Pvf.Tokens;

namespace Dfo.Pvf.Schema;

/// <summary>What a reader does when a field it expects once is declared more than once.</summary>
public enum PvfRepeats
{
    /// <summary>The file is malformed.</summary>
    Reject,
    /// <summary>The repeats contradict each other, so the field counts as undeclared.</summary>
    AsUndeclared,
}

/// <summary>Single-field reads over a flat section list, with the repeat policy stated by the caller.</summary>
public static class PvfSections
{
    public static PvfSection? Single(IReadOnlyList<PvfSection> sections, PvfField field, PvfRepeats repeats, string source)
    {
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(field);
        if (!Enum.IsDefined(repeats)) throw new ArgumentOutOfRangeException(nameof(repeats));
        var matches = sections.Where(section => field.Matches(section.Name)).Take(2).ToArray();
        if (matches.Length > 1)
        {
            PvfField.Require(repeats != PvfRepeats.Reject, source, field.Name, "duplicate field");
            return null;
        }
        return matches.FirstOrDefault();
    }

    /// <summary>A field declared once with exactly one Int32 value; anything else reads as undeclared.</summary>
    public static int? SingleInt32(PvfArchive archive, IReadOnlyList<PvfSection> sections, PvfField<int> field,
        PvfRepeats repeats, string source) =>
        Single(sections, field, repeats, source) is { Values.Count: 1 } section && section.Values[0].Kind == PvfTokenKind.Int32
            ? field.Decode(archive, section.Values, source) : null;
}
