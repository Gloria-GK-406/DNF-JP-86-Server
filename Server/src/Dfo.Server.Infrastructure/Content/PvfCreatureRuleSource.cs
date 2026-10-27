using Dfo.Pvf.Schema;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Creatures;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>Reads A21 creature rules without imposing a fixed creature level cap.</summary>
internal sealed class PvfCreatureRuleSource(
    PvfArchiveProvider provider,
    ICreatureDefinitionSource definitions) : ICreatureRuleSource, Dfo.Server.Abstractions.Content.IContentWarmup
{
    private static class Fields
    {
        internal static readonly PvfField<string> EquipmentTypeField = PvfField.Custom<string>("[equipment type]", (archive, values, path) =>
        {
            if (values.Count == 0 || !values[0].IsString || values.Skip(1).Any(token => token.Kind != PvfTokenKind.Int32))
                throw new InvalidDataException($"PVF '{path}', field '[equipment type]': expected type string and optional Int32 operands.");
            return archive.ResolveString(values[0].Value);
        }, optional: true);
        internal static readonly PvfField<int> SubTypeField = PvfField.Int32("[sub type]", optional: true);
        internal static readonly PvfField<int> CreatureSpeciesField = PvfField.Int32("[creature species]", optional: true);
        internal static readonly PvfField<int> OutputIndexField = PvfField.Int32("[output index]", optional: true);
        internal static readonly PvfField<int> CreatureMinimumLevelField = PvfField.Int32("[creature minimum level]", optional: true);
        internal static readonly PvfField<double> CreatureExperienceAmountRateField = PvfField.Number("[creature experience amount rate]", optional: true);
        internal static readonly PvfField<bool> SelectiveGrowCreatureField = PvfField.Marker("[selective grow creature]", optional: true);
        internal static readonly PvfField<int> UsingExpItemField = PvfField.Int32("[using exp item]", optional: true);
        internal static readonly PvfField<int> GrowAtypeItemIndexField = PvfField.Int32("[grow Atype item index]", optional: true);
        internal static readonly PvfField<int> GrowBtypeItemIndexField = PvfField.Int32("[grow Btype item index]", optional: true);
    }

    private readonly Lock gate = new();
    private Catalog? catalog;
    private readonly Dictionary<int, CreatureRule?> ruleCache = [];
    private readonly Dictionary<int, CreatureArtifactExperienceRule?> artifactCache = [];

    public string Name => "creature-rules";

    public ValueTask<int> WarmAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Load() is null ? 0 : 1);
    }

    public async ValueTask<CreatureRule?> GetAsync(int itemTemplateId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
            if (ruleCache.TryGetValue(itemTemplateId, out var cached)) return cached;
        var content = Load();
        if (!content.EquipmentPaths.TryGetValue(itemTemplateId, out var path))
            return CacheRule(itemTemplateId, null);

        var equipment = ReadEquipment(provider.Archive, path);
        if (!equipment.IsCreature || equipment.CreatureId <= 0)
            return CacheRule(itemTemplateId, null);

        var definition = await definitions.GetAsync(equipment.CreatureId, cancellationToken);
        if (definition is null)
            return CacheRule(itemTemplateId, null);

        var hunger = content.CreaturePaths.TryGetValue(equipment.CreatureId, out var creaturePath)
            ? ReadHunger(provider.Archive, creaturePath)
            : null;
        var result = new CreatureRule(
            itemTemplateId,
            equipment.CreatureId,
            definition.MaximumLevel,
            hunger,
            content.ExperienceThresholds,
            definition.Evolutions,
            equipment.IsEgg && equipment.OutputTemplateId > 0 ? equipment.OutputTemplateId : null,
            equipment.IsEgg)
        {
            OutputTemplateId = equipment.OutputTemplateId > 0 ? equipment.OutputTemplateId : null
        };
        return CacheRule(itemTemplateId, result);
    }

    public ValueTask<CreatureArtifactExperienceRule?> GetArtifactExperienceAsync(
        int itemTemplateId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
            if (artifactCache.TryGetValue(itemTemplateId, out var cached))
                return ValueTask.FromResult(cached);
        var content = Load();
        if (!content.EquipmentPaths.TryGetValue(itemTemplateId, out var path))
            return ValueTask.FromResult(CacheArtifact(itemTemplateId, null));

        var equipment = ReadEquipment(provider.Archive, path);
        CreatureArtifactExperienceRule? result = equipment.IsArtifact
            ? new CreatureArtifactExperienceRule(itemTemplateId,
                equipment.RequiredCreatureLevel, equipment.ExperiencePercent)
            : null;
        return ValueTask.FromResult(CacheArtifact(itemTemplateId, result));
    }

    private CreatureRule? CacheRule(int itemTemplateId, CreatureRule? value)
    {
        lock (gate)
        {
            ruleCache.TryAdd(itemTemplateId, value);
            return ruleCache[itemTemplateId];
        }
    }

    private CreatureArtifactExperienceRule? CacheArtifact(
        int itemTemplateId, CreatureArtifactExperienceRule? value)
    {
        lock (gate)
        {
            artifactCache.TryAdd(itemTemplateId, value);
            return artifactCache[itemTemplateId];
        }
    }

    private Catalog Load()
    {
        if (catalog is not null) return catalog;
        lock (gate) return catalog ??= Build(provider.Archive);
    }

    private static Catalog Build(PvfArchive archive)
    {
        const string expPath = "creature/ExpTable.tbl";
        var thresholds = new List<int>();
        var reader = archive.OpenScript(expPath);
        while (reader.Read(out var token))
            if (token.Kind == PvfTokenKind.Int32)
                thresholds.Add(token.Value);

        if (thresholds.Count == 0 || thresholds[0] < 0
            || thresholds.Zip(thresholds.Skip(1)).Any(pair => pair.Second <= pair.First))
            throw new InvalidDataException($"{expPath} has no increasing experience thresholds.");

        var equipment = PvfListFile.Read(archive, "equipment/equipment.lst")
            .Where(row => row.RelativePath.StartsWith("creature/", StringComparison.OrdinalIgnoreCase))
            .GroupBy(row => row.Id)
            .ToDictionary(group => group.Key, group => "equipment/" + group.First().RelativePath);
        var creatures = PvfListFile.Read(archive, "creature/creature.lst")
            .GroupBy(row => row.Id)
            .ToDictionary(group => group.Key, group => "creature/" + group.First().RelativePath);
        return new Catalog(thresholds.ToArray(), equipment, creatures);
    }

    private static EquipmentFields ReadEquipment(PvfArchive archive, string path)
    {
        var type = string.Empty;
        var subtype = 0;
        var species = 0;
        var output = 0;
        var minimum = 0;
        var percent = 0f;
        // Decode each occurrence independently: repeated scoped declarations retain last-value projection.
        foreach (var section in PvfDocument.Open(archive, path).Sections)
        {
            if (Fields.EquipmentTypeField.Matches(section.Name)) type = Fields.EquipmentTypeField.Decode(archive, section.Values, path);
            else if (Fields.SubTypeField.Matches(section.Name)) subtype = Fields.SubTypeField.Decode(archive, section.Values, path);
            else if (Fields.CreatureSpeciesField.Matches(section.Name)) species = Fields.CreatureSpeciesField.Decode(archive, section.Values, path);
            else if (Fields.OutputIndexField.Matches(section.Name)) output = Fields.OutputIndexField.Decode(archive, section.Values, path);
            else if (Fields.CreatureMinimumLevelField.Matches(section.Name)) minimum = Fields.CreatureMinimumLevelField.Decode(archive, section.Values, path);
            else if (Fields.CreatureExperienceAmountRateField.Matches(section.Name))
                percent = (float)Fields.CreatureExperienceAmountRateField.Decode(archive, section.Values, path);
        }

        return new EquipmentFields(
            type.Contains("[creature]", StringComparison.OrdinalIgnoreCase),
            type.Contains("[artifact", StringComparison.OrdinalIgnoreCase),
            subtype == 1, species, output, minimum, percent);
    }

    private static bool? ReadHunger(PvfArchive archive, string path)
    {
        var selective = false;
        var usingExp = false;
        var aItem = -1;
        var bItem = -1;
        foreach (var section in PvfDocument.Open(archive, path).Sections)
        {
            if (Fields.SelectiveGrowCreatureField.Matches(section.Name))
                selective |= Fields.SelectiveGrowCreatureField.Decode(archive, section.Values, path);
            else if (Fields.UsingExpItemField.Matches(section.Name))
                usingExp = Fields.UsingExpItemField.Decode(archive, section.Values, path) != 0;
            else if (Fields.GrowAtypeItemIndexField.Matches(section.Name))
                aItem = Fields.GrowAtypeItemIndexField.Decode(archive, section.Values, path);
            else if (Fields.GrowBtypeItemIndexField.Matches(section.Name))
                bItem = Fields.GrowBtypeItemIndexField.Decode(archive, section.Values, path);
        }

        // 70 CCreatureScript::IsItemGrowCreature requires an A/B item and [using exp item].
        // A missing creature script path is Unknown (handled by the caller), but a script
        // without that declaration uses the ordinary hunger mechanic.
        return !(selective && usingExp && (aItem != -1 || bItem != -1));
    }

    private sealed record Catalog(
        IReadOnlyList<int> ExperienceThresholds,
        Dictionary<int, string> EquipmentPaths,
        Dictionary<int, string> CreaturePaths);

    private readonly record struct EquipmentFields(
        bool IsCreature, bool IsArtifact, bool IsEgg, int CreatureId,
        int OutputTemplateId, int RequiredCreatureLevel, float ExperiencePercent);
}
