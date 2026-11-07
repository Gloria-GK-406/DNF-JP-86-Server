using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>Reads <c>etc/equipment_grow.etc</c>, the experience table of growth weapons.</summary>
/// <remarks>
/// The shapes follow the A21 client's own parser (loaded at VA 0x01668F3B): <c>[level correction]</c>
/// is always six integers with no count, <c>[difficulty correction]</c> five, <c>[kind]</c> a count
/// followed by that many (kind, percent) pairs, and each <c>[exp base]</c> row three integers.
/// </remarks>
internal sealed class PvfEquipmentGrowthDefinitionSource(PvfArchiveProvider provider)
    : IEquipmentGrowthDefinitionSource, IContentWarmup
{
    internal const string Path = "etc/equipment_grow.etc";
    private readonly Lock gate = new();
    private IReadOnlyDictionary<int, EquipmentGrowthGroup>? groups;

    public string Name => "equipment-growth";

    public ValueTask<int> WarmAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Load().Count);
    }

    public ValueTask<EquipmentGrowthGroup?> GetGroupAsync(int group, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Load().GetValueOrDefault(group));
    }

    private IReadOnlyDictionary<int, EquipmentGrowthGroup> Load()
    {
        lock (gate)
            return groups ??= PvfDocument.TryOpen(provider.Archive, Path, PvfPreamble.Ignore) is { } document
                ? Read(provider.Archive, document.Sections) : new Dictionary<int, EquipmentGrowthGroup>();
    }

    internal static IReadOnlyDictionary<int, EquipmentGrowthGroup> Read(PvfArchive archive, IReadOnlyList<PvfSection> sections)
    {
        var result = new Dictionary<int, EquipmentGrowthGroup>();
        var i = 0;
        while (i < sections.Count)
        {
            if (sections[i].Name != "[group]") { i++; continue; }
            var group = Ints(sections[i], 1)[0];
            PvfField.Require(group > 0, Path, "[group]", "expected a positive group");
            i++;
            var maxLevel = 0;
            var acquisitions = new List<EquipmentGrowthAcquisition>();
            var stars = new List<EquipmentGrowthStar>();
            for (; i < sections.Count && sections[i].Name != "[/group]"; i++)
            {
                var section = sections[i];
                switch (section.Name)
                {
                    case "[max level]": maxLevel = Ints(section, 1)[0]; break;
                    case "[exp acquire]": acquisitions.Add(ReadAcquisition(archive, sections, ref i)); break;
                    case "[exp base]": stars.Add(ReadStar(archive, sections, ref i)); break;
                }
            }
            PvfField.Require(i < sections.Count, Path, "[group]", "block is not closed");
            i++;
            PvfField.Require(maxLevel > 0 && stars.Count > 0, Path, "[group]", "expected [max level] and [exp base] rows");
            PvfField.Require(stars.Select(x => x.Star).Distinct().Count() == stars.Count, Path, "[exp base]", "duplicate star");
            PvfField.Require(result.TryAdd(group, new(group, maxLevel, acquisitions.AsReadOnly(),
                stars.OrderBy(x => x.Star).ToArray().AsReadOnly())), Path, "[group]", "duplicate group");
        }
        return result;
    }

    private static EquipmentGrowthAcquisition ReadAcquisition(PvfArchive archive, IReadOnlyList<PvfSection> sections, ref int i)
    {
        var head = sections[i];
        PvfField.Require(head.Values.Count == 1 && head.Values[0].IsString, Path, head.Name, "expected one event name");
        var name = archive.ResolveString(head.Values[0].Value).Trim().Trim('`').ToLowerInvariant();
        var point = 0;
        var kinds = new Dictionary<int, int>();
        IReadOnlyList<int> level = [], difficulty = [];
        for (i++; i < sections.Count && sections[i].Name != "[/exp acquire]"; i++)
        {
            var section = sections[i];
            switch (section.Name)
            {
                case "[point]": point = Ints(section, 1)[0]; break;
                case "[kind]":
                    var values = Ints(section, section.Values.Count);
                    PvfField.Require(values.Length >= 1 && values.Length == 1 + 2 * values[0], Path, section.Name,
                        "expected a count and that many (kind, percent) pairs");
                    for (var k = 1; k < values.Length; k += 2)
                        PvfField.Require(kinds.TryAdd(values[k], values[k + 1]), Path, section.Name, "duplicate kind");
                    break;
                case "[level correction]": level = Ints(section, 6); break;
                case "[difficulty correction]": difficulty = Ints(section, 5); break;
            }
        }
        PvfField.Require(i < sections.Count, Path, "[exp acquire]", "block is not closed");
        PvfField.Require(point >= 0, Path, "[point]", "expected a nonnegative point");
        return new(name, point, kinds, level, difficulty);
    }

    private static EquipmentGrowthStar ReadStar(PvfArchive archive, IReadOnlyList<PvfSection> sections, ref int i)
    {
        var row = Ints(sections[i], 3);
        PvfField.Require(row[0] > 0 && row[1] >= 0 && row[2] is 0 or 1, Path, "[exp base]", "expected star, experience and a 0/1 flag");
        var penalties = new List<EquipmentGrowthPenalty>();
        for (i++; i < sections.Count && sections[i].Name != "[/exp base]"; i++)
        {
            var section = sections[i];
            if (section.Name != "[penalty]") continue;
            PvfField.Require(section.Values.Count % 3 == 0, Path, section.Name, "expected (kind, threshold, percent) triples");
            for (var k = 0; k < section.Values.Count; k += 3)
            {
                PvfField.Require(section.Values[k].IsString && section.Values[k + 1].Kind == PvfTokenKind.Int32
                    && section.Values[k + 2].Kind == PvfTokenKind.Int32, Path, section.Name, "expected (kind, threshold, percent)");
                var threshold = section.Values[k + 1].Value;
                var percent = section.Values[k + 2].Value;
                PvfField.Require(threshold >= 0 && percent is >= 0 and <= 100, Path, section.Name, "penalty out of range");
                penalties.Add(new(archive.ResolveString(section.Values[k].Value).Trim().Trim('`').ToLowerInvariant(), threshold, percent));
            }
        }
        PvfField.Require(i < sections.Count, Path, "[exp base]", "block is not closed");
        return new(row[0], row[1], row[2] == 1, penalties.AsReadOnly());
    }

    private static int[] Ints(PvfSection section, int count)
    {
        PvfField.Require(section.Values.Count == count && section.Values.All(x => x.Kind == PvfTokenKind.Int32),
            Path, section.Name, $"expected {count} integers");
        return [.. section.Values.Select(x => x.Value)];
    }
}
