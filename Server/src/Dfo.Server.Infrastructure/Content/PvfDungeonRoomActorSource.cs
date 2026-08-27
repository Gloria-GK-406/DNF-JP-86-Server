using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>
/// Reads a room's monsters and AI characters out of its map declaration.
/// </summary>
/// <remarks>
/// <c>map/map.lst</c> maps a map identifier to its <c>.map</c> file. That file declares its
/// monsters in one <c>[monster]</c> block as a flat run of records -- eight integers then two
/// quoted words per monster -- and its AI characters in an <c>[ai character]</c> block as four
/// integers, two quoted words and two more integers each. Both blocks are read as runs rather
/// than one record apiece: the tutorial's later rooms declare four and five monsters in a single
/// block, so a reader that stopped after the first would leave every room but the emptiest short.
/// <para>
/// An AI character's level is not in the map. It comes from the character's own <c>.aic</c> file,
/// whose <c>[minimum info]</c> opens with a quoted name and then integers of which the fifth is
/// the level.
/// </para>
/// </remarks>
internal sealed class PvfDungeonRoomActorSource : IDungeonRoomActorSource
{
    private const string MapListPath = "map/map.lst";
    private const string MapDirectory = "map";
    private const string AiCharacterListPath = "aicharacter/aicharacter.lst";
    private const string AiCharacterDirectory = "aicharacter";

    /// <summary>The actor type an AI character is reported as.</summary>
    /// <remarks>
    /// Five through eight are the AI-character band and the map's own words choose within it. Only
    /// five has been observed, and the mapping from the map's quoted words to a number inside that
    /// band is not established, so this reports the band's floor rather than guessing.
    /// </remarks>
    private const byte AiCharacterActorType = 5;

    /// <summary>The highest actor type that is still a monster.</summary>
    private const byte HighestMonsterActorType = 3;

    /// <summary><c>code level type x y z</c> and two more the map keeps for itself.</summary>
    private const int MonsterRecordIntegers = 8;

    /// <summary><c>code x y z</c>, then two more after the quoted words.</summary>
    private const int AiCharacterRecordIntegers = 6;

    /// <summary>Which integer of <c>[minimum info]</c> carries the level.</summary>
    private const int AiCharacterLevelPosition = 5;

    private readonly PvfArchiveProvider _provider;
    private readonly Lock _gate = new();
    private readonly Dictionary<int, IReadOnlyList<DungeonRoomActor>> _byMap = [];
    private readonly Dictionary<int, byte> _aiCharacterLevels = [];
    private IReadOnlyDictionary<int, string>? _mapPaths;
    private IReadOnlyDictionary<int, string>? _aiCharacterPaths;

    public PvfDungeonRoomActorSource(PvfArchiveProvider provider) => _provider = provider;

    public ValueTask<IReadOnlyList<DungeonRoomActor>> GetAsync(
        int mapId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_byMap.TryGetValue(mapId, out var cached))
                return ValueTask.FromResult(cached);

