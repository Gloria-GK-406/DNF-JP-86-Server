using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfDungeonExperienceProgressionPolicySource(PvfArchiveProvider provider)
    : IDungeonExperienceProgressionPolicySource
{
    public DungeonExperienceProgressionPolicy Load()
    {
        var thresholds = ReadAllInts("character/ExpTable.tbl").Select(value => checked((uint)Math.Max(0, value))).ToArray();
        if (thresholds.Length < 85) throw new InvalidDataException("PVF experience thresholds are incomplete.");
        var maximumHonor = ReadMaximumHonorExperience();
        var capsule = ReadTaggedInts("etc/expandexpgage.etc");
        var maximum = checked((uint)Math.Max(1, First(capsule, "[max gage exp]", 11691495)));
        var rate = Math.Max(0, First(capsule, "[exp gain rate]", 100));
        return new DungeonExperienceProgressionPolicy(thresholds, maximumHonor, maximum, rate);
    }

    private IReadOnlyList<int> ReadAllInts(string path)
    {
        var values = new List<int>(); var reader = provider.Archive.OpenScript(path);
        while (reader.Read(out var token)) if (token.Kind == PvfTokenKind.Int32) values.Add(token.Value);
        return values;
    }

    private Dictionary<string, List<int>> ReadTaggedInts(string path)
    {
        var result = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        string? section = null; var reader = provider.Archive.OpenScript(path);
        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier)
            { section = provider.Archive.ResolveString(token.Value); result.TryAdd(section, []); }
            else if (section is not null && token.Kind == PvfTokenKind.Int32) result[section].Add(token.Value);
        }
        return result;
    }
    private ulong ReadMaximumHonorExperience()
    {
        var reader = provider.Archive.OpenScript("etc/honorlevel.etc");
        var grades = new List<List<int>>(); List<int>? grade = null; var maxOnMaximum = 0;
        string? section = null;
        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier)
            {
                section = provider.Archive.ResolveString(token.Value);
                if (section.Equals("[grade]", StringComparison.OrdinalIgnoreCase))
                { grade = []; grades.Add(grade); }
                else if (section.Equals("[/grade]", StringComparison.OrdinalIgnoreCase)) grade = null;
            }
            else if (token.Kind == PvfTokenKind.Int32)
            {
                if (grade is not null) grade.Add(token.Value);
                else if (section?.Equals("[maxexp on maxlevel]", StringComparison.OrdinalIgnoreCase) == true)
                    maxOnMaximum = Math.Max(0, token.Value);
            }
        }
        ulong total = checked((ulong)maxOnMaximum);
        foreach (var values in grades)
            for (var index = 4; index < values.Count; index += 2)
                total = Math.Min((ulong)long.MaxValue, total + checked((ulong)Math.Max(0, values[index])));
        if (total == 0) throw new InvalidDataException("PVF honor-level cap is unavailable.");
        return total;
    }
    private static int First(Dictionary<string,List<int>> source, string key, int fallback) =>
        source.TryGetValue(key, out var values) && values.Count > 0 ? values[0] : fallback;
}
