using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>Caches complete MAP declarations and adapts unconditional rows for room generation.</summary>
internal sealed class PvfDungeonRoomActorSource(PvfArchiveProvider provider) : IDungeonRoomActorSource, Dfo.Server.Abstractions.Content.IContentWarmup
{
    private readonly Lock gate = new();
    private readonly Dictionary<int, DungeonMapActorDeclarations> declarations = [];
    private readonly Dictionary<int, IReadOnlyList<DungeonRoomActor>> actors = [];
    private readonly Dictionary<int, byte> aiCharacterLevels = [];
    private readonly Dictionary<string, IReadOnlyDictionary<int, string>> listPaths = [];
    private static readonly PvfField<IReadOnlyList<PvfToken>> MinimumInfo = PvfField.Tokens("[minimum info]", optional: true);

    public ValueTask<DungeonMapActorDeclarations> GetDeclarationsAsync(int mapId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate) return ValueTask.FromResult(ReadDeclarations(mapId));
    }

    public ValueTask<IReadOnlyList<DungeonRoomActor>> GetAsync(int mapId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (!actors.TryGetValue(mapId, out var result))
            {
                result = ReadDeclarations(mapId).Actors
                    .Where(row => row.Source is not (DungeonActorSpawnSource.ConditionalMonster or DungeonActorSpawnSource.ConditionalSummon))
                    .Where(row => row.Source != DungeonActorSpawnSource.AiCharacter || row.LevelValue > 0)
                    .OrderBy(row => row.Source)
                    .Select(ToActor).ToArray();
                actors[mapId] = result;
            }
            return ValueTask.FromResult(result);
        }
    }

    private DungeonMapActorDeclarations ReadDeclarations(int mapId)
    {
        if (declarations.TryGetValue(mapId, out var cached)) return cached;
        var archive = provider.Archive;
        if (!Paths("map").TryGetValue(mapId, out var relativePath) || archive.FindFile($"map/{relativePath}") < 0)
            return declarations[mapId] = new([], []);
        var path = $"map/{relativePath}";
        var parsed = PvfMapActorDeclarationParser.Parse(archive, PvfDocument.Open(archive, path), path);
        var enriched = parsed.Actors.Select(row => row.Source switch
        {
            DungeonActorSpawnSource.AiCharacter => row with { LevelValue = AiCharacterLevel(row.Code) },
            DungeonActorSpawnSource.SpecialPassiveObject => row with { IsSceneOwnedWave = IsVerifiedSceneWave(row.ParentObjectCode ?? 0) },
            _ => row,
        }).ToArray();
        return declarations[mapId] = parsed with { Actors = enriched };
    }

    private static DungeonRoomActor ToActor(DungeonActorSpawnDeclaration row)
    {
        var type = checked((byte)(row.Rank + (row.Source == DungeonActorSpawnSource.AiCharacter ? 5 : 0)));
        var level = !row.RelativeLevel && row.LevelValue > 0 ? (byte)Math.Min(row.LevelValue, byte.MaxValue) : (byte)0;
        return new(row.Code, level, type, row.RelativeLevel ? row.LevelValue : null, row.RandomDropCount)
        {
            Declaration = row,
            IsHostile = !row.IsDummy && row.Faction == DungeonActorFaction.Monster,
        };
    }

    public string Name => "dungeon-room-actor-index";

    /// <remarks>The list files the actor readers resolve ids through; the per-map reads stay keyed.</remarks>
    public ValueTask<int> WarmAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate) return ValueTask.FromResult(new[] { "map", "aicharacter", "passiveobject" }.Sum(folder => Paths(folder).Count));
    }

    private IReadOnlyDictionary<int, string> Paths(string folder)
    {
        if (listPaths.TryGetValue(folder, out var paths)) return paths;
        var result = new Dictionary<int, string>();
        foreach (var (id, path) in PvfListFile.Read(provider.Archive, $"{folder}/{folder}.lst")) result.TryAdd(id, path);
        return listPaths[folder] = result;
    }

    private byte AiCharacterLevel(int code)
    {
        if (aiCharacterLevels.TryGetValue(code, out var cached)) return cached;
        var archive = provider.Archive;
        if (!Paths("aicharacter").TryGetValue(code, out var relativePath) || archive.FindFile($"aicharacter/{relativePath}") < 0)
            return aiCharacterLevels[code] = 0;
        var path = $"aicharacter/{relativePath}";
        if (!PvfDocument.Open(archive, path).TryRead(MinimumInfo, out var values)) return aiCharacterLevels[code] = 0;
        if (values.Count != 16 || values[0].Kind != PvfTokenKind.QuotedString
            || !values.Skip(1).Take(11).All(token => token.Kind == PvfTokenKind.Int32)
            || values[12].Kind != PvfTokenKind.QuotedString
            || !values.Skip(13).All(token => token.Kind == PvfTokenKind.Int32)
            || values[5].Value is <= 0 or > byte.MaxValue)
            throw new InvalidDataException($"PVF '{path}', field '[minimum info]': invalid APC level declaration.");
        return aiCharacterLevels[code] = (byte)values[5].Value;
    }

    private bool IsVerifiedSceneWave(int code)
    {
        if (!Paths("passiveobject").TryGetValue(code, out var path)) return false;
        // Confirmed controller families; arbitrary OBJ [int data] is not enough evidence.
        var normalized = path.Replace('\\', '/').ToLowerInvariant();
        if (code == 1112 && normalized == "mapobject/obstacle/elevatorsummon.obj") return true;
        if (!normalized.StartsWith("actionobject/map/gentdefence/", StringComparison.Ordinal)) return false;
        return Path.GetFileName(normalized) is "gentdefence_wave1.obj" or "gentdefence_wave1_2.obj"
            or "gentdefence_wave1_3.obj" or "gentdefence_wave2.obj" or "gentdefence_wave2_2.obj"
            or "gentdefence_wave2_3.obj" or "gentdefence_wave3.obj" or "gentdefence_wave3_2.obj"
            or "gentdefence_wave3_3.obj";
    }
}