            var actors = Read(_provider.Archive, mapId);
            _byMap[mapId] = actors;
            return ValueTask.FromResult(actors);
        }
    }

    private IReadOnlyDictionary<int, string> MapPaths()
    {
        if (_mapPaths is not null)
            return _mapPaths;

        var paths = new Dictionary<int, string>();
        foreach (var (id, relativePath) in PvfListFile.Read(_provider.Archive, MapListPath))
            paths.TryAdd(id, relativePath);
        return _mapPaths = paths;
    }

    private IReadOnlyDictionary<int, string> AiCharacterPaths()
    {
        if (_aiCharacterPaths is not null)
            return _aiCharacterPaths;

        var paths = new Dictionary<int, string>();
        foreach (var (id, relativePath) in PvfListFile.Read(_provider.Archive, AiCharacterListPath))
            paths.TryAdd(id, relativePath);
        return _aiCharacterPaths = paths;
    }

    private IReadOnlyList<DungeonRoomActor> Read(PvfArchive archive, int mapId)
    {
        if (!MapPaths().TryGetValue(mapId, out var relativePath))
            return [];

        var fileIndex = archive.FindFile($"{MapDirectory}/{relativePath}");
        if (fileIndex < 0)
            return [];

        var monsterOpen = archive.GetTag("[monster]").Offset;
        var monsterClose = archive.GetTag("[/monster]").Offset;
        var aiOpen = archive.GetTag("[ai character]").Offset;
        var aiClose = archive.GetTag("[/ai character]").Offset;

        var monsters = new List<DungeonRoomActor>();
        var aiCharacters = new List<DungeonRoomActor>();
        var record = new List<int>();
        var block = Block.None;

        var reader = archive.OpenScript(fileIndex);
        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier)
            {
                if (token.Value == monsterOpen)
                {
                    block = Block.Monster;
                }
                else if (token.Value == aiOpen)
                {
                    block = Block.AiCharacter;
                }
                else if (token.Value == monsterClose || token.Value == aiClose)
                {
                    block = Block.None;
                }
                else
                {
                    // Any other tag ends the block too. The observed maps close theirs properly,
                    // but one that did not would otherwise swallow the next section's integers.
                    block = Block.None;
                }

                record.Clear();
                continue;
            }

            if (block == Block.None)
                continue;

            // The quoted words inside a record carry placement and behaviour, which are not read
            // here. They are skipped rather than counted: the integers alone decide where one
            // record ends and the next begins.
            if (token.Kind != PvfTokenKind.Int32)
                continue;

            record.Add(token.Value);
            if (block == Block.Monster && record.Count == MonsterRecordIntegers)
            {
                monsters.Add(Monster(record));
                record.Clear();
            }
            else if (block == Block.AiCharacter && record.Count == AiCharacterRecordIntegers)
            {
                if (TryAiCharacter(archive, record[0], out var actor))
                    aiCharacters.Add(actor);
                record.Clear();
            }
        }

        return [.. monsters, .. aiCharacters];
    }

    private static DungeonRoomActor Monster(IReadOnlyList<int> record)
    {
        var level = record[1] is > 0 and <= byte.MaxValue ? (byte)record[1] : (byte)1;

        // A type outside the monster band would reach the client as an AI character. The archive
        // has not been seen to declare one; clamping keeps a map that did from turning a monster
        // into something that fights beside the player.
        var type = record[2] is >= 0 and <= HighestMonsterActorType ? (byte)record[2] : (byte)0;
        return new DungeonRoomActor(record[0], level, type);
    }

    private bool TryAiCharacter(PvfArchive archive, int code, out DungeonRoomActor actor)
    {
        actor = default;
        if (code <= 0)
            return false;

        if (!_aiCharacterLevels.TryGetValue(code, out var level))
        {
            level = AiCharacterLevel(archive, code);
            _aiCharacterLevels[code] = level;
        }

        // A character the archive does not declare is dropped rather than sent at level zero: the
        // client is being told to build an actor, and one it cannot resolve is worse than absent.
        if (level == 0)
            return false;

        actor = new DungeonRoomActor(code, level, AiCharacterActorType);
        return true;
    }

    private byte AiCharacterLevel(PvfArchive archive, int code)
    {
        if (!AiCharacterPaths().TryGetValue(code, out var relativePath))
            return 0;

        var fileIndex = archive.FindFile($"{AiCharacterDirectory}/{relativePath}");
        if (fileIndex < 0)
            return 0;

        var minimumInfo = archive.GetTag("[minimum info]");
        if (!minimumInfo.IsResolved)
            return 0;

        var reader = archive.OpenScript(fileIndex);
        if (!reader.TryMovePast(minimumInfo))
            return 0;

        var seen = 0;
        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier)
                return 0;
            if (token.Kind != PvfTokenKind.Int32)
                continue;
            if (++seen == AiCharacterLevelPosition)
                return token.Value is > 0 and <= byte.MaxValue ? (byte)token.Value : (byte)0;
        }

        return 0;
    }

    private enum Block
    {
        None,
        Monster,
        AiCharacter,
    }
}
