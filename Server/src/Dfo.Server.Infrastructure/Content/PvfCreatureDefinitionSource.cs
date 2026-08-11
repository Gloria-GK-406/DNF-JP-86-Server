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

        foreach (var (creatureId, relativePath) in PvfListFile.Read(archive, ListPath))
        {
            var fileIndex = archive.FindFile($"{CreatureDirectory}/{relativePath}");
            if (fileIndex < 0)
                continue;

            var ambient = ReadAmbientLines(archive, tags, relativePath);
            var definition = Project(archive, tags, creatureId, fileIndex, ambient);
            ordered.Add(definition);
            byId.TryAdd(creatureId, definition);
        }

        if (ordered.Count == 0)
            throw new InvalidOperationException($"{ListPath} yielded no creature definitions.");

        return new Catalog(ordered, byId);
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
        public int RoomList { get; }

        public bool HasAmbientTags => Ambient >= 0 && RoomList >= 0;
    }

    private sealed record Catalog(
        IReadOnlyList<CreatureDefinition> Ordered,
        Dictionary<int, CreatureDefinition> ById);
}
