using Dfo.Pvf.Schema;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>
/// Projects the archive's creature scripts into <see cref="CreatureDefinition"/>.
/// </summary>
/// <remarks>
/// A creature's own file carries its levels, artifact slots and evolution chain. Its town
/// greetings live in a companion script beside it, under <c>creature/script</c>, which only
/// some creatures have.
/// </remarks>
internal sealed class PvfCreatureDefinitionSource : ICreatureDefinitionSource, Dfo.Server.Abstractions.Content.IContentWarmup
{
    private static class Fields
    {
        internal static readonly PvfField<string> NameField = PvfField.String("[name]", optional: true);
        internal static readonly PvfField<int> StartLevelField = PvfField.Int32("[start level]", optional: true);
        internal static readonly PvfField<int> PermissionLevelField = PvfField.Int32("[permission level]", optional: true);
        internal static readonly PvfField<int> MaxLevelField = PvfField.Int32("[max level]", optional: true);
        internal static readonly PvfField<IReadOnlyList<string>> ArtifactSlotField = PvfField.Strings("[artifact slot]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> EvolutionLevelField = PvfField.Int32List("[evolution level]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> EvolutionQuestField = PvfField.Int32List("[evolution quest]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> EvolutionCreatureIdField = PvfField.Int32List("[evolution creature id]", optional: true);
        internal static readonly PvfField<bool> AmbientField = PvfField.Marker("[ambient]", optional: true);
        internal static readonly PvfField<string> EquipmentTypeField = PvfField.Custom<string>("[equipment type]", (archive, values, path) =>
        {
            if (values.Count == 0 || !values[0].IsString || values.Skip(1).Any(token => token.Kind != PvfTokenKind.Int32))
                throw new InvalidDataException($"PVF '{path}', field '[equipment type]': expected type string and optional Int32 operands.");
            return archive.ResolveString(values[0].Value);
        }, optional: true);
        internal static readonly PvfField<int> OutputIndexField = PvfField.Int32("[output index]", optional: true);
        internal static readonly PvfField<int> CreatureSpeciesField = PvfField.Int32("[creature species]", optional: true);
        internal static readonly PvfField<IReadOnlyList<string>> RoomListField = PvfField.Strings("[room list]", optional: true);
    }

    private const string ListPath = "creature/creature.lst";
    private const string CreatureDirectory = "creature";
    private const string ScriptDirectory = "creature/script";
    private const string EquipmentListPath = "equipment/equipment.lst";
    private const string EquipmentDirectory = "equipment";
    private const string CreatureEquipmentPrefix = "creature/";
    private const string CreatureEquipmentType = "[creature]";

    private readonly PvfArchiveProvider _provider;
    private readonly Lock _gate = new();
    private Catalog? _catalog;

    public string Name => "creatures";

    public ValueTask<int> WarmAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Load().Ordered.Count);
    }

    public PvfCreatureDefinitionSource(PvfArchiveProvider provider) => _provider = provider;

