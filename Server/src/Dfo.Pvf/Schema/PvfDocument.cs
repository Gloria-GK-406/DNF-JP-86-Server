using Dfo.Pvf.Tokens;

namespace Dfo.Pvf.Schema;

public sealed record PvfSection(string Name, IReadOnlyList<PvfToken> Values);

/// <summary>What a reader does with values that appear before the first section heading.</summary>
public enum PvfPreamble
{
    /// <summary>The file is malformed.</summary>
    Reject,
    /// <summary>Leave them out; the file's sections are read as if they were absent.</summary>
    Ignore,
}

public sealed class PvfDocument
{
    private readonly PvfArchive archive;
    private readonly string source;
    public IReadOnlyList<PvfSection> Sections { get; }

    private PvfDocument(PvfArchive archive, string source, IReadOnlyList<PvfSection> sections)
    {
        this.archive = archive;
        this.source = source;
        Sections = sections;
    }

    public static PvfDocument Open(PvfArchive archive, string path)
    {
        var index = archive.FindFile(path);
        PvfField.Require(index >= 0, path, "script", "script is missing");
        PvfField.Require(archive.GetEntry(index).IsScript, path, "script", "expected a script file");
        return Read(archive, index, path, PvfPreamble.Reject);
    }

    /// <summary>Opens a script that may be absent; null when the archive holds no script at this path.</summary>
    public static PvfDocument? TryOpen(PvfArchive archive, string path, PvfPreamble preamble = PvfPreamble.Reject)
    {
        if (!Enum.IsDefined(preamble)) throw new ArgumentOutOfRangeException(nameof(preamble));
        var index = archive.FindFile(path);
        return index < 0 || !archive.GetEntry(index).IsScript ? null : Read(archive, index, path, preamble);
    }

    private static PvfDocument Read(PvfArchive archive, int index, string path, PvfPreamble preamble)
    {
        PvfField.Require(archive.GetFileBytes(index).Length % PvfToken.Size == 0, path, "script", "truncated token record");
        var reader = archive.OpenScript(index);
        var sections = new List<PvfSection>();
        string? name = null;
        var values = new List<PvfToken>();
        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier && archive.ResolveString(token.Value).StartsWith('['))
            {
                if (name is not null) sections.Add(new(name, values.AsReadOnly()));
                name = archive.ResolveString(token.Value);
                values = [];
            }
            else
            {
                if (name is null)
                {
                    PvfField.Require(preamble == PvfPreamble.Ignore, path, "script", "value appears before a section");
                    continue;
                }
                values.Add(token);
            }
        }
        if (name is not null) sections.Add(new(name, values.AsReadOnly()));
        return new(archive, path, sections.AsReadOnly());
    }

    /// <summary>
    /// Returns the root sections, preserving their order and repeated occurrences.
    /// A heading opens a block only when its matching closing heading exists in this document;
    /// unpaired headings remain ordinary fields. Unknown blocks follow the same rule.
    /// </summary>
    public PvfDocument AtRoot()
    {
        var closingNames = Sections.Where(section => section.Name.StartsWith("[/", StringComparison.Ordinal))
            .Select(section => section.Name).ToHashSet(StringComparer.Ordinal);
        if (closingNames.Count == 0) return this;

        var roots = new List<PvfSection>();
        var blocks = new Stack<string>();
        foreach (var section in Sections)
        {
            if (blocks.TryPeek(out var expected) && section.Name == expected)
            {
                blocks.Pop();
                continue;
            }
            if (blocks.Count == 0) roots.Add(section);
            var closing = "[/" + section.Name[1..];
            if (closingNames.Contains(closing)) blocks.Push(closing);
        }
        return new(archive, source, roots.AsReadOnly());
    }

    /// <summary>Validates every occurrence without imposing global uniqueness on scoped/repeated fields.</summary>
    public void ValidateFields(params PvfField[] fields)
    {
        foreach (var field in fields) RequireSection(field);
        var known = fields.ToDictionary(field => field.Name, StringComparer.Ordinal);
        foreach (var section in Sections)
            if (known.TryGetValue(section.Name, out var field)) field.Validate(archive, section.Values, source);
    }

    /// <summary>Reads one section. Missing optional fields return the type default; use TryRead to distinguish absence.</summary>
    public T Read<T>(PvfField<T> field)
    {
        if (TryRead(field, out var value)) return value;
        PvfField.Require(field.Optional, source, field.Name, "required field is missing");
        return default!;
    }

    public bool TryRead<T>(PvfField<T> field, out T value)
    {
        RequireSection(field);
        var found = Sections.Where(section => field.Matches(section.Name)).ToArray();
        if (found.Length == 0) { value = default!; return false; }
        PvfField.Require(found.Length == 1, source, field.Name, "duplicate field");
        value = field.Decode(archive, found[0].Values, source);
        return true;
    }

    public IReadOnlyList<T> ReadAll<T>(PvfField<T> field)
    {
        RequireSection(field);
        return Array.AsReadOnly(Sections.Where(section => field.Matches(section.Name))
            .Select(section => field.Decode(archive, section.Values, source)).ToArray());
    }

    private static void RequireSection(PvfField field)
    {
        if (field.TokenKind != PvfTokenKind.Identifier)
            throw new ArgumentException($"Field {field.Name} is an embedded {field.TokenKind} marker; use its scoped parser.", nameof(field));
    }
}
