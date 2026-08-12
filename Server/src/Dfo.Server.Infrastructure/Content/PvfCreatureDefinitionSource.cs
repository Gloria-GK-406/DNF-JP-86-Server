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
internal sealed class PvfCreatureDefinitionSource : ICreatureDefinitionSource
{
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

    private Catalog Load()
    {
        if (_catalog is not null)
            return _catalog;

        lock (_gate)
            return _catalog ??= Build(_provider.Archive);
    }

    private static Catalog Build(PvfArchive archive)
    {
        var tags = new CreatureTags(archive);
        var ordered = new List<CreatureDefinition>();
        var byId = new Dictionary<int, CreatureDefinition>();
        var creatureIdByStem = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var (creatureId, relativePath) in PvfListFile.Read(archive, ListPath))
        {
            var fileIndex = archive.FindFile($"{CreatureDirectory}/{relativePath}");
            if (fileIndex < 0)
                continue;

            var ambient = ReadAmbientLines(archive, tags, relativePath);
            var definition = Project(archive, tags, creatureId, fileIndex, ambient);
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

        return new Catalog(ordered, byId, BuildEquipmentIndex(archive, tags, creatureIdByStem));
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
    private static Dictionary<int, int> BuildEquipmentIndex(
        PvfArchive archive,
        CreatureTags tags,
        Dictionary<string, int> creatureIdByStem)
    {
        var index = new Dictionary<int, int>();
        var listIndex = archive.FindFile(EquipmentListPath);
        if (listIndex < 0)
            return index;

        foreach (var (itemTemplateId, relativePath) in PvfListFile.Read(archive, EquipmentListPath))
        {
            if (!relativePath.StartsWith(CreatureEquipmentPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var stem = StemOf(relativePath);
            if (stem.Length == 0 || !creatureIdByStem.TryGetValue(stem, out var creatureId))
                continue;

            var fileIndex = archive.FindFile($"{EquipmentDirectory}/{relativePath}");
            if (fileIndex < 0 || !DeclaresCreatureType(archive, tags, fileIndex))
                continue;

            index.TryAdd(itemTemplateId, creatureId);
        }

        return index;
    }

    private static bool DeclaresCreatureType(PvfArchive archive, CreatureTags tags, int fileIndex)
    {
        if (!tags.EquipmentType.IsResolved)
            return false;

        var reader = archive.OpenScript(fileIndex);
        var inEquipmentType = false;

        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier)
            {
                inEquipmentType = token.Value == tags.EquipmentType.Offset;
                continue;
            }

            if (inEquipmentType
                && token.IsString
                && archive.ResolveString(token.Value)
                    .Contains(CreatureEquipmentType, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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
        CreatureTags tags,
        int creatureId,
        int fileIndex,
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

        var reader = archive.OpenScript(fileIndex);
        var section = -1;

        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier)
            {
                section = token.Value;
                continue;
            }

            if (section == tags.Name && token.IsString)
                name = archive.ResolveString(token.Value);
            else if (section == tags.ArtifactSlot && token.IsString)
                artifactSlots.Add(archive.ResolveString(token.Value));
            else if (token.Kind != PvfTokenKind.Int32)
                continue;
            else if (section == tags.StartLevel)
                startLevel = token.Value;
            else if (section == tags.PermissionLevel)
                permissionLevel = token.Value;
            else if (section == tags.MaximumLevel)
                maximumLevel = token.Value;
            else if (section == tags.EvolutionLevel)
                evolutionLevels.Add(token.Value);
            else if (section == tags.EvolutionQuest)
                evolutionQuests.Add(token.Value);
            else if (section == tags.EvolutionCreatureId)
                evolutionTargets.Add(token.Value);
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
        CreatureTags tags,
        string creatureRelativePath)
    {
        var stem = Path.GetFileNameWithoutExtension(creatureRelativePath);
        if (string.IsNullOrEmpty(stem))
            return [];

        var scriptIndex = archive.FindFile($"{ScriptDirectory}/{stem}.wrd");
        if (scriptIndex < 0 || !tags.HasAmbientTags)
            return [];

        var reader = archive.OpenScript(scriptIndex);
        if (!reader.TryMovePast(new Dfo.Pvf.Tokens.TagRef(tags.Ambient)))
            return [];
        if (!reader.TryMovePast(new Dfo.Pvf.Tokens.TagRef(tags.RoomList)))
            return [];

        var lines = new List<string>();
        while (reader.TryReadSectionValue(out var token))
        {
            if (!token.IsString)
                continue;

            var line = archive.ResolveString(token.Value);
            if (line.Length > 0)
                lines.Add(line);
        }

        return lines;
    }

    private sealed class CreatureTags
    {
        public CreatureTags(PvfArchive archive)
        {
            Name = archive.GetTag("[name]").Offset;
            StartLevel = archive.GetTag("[start level]").Offset;
            PermissionLevel = archive.GetTag("[permission level]").Offset;
            MaximumLevel = archive.GetTag("[max level]").Offset;
            ArtifactSlot = archive.GetTag("[artifact slot]").Offset;
            EvolutionLevel = archive.GetTag("[evolution level]").Offset;
            EvolutionQuest = archive.GetTag("[evolution quest]").Offset;
            EvolutionCreatureId = archive.GetTag("[evolution creature id]").Offset;
            Ambient = archive.GetTag("[ambient]").Offset;
            EquipmentType = archive.GetTag("[equipment type]");
            RoomList = archive.GetTag("[room list]").Offset;
        }

        public int Name { get; }
        public int StartLevel { get; }
        public int PermissionLevel { get; }
        public int MaximumLevel { get; }
        public int ArtifactSlot { get; }
        public int EvolutionLevel { get; }
        public int EvolutionQuest { get; }
        public int EvolutionCreatureId { get; }
        public int Ambient { get; }
        public TagRef EquipmentType { get; }
        public int RoomList { get; }

        public bool HasAmbientTags => Ambient >= 0 && RoomList >= 0;
    }

    private sealed record Catalog(
        IReadOnlyList<CreatureDefinition> Ordered,
        Dictionary<int, CreatureDefinition> ById,
        Dictionary<int, int> CreatureIdByEquipment);
}
