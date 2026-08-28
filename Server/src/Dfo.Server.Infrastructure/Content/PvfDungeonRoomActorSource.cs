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

    /// <summary>
    /// <c>code, level, autoLevel, x, y, z, randomDrops, fixedDrops</c>, then two quoted words.
    /// </summary>
    /// <remarks>
    /// The third integer is the map's AUTO-LEVEL, not the actor's rank. Reading it as the rank is
    /// what made ordinary monsters arrive as bosses: dungeon 144's rooms declare auto-level 3 and
    /// 4 on monsters the map calls <c>[normal]</c>, and 3 is the rank number for a boss. The rank
    /// is the second quoted word, which this used to skip.
    /// </remarks>
    private const int MonsterRecordIntegers = 8;

    /// <summary>How many quoted words close a monster record: its placement, then its rank.</summary>
    private const int MonsterRecordWords = 2;

    /// <summary>Which quoted word of a monster record carries its rank.</summary>
    private const int MonsterRankWordPosition = 1;

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
        var words = new List<string>();
        var block = Block.None;

        // A monster record is finished by its quoted rank, which arrives AFTER its last integer,
        // so one cannot be emitted the moment its integers are complete. It is held until whatever
        // comes next says it is over: the next record's first integer, or the end of the block.
        void FlushMonster()
        {
            if (record.Count == MonsterRecordIntegers)
                monsters.Add(Monster(record, words));
            record.Clear();
            words.Clear();
        }

        var reader = archive.OpenScript(fileIndex);
        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier)
            {
                var previous = block;
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

                if (previous == Block.Monster)
                    FlushMonster();
                record.Clear();
                words.Clear();
                continue;
            }

            if (block == Block.None)
                continue;

            // A monster record closes with two quoted words -- where it stands, and what rank it
            // is -- and the rank is a field. An AI character's words are read from its own file
            // instead, so they are still skipped there.
            if (token.Kind == PvfTokenKind.QuotedString)
            {
                if (block == Block.Monster
                    && record.Count == MonsterRecordIntegers
                    && words.Count < MonsterRecordWords)
                {
                    words.Add(archive.ResolveString(token.Value));
                }

                continue;
            }

            if (token.Kind != PvfTokenKind.Int32)
                continue;

            if (block == Block.Monster && record.Count == MonsterRecordIntegers)
                FlushMonster();

            record.Add(token.Value);
            if (block == Block.AiCharacter && record.Count == AiCharacterRecordIntegers)
            {
                if (TryAiCharacter(archive, record[0], out var actor))
                    aiCharacters.Add(actor);
                record.Clear();
                words.Clear();
            }
        }

        if (block == Block.Monster)
            FlushMonster();
        return [.. monsters, .. aiCharacters];
    }

    /// <remarks>
    /// The level is passed through as declared, zero included. Zero is not "level zero": it is the
    /// map declining to set one, and what the monster is then worth comes from the dungeon's own
    /// <c>[basis level]</c> -- which this source cannot see, because a map belongs to no one
    /// dungeon. The caller substitutes it.
    /// </remarks>
    private static DungeonRoomActor Monster(IReadOnlyList<int> record, IReadOnlyList<string> words)
    {
        var level = record[1] is > 0 and <= byte.MaxValue ? (byte)record[1] : (byte)0;
        var rank = words.Count > MonsterRankWordPosition
            ? RankOf(words[MonsterRankWordPosition])
            : (byte)0;
        return new DungeonRoomActor(record[0], level, rank);
    }

    /// <summary>The map's word for how big a monster is, as the number the client draws it by.</summary>
    /// <remarks>
    /// Four words and four numbers, in the order the archive lists them. A word this does not know
    /// reads as an ordinary monster rather than as the band's top: an unrecognised rank drawn as a
    /// boss would put a boss health bar on something that is not one, which is the failure this
    /// whole reading exists to remove.
    /// </remarks>
    private static byte RankOf(string word) =>
        word.Trim().Trim('`', '"').Trim().ToLowerInvariant() switch
        {
            "[normal]" or "normal" => 0,
            "[champion]" or "champion" => 1,
            "[super champion]" or "super champion" => 2,
            "[boss]" or "boss" => HighestMonsterActorType,
            _ => 0,
        };

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
