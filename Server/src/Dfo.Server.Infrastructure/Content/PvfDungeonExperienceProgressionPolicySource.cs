using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfDungeonExperienceProgressionPolicySource(PvfArchiveProvider provider)
    : IDungeonExperienceProgressionPolicySource
{
    private static class Fields
    {
        internal static readonly PvfField<int> MaxGageExpField = PvfField.Int32("[max gage exp]", optional: true);
        internal static readonly PvfField<int> ExpGainRateField = PvfField.Int32("[exp gain rate]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> GradeField = PvfField.Custom<IReadOnlyList<int>>("[grade]",
            (_, values, path) =>
            {
                if (values.Count < 6 || (values.Count - 6) % 2 != 0 ||
                    values[0].Kind != PvfTokenKind.Int32 || !values[1].IsString || !values[2].IsString ||
                    values[3].Kind != PvfTokenKind.Int32 || !values[4].IsString || values[5].Kind != PvfTokenKind.Int32 ||
                    values.Skip(6).Any(token => token.Kind != PvfTokenKind.Int32))
                    throw new InvalidDataException($"PVF '{path}', field '[grade]': expected honor grade header and level/experience Int32 pairs.");
                return Array.AsReadOnly(values.Where(token => token.Kind == PvfTokenKind.Int32).Select(token => token.Value).ToArray());
            }, optional: true);
        internal static readonly PvfField<bool> CloseGradeField = PvfField.Marker("[/grade]", optional: true);
        internal static readonly PvfField<int> MaxexpOnMaxlevelField = PvfField.Int32("[maxexp on maxlevel]", optional: true);
    }

    public DungeonExperienceProgressionPolicy Load()
    {
        var thresholds = ReadAllInts("character/ExpTable.tbl").Select(value => checked((uint)Math.Max(0, value))).ToArray();
        if (thresholds.Length < 85) throw new InvalidDataException("PVF experience thresholds are incomplete.");
        var maximumHonor = ReadMaximumHonorExperience();
        var capsule = PvfDocument.Open(provider.Archive, "etc/expandexpgage.etc");
        var maximum = checked((uint)Math.Max(1, First(capsule, Fields.MaxGageExpField, 11691495)));
        var rate = Math.Max(0, First(capsule, Fields.ExpGainRateField, 100));
        return new DungeonExperienceProgressionPolicy(thresholds, maximumHonor, maximum, rate);
    }

    private IReadOnlyList<int> ReadAllInts(string path)
    {
        var values = new List<int>(); var reader = provider.Archive.OpenScript(path);
        while (reader.Read(out var token)) if (token.Kind == PvfTokenKind.Int32) values.Add(token.Value);
        return values;
    }

    private ulong ReadMaximumHonorExperience()
    {
        const string path = "etc/honorlevel.etc";
        var archive = provider.Archive;
        var grades = new List<List<int>>(); List<int>? grade = null; var maxOnMaximum = 0;
        foreach (var section in PvfDocument.Open(archive, path).Sections)
        {
            if (section.Name.Equals(Fields.GradeField.Name, StringComparison.OrdinalIgnoreCase))
            {
                grade = Fields.GradeField.Decode(archive, section.Values, path).ToList();
                grades.Add(grade);
            }
            else if (section.Name.Equals(Fields.CloseGradeField.Name, StringComparison.OrdinalIgnoreCase))
            {
                Fields.CloseGradeField.Decode(archive, section.Values, path);
                grade = null;
            }
            else if (section.Name.Equals(Fields.MaxexpOnMaxlevelField.Name, StringComparison.OrdinalIgnoreCase))
            {
                var value = Fields.MaxexpOnMaxlevelField.Decode(archive, section.Values, path);
                if (grade is null) maxOnMaximum = Math.Max(0, value);
                else grade.Add(value);
            }
            else if (grade is not null)
            {
                // Unknown nested headings retain the old integer-only grade projection.
                grade.AddRange(section.Values.Where(token => token.Kind == PvfTokenKind.Int32).Select(token => token.Value));
            }
        }
        ulong total = checked((ulong)maxOnMaximum);
        foreach (var values in grades)
            for (var index = 4; index < values.Count; index += 2)
                total = Math.Min((ulong)long.MaxValue, total + checked((ulong)Math.Max(0, values[index])));
        if (total == 0) throw new InvalidDataException("PVF honor-level cap is unavailable.");
        return total;
    }
    private static int First(PvfDocument source, PvfField<int> field, int fallback) =>
        source.TryRead(field, out var value) ? value : fallback;
}
