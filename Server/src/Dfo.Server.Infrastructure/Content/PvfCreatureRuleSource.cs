using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Creatures;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>Reads A21 creature rules without imposing a fixed creature level cap.</summary>
internal sealed class PvfCreatureRuleSource(
    PvfArchiveProvider provider,
    ICreatureDefinitionSource definitions) : ICreatureRuleSource
{
    private readonly Lock gate = new();
    private Catalog? catalog;
    private readonly Dictionary<int, CreatureRule?> ruleCache = [];
    private readonly Dictionary<int, CreatureArtifactExperienceRule?> artifactCache = [];

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
        var typeTag = archive.GetTag("[equipment type]").Offset;
        var subtypeTag = archive.GetTag("[sub type]").Offset;
        var speciesTag = archive.GetTag("[creature species]").Offset;
        var outputTag = archive.GetTag("[output index]").Offset;
        var minimumTag = archive.GetTag("[creature minimum level]").Offset;
        var experienceTag = archive.GetTag("[creature experience amount rate]").Offset;
        var section = -1;
        var type = string.Empty;
        var subtype = 0;
        var species = 0;
        var output = 0;
        var minimum = 0;
        var percent = 0f;
        var reader = archive.OpenScript(path);
        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier) { section = token.Value; continue; }
            if (section == typeTag && token.IsString)
                type = archive.ResolveString(token.Value);
            else if (section == subtypeTag && token.Kind == PvfTokenKind.Int32)
                subtype = token.Value;
            else if (section == speciesTag && token.Kind == PvfTokenKind.Int32)
                species = token.Value;
            else if (section == outputTag && token.Kind == PvfTokenKind.Int32)
                output = token.Value;
            else if (section == minimumTag && token.Kind == PvfTokenKind.Int32)
                minimum = token.Value;
            else if (section == experienceTag)
            {
                if (token.Kind == PvfTokenKind.Single) percent = token.AsSingle();
                else if (token.Kind == PvfTokenKind.Int32) percent = token.Value;
            }
        }

        return new EquipmentFields(
            type.Contains("[creature]", StringComparison.OrdinalIgnoreCase),
            type.Contains("[artifact", StringComparison.OrdinalIgnoreCase),
            subtype == 1, species, output, minimum, percent);
    }

    private static bool? ReadHunger(PvfArchive archive, string path)
    {
        var selectiveTag = archive.GetTag("[selective grow creature]").Offset;
        var usingExpTag = archive.GetTag("[using exp item]").Offset;
        var aTag = archive.GetTag("[grow Atype item index]").Offset;
        var bTag = archive.GetTag("[grow Btype item index]").Offset;
        var reader = archive.OpenScript(path);
        var section = -1;
        var selective = false;
        var usingExp = false;
        var aItem = -1;
        var bItem = -1;
        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier)
            {
                section = token.Value;
                if (section == selectiveTag) selective = true;
                continue;
            }
            if (token.Kind != PvfTokenKind.Int32) continue;
            if (section == usingExpTag) usingExp = token.Value != 0;
            else if (section == aTag) aItem = token.Value;
            else if (section == bTag) bItem = token.Value;
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
