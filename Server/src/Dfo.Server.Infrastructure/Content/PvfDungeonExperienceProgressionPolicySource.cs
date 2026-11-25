using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfDungeonExperienceProgressionPolicySource(PvfArchiveProvider provider)
    : IDungeonExperienceProgressionPolicySource
{
    private static class Fields
    {
        internal static readonly PvfField<int> MaxGageExpField = PvfField.Int32("[max gage exp]", optional: true);
        internal static readonly PvfField<int> ExpGainRateField = PvfField.Int32("[exp gain rate]", optional: true);
        internal static readonly PvfField<IReadOnlyList<HonorLevelThreshold>> GradeField =
            PvfField.Custom<IReadOnlyList<HonorLevelThreshold>>("[grade]",
            (_, values, path) =>
            {
                if (values.Count < 8 || (values.Count - 6) % 2 != 0 ||
                    values[0].Kind != PvfTokenKind.Int32 || values[0].Value <= 0 ||
                    !values[1].IsString || !values[2].IsString ||
                    values[3].Kind != PvfTokenKind.Int32 || !values[4].IsString || values[5].Kind != PvfTokenKind.Int32 ||
                    values.Skip(6).Any(token => token.Kind != PvfTokenKind.Int32 || token.Value < 0))
                    throw new InvalidDataException($"PVF '{path}', field '[grade]': expected honor grade header and non-negative level/experience Int32 pairs.");
                var grade = (uint)values[0].Value;
                var levels = new List<HonorLevelThreshold>();
                for (var index = 6; index < values.Count; index += 2)
                    levels.Add(new((uint)values[index].Value, grade, (uint)values[index + 1].Value));
                return levels;
            }, optional: true);
        internal static readonly PvfField<bool> CloseGradeField = PvfField.Marker("[/grade]", optional: true);
        internal static readonly PvfField<int> MaxexpOnMaxlevelField = PvfField.Int32("[maxexp on maxlevel]", optional: true);
    }

    public DungeonExperienceProgressionPolicy Load()
    {
        var thresholds = ReadAllInts("character/ExpTable.tbl").Select(value => checked((uint)Math.Max(0, value))).ToArray();
        if (thresholds.Length < 85) throw new InvalidDataException("PVF experience thresholds are incomplete.");
        var honor = ReadHonorScale();
        var capsule = PvfDocument.Open(provider.Archive, "etc/expandexpgage.etc");
        var maximum = checked((uint)Math.Max(1, First(capsule, Fields.MaxGageExpField, 11691495)));
        var rate = Math.Max(0, First(capsule, Fields.ExpGainRateField, 100));
        return new DungeonExperienceProgressionPolicy(thresholds, honor, maximum, rate);
    }

    private IReadOnlyList<int> ReadAllInts(string path)
    {
        var values = new List<int>(); var reader = provider.Archive.OpenScript(path);
        while (reader.Read(out var token)) if (token.Kind == PvfTokenKind.Int32) values.Add(token.Value);
        return values;
    }

    private HonorExperienceScale ReadHonorScale()
    {
        const string path = "etc/honorlevel.etc";
        var archive = provider.Archive;
        var document = PvfDocument.Open(archive, path);
        var levels = new List<HonorLevelThreshold>();
        foreach (var section in document.Sections)
        {
            if (section.Name.Equals(Fields.GradeField.Name, StringComparison.OrdinalIgnoreCase))
                levels.AddRange(Fields.GradeField.Decode(archive, section.Values, path));
            else if (section.Name.Equals(Fields.CloseGradeField.Name, StringComparison.OrdinalIgnoreCase))
                Fields.CloseGradeField.Decode(archive, section.Values, path);
        }
        var maximumLevelExperience = First(document, Fields.MaxexpOnMaxlevelField, 0);
        if (maximumLevelExperience < 0)
            throw new InvalidDataException($"PVF '{path}', field '[maxexp on maxlevel]': expected non-negative experience.");
        var scale = new HonorExperienceScale(levels, (uint)maximumLevelExperience);
        if (scale.MaximumExperience == 0)
            throw new InvalidDataException("PVF honor-level cap is unavailable.");
        return scale;
    }
    private static int First(PvfDocument source, PvfField<int> field, int fallback) =>
        source.TryRead(field, out var value) ? value : fallback;
}