    public ValueTask<IReadOnlyList<CreatureDefinition>> GetAllAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Load().Ordered);
    }

    public ValueTask<CreatureDefinition?> GetAsync(int creatureId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Load().ById.GetValueOrDefault(creatureId));
    }

    public ValueTask<CreatureDefinition?> FindByEquipmentAsync(
        int itemTemplateId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var catalog = Load();
        return ValueTask.FromResult(
            catalog.CreatureIdByEquipment.TryGetValue(itemTemplateId, out var creatureId)
                ? catalog.ById.GetValueOrDefault(creatureId)
                : null);
    }

    public ValueTask<bool> CanResolveEvolutionEquipmentAsync(
        int currentEquipmentItemId,
        int targetCreatureId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var catalog = Load();
        if (catalog.OutputEquipmentByEquipment.TryGetValue(
                currentEquipmentItemId,
                out var outputItemId)
            && outputItemId > 0
            && outputItemId != currentEquipmentItemId
            && catalog.CreatureIdByEquipment.ContainsKey(outputItemId))
        {
            return ValueTask.FromResult(true);
        }

        return ValueTask.FromResult(
            catalog.CreatureIdsWithEquipment.Contains(targetCreatureId));
    }

    private Catalog Load()
    {
        if (_catalog is not null)
            return _catalog;

        lock (_gate)
            return _catalog ??= Build(_provider.Archive);
    }

    private static Catalog Build(PvfArchive archive)
    {
        var ordered = new List<CreatureDefinition>();
        var byId = new Dictionary<int, CreatureDefinition>();
        var creatureIdByStem = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var (creatureId, relativePath) in PvfListFile.Read(archive, ListPath))
        {
            var fileIndex = archive.FindFile($"{CreatureDirectory}/{relativePath}");
            if (fileIndex < 0)
                continue;

            var ambient = ReadAmbientLines(archive, relativePath);
            var definition = Project(archive, creatureId, $"{CreatureDirectory}/{relativePath}", ambient);
            ordered.Add(definition);
            byId.TryAdd(creatureId, definition);

            // File names are not unique across the list — 408 entries share 332 stems — and
            // the legacy index keeps the first. Keeping the last instead would silently
            // reassign creatures whose name repeats.
            var stem = StemOf(relativePath);
            if (stem.Length > 0)
                creatureIdByStem.TryAdd(stem, creatureId);
        }

        if (ordered.Count == 0)
            throw new InvalidOperationException($"{ListPath} yielded no creature definitions.");

        var equipment = BuildEquipmentIndex(archive, creatureIdByStem);
        return new Catalog(
            ordered,
            byId,
            equipment.CreatureIdByEquipment,
            equipment.CreatureIdByEquipment.Values.ToHashSet(),
            equipment.OutputEquipmentByEquipment);
    }

    /// <summary>
    /// Maps each creature-equipment identifier onto the creature it denotes.
    /// </summary>
    /// <remarks>
    /// The two identifier spaces are disjoint, so this hop is the only thing that turns an
    /// equipped item into a creature. The equipment list names a file; that file's name
    /// matches a creature list entry. Both the directory and the declared equipment type
    /// are checked, because the directory alone admits equipment that merely lives beside
    /// creatures.
    /// </remarks>
    private static EquipmentIndex BuildEquipmentIndex(
        PvfArchive archive,
        Dictionary<string, int> creatureIdByStem)
    {
        var index = new Dictionary<int, int>();
        var outputs = new Dictionary<int, int>();
        var listIndex = archive.FindFile(EquipmentListPath);
        if (listIndex < 0)
            return new EquipmentIndex(index, outputs);

        foreach (var (itemTemplateId, relativePath) in PvfListFile.Read(archive, EquipmentListPath))
        {
            if (!relativePath.StartsWith(CreatureEquipmentPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var fileIndex = archive.FindFile($"{EquipmentDirectory}/{relativePath}");
            if (fileIndex < 0
                || !TryReadCreatureEquipment(archive, $"{EquipmentDirectory}/{relativePath}", out var outputItemId, out var creatureId))
                continue;

            // Eggs and alternate equipment names need not share the .cre file stem.
            // The explicit species is authoritative; filename matching is legacy fallback.
            if (creatureId <= 0 && !creatureIdByStem.TryGetValue(StemOf(relativePath), out creatureId))
                continue;

            index.TryAdd(itemTemplateId, creatureId);
            if (outputItemId > 0)
                outputs.TryAdd(itemTemplateId, outputItemId);
        }

        return new EquipmentIndex(index, outputs);
    }

    private static bool TryReadCreatureEquipment(
        PvfArchive archive,
        string path,
        out int outputItemId, out int creatureId)
    {
        outputItemId = 0;
        creatureId = 0;
        var declaresCreatureType = false;
        foreach (var section in PvfDocument.Open(archive, path).Sections)
        {
            if (Fields.EquipmentTypeField.Matches(section.Name))
                declaresCreatureType |= Fields.EquipmentTypeField.Decode(archive, section.Values, path)
                    .Contains(CreatureEquipmentType, StringComparison.OrdinalIgnoreCase);
            else if (Fields.OutputIndexField.Matches(section.Name))
                outputItemId = Fields.OutputIndexField.Decode(archive, section.Values, path);
            else if (Fields.CreatureSpeciesField.Matches(section.Name))
                creatureId = Fields.CreatureSpeciesField.Decode(archive, section.Values, path);
        }

        return declaresCreatureType;
    }

    private static string StemOf(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        var lastSlash = normalized.LastIndexOf('/');
        var name = lastSlash >= 0 ? normalized[(lastSlash + 1)..] : normalized;
        var lastDot = name.LastIndexOf('.');
        return lastDot > 0 ? name[..lastDot] : name;
    }

    private static CreatureDefinition Project(
        PvfArchive archive,
        int creatureId,
        string path,
        IReadOnlyList<string> ambientLines)
    {
        var name = string.Empty;
        var startLevel = 0;
        var permissionLevel = 0;
        var maximumLevel = 0;
        var artifactSlots = new List<string>();
        var evolutionLevels = new List<int>();
        var evolutionQuests = new List<int>();
        var evolutionTargets = new List<int>();

        foreach (var section in PvfDocument.Open(archive, path).Sections)
        {
            if (Fields.NameField.Matches(section.Name)) name = Fields.NameField.Decode(archive, section.Values, path);
            else if (Fields.ArtifactSlotField.Matches(section.Name)) artifactSlots.AddRange(Fields.ArtifactSlotField.Decode(archive, section.Values, path));
            else if (Fields.StartLevelField.Matches(section.Name)) startLevel = Fields.StartLevelField.Decode(archive, section.Values, path);
            else if (Fields.PermissionLevelField.Matches(section.Name)) permissionLevel = Fields.PermissionLevelField.Decode(archive, section.Values, path);
            else if (Fields.MaxLevelField.Matches(section.Name)) maximumLevel = Fields.MaxLevelField.Decode(archive, section.Values, path);
            else if (Fields.EvolutionLevelField.Matches(section.Name)) evolutionLevels.AddRange(Fields.EvolutionLevelField.Decode(archive, section.Values, path));
            else if (Fields.EvolutionQuestField.Matches(section.Name)) evolutionQuests.AddRange(Fields.EvolutionQuestField.Decode(archive, section.Values, path));
            else if (Fields.EvolutionCreatureIdField.Matches(section.Name)) evolutionTargets.AddRange(Fields.EvolutionCreatureIdField.Decode(archive, section.Values, path));
        }

        // The content declares the evolution tags on creatures that do not evolve, using
        // zero as the placeholder. A step that leads nowhere is not a step, so those are
        // dropped rather than surfaced as an evolution into creature zero.
        var evolutions = new List<CreatureEvolution>(evolutionTargets.Count);
        for (var i = 0; i < evolutionTargets.Count; i++)
        {
            if (evolutionTargets[i] <= 0)
                continue;

            evolutions.Add(new CreatureEvolution(
                i < evolutionLevels.Count && evolutionLevels[i] > 0 ? evolutionLevels[i] : null,
                i < evolutionQuests.Count && evolutionQuests[i] > 0 ? evolutionQuests[i] : null,
                evolutionTargets[i]));
        }

        return new CreatureDefinition(
            creatureId, name, startLevel, permissionLevel, maximumLevel, artifactSlots, evolutions, ambientLines);
    }

    /// <summary>
    /// Reads the general town greetings, which sit directly under the ambient room list.
    /// Town-specific blocks nested below it are separate content and are not folded in.
    /// </summary>
    private static IReadOnlyList<string> ReadAmbientLines(
        PvfArchive archive,
        string creatureRelativePath)
    {
        var stem = Path.GetFileNameWithoutExtension(creatureRelativePath);
        if (string.IsNullOrEmpty(stem))
            return [];

        var path = $"{ScriptDirectory}/{stem}.wrd";
        if (archive.FindFile(path) < 0) return [];

        var ambientSeen = false;
        IReadOnlyList<string>? lines = null;
        foreach (var section in PvfDocument.Open(archive, path).Sections)
        {
            if (Fields.AmbientField.Matches(section.Name))
            {
                Fields.AmbientField.Decode(archive, section.Values, path);
                ambientSeen = true;
            }
            else if (Fields.RoomListField.Matches(section.Name))
            {
                var values = Fields.RoomListField.Decode(archive, section.Values, path);
                // Preserve the first ambient room list, excluding each nested town heading.
                if (ambientSeen && lines is null) lines = values.Where(line => line.Length > 0).ToArray();
            }
        }
        return lines ?? [];
    }

    private sealed record Catalog(
        IReadOnlyList<CreatureDefinition> Ordered,
        Dictionary<int, CreatureDefinition> ById,
        Dictionary<int, int> CreatureIdByEquipment,
        HashSet<int> CreatureIdsWithEquipment,
        Dictionary<int, int> OutputEquipmentByEquipment);

    private sealed record EquipmentIndex(
        Dictionary<int, int> CreatureIdByEquipment,
        Dictionary<int, int> OutputEquipmentByEquipment);
}
