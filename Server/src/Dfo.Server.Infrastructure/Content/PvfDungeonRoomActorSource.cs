using Dfo.Pvf.Schema;
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
    private static class Fields
    {
        internal static readonly PvfField<IReadOnlyList<PvfToken>> MonsterField = PvfField.Tokens("[monster]", optional: true);
        internal static readonly PvfField<bool> CloseMonsterField = PvfField.Marker("[/monster]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> AiCharacterField = PvfField.Tokens("[ai character]", optional: true);
        internal static readonly PvfField<bool> CloseAiCharacterField = PvfField.Marker("[/ai character]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> MinimumInfoField = PvfField.Tokens("[minimum info]", optional: true);
    }

    private const string MapListPath = "map/map.lst";
    private const string MapDirectory = "map";
    private const string AiCharacterListPath = "aicharacter/aicharacter.lst";
    private const string AiCharacterDirectory = "aicharacter";

    /// <summary>The actor type an AI character is reported as.</summary>
    /// <remarks>
    /// Five through eight are the AI-character band: add the map's rank (0..3) to its floor.
    /// The same mapping is used by the A21 reference and our illusion-tower content reader.
    /// </remarks>
    private const byte AiCharacterActorType = 5;

    /// <summary>The highest actor type that is still a monster.</summary>
    private const byte HighestMonsterActorType = 3;

    /// <summary>
    /// <c>code, relativeLevelFlag, levelValue, x, y, z, randomDrops, fixedDrops</c>, then two quoted words.
    /// </summary>
    /// <remarks>
    /// The third integer is the map's level value/adjustment, not the actor's rank. Reading it as
    /// the rank made ordinary monsters arrive as bosses: dungeon 144's rooms declare values 3 and
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

        var path = $"{MapDirectory}/{relativePath}";
        var document = PvfDocument.Open(archive, path);
        foreach (var section in document.Sections)
        {
            if (Fields.CloseMonsterField.Matches(section.Name)) Fields.CloseMonsterField.Decode(archive, section.Values, path);
            if (Fields.CloseAiCharacterField.Matches(section.Name)) Fields.CloseAiCharacterField.Decode(archive, section.Values, path);
        }
        var monsters = new List<DungeonRoomActor>();
        foreach (var values in document.ReadAll(Fields.MonsterField))
        {
            var width = MonsterRecordIntegers + MonsterRecordWords;
            Require(values.Count % width == 0, path, Fields.MonsterField, "truncated monster record");
            for (var offset = 0; offset < values.Count; offset += width)
            {
                Require(values.Skip(offset).Take(MonsterRecordIntegers).All(token => token.Kind == PvfTokenKind.Int32)
                    && values.Skip(offset + MonsterRecordIntegers).Take(MonsterRecordWords).All(token => token.Kind == PvfTokenKind.QuotedString),
                    path, Fields.MonsterField, "expected eight Int32 values followed by two quoted words");
                var record = values.Skip(offset).Take(MonsterRecordIntegers).Select(token => token.Value).ToArray();
                Require(record[0] > 0 && record[6] is >= 0 and <= 127, path, Fields.MonsterField, "invalid monster id or random drop count");
                var words = values.Skip(offset + MonsterRecordIntegers).Take(MonsterRecordWords)
                    .Select(token => archive.ResolveString(token.Value)).ToArray();
                Require(words.All(word => !string.IsNullOrWhiteSpace(word)), path, Fields.MonsterField, "missing placement or rank word");
                monsters.Add(Monster(record, words));
            }
        }
        var aiCharacters = new List<DungeonRoomActor>();
        foreach (var values in document.ReadAll(Fields.AiCharacterField))
        {
            var width = AiCharacterRecordIntegers + MonsterRecordWords;
            Require(values.Count % width == 0, path, Fields.AiCharacterField, "truncated AI character record");
            for (var offset = 0; offset < values.Count; offset += width)
            {
                Require(values.Skip(offset).Take(4).All(token => token.Kind == PvfTokenKind.Int32)
                    && values.Skip(offset + 4).Take(2).All(token => token.Kind == PvfTokenKind.QuotedString)
                    && values.Skip(offset + 6).Take(2).All(token => token.Kind == PvfTokenKind.Int32),
                    path, Fields.AiCharacterField, "expected four Int32 values, two quoted words and two Int32 values");
                Require(values[offset].Value > 0, path, Fields.AiCharacterField, "invalid AI character id");
                var faction = archive.ResolveString(values[offset + 4].Value).Trim().ToLowerInvariant();
                Require(faction is "[monster]" or "[character]" or "[neutral]", path,
                    Fields.AiCharacterField, $"unknown APC faction '{faction}'");
                var rank = RankOf(archive.ResolveString(values[offset + 5].Value));
                if (TryAiCharacter(archive, values[offset].Value, out var actor))
                    aiCharacters.Add(actor with
                    {
                        ActorType = checked((byte)(AiCharacterActorType + rank)),
                        IsHostile = faction == "[monster]",
                    });
            }
        }
        return [.. monsters, .. aiCharacters];
    }

    private static void Require(bool valid, string path, PvfField field, string reason)
    {
        if (!valid) throw new InvalidDataException($"PVF '{path}', field '{field}': {reason}.");
    }

    /// <remarks>
    /// The second integer selects relative mode when nonzero; the third then adjusts the
    /// dungeon basis level. Otherwise the third is an absolute level. Keep relative declarations
    /// unresolved because this source knows the map but not which dungeon is using it.
    /// </remarks>
    private static DungeonRoomActor Monster(IReadOnlyList<int> record, IReadOnlyList<string> words)
    {
        var relative = record[1] != 0;
        var level = !relative && record[2] > 0
            ? (byte)Math.Min(record[2], byte.MaxValue)
            : (byte)0;
        var rank = words.Count > MonsterRankWordPosition
            ? RankOf(words[MonsterRankWordPosition])
            : (byte)0;
        return new DungeonRoomActor(
            record[0], level, rank, relative ? record[2] : null,
            ValidateRandomDropCount(record[6]));
    }

    private static int ValidateRandomDropCount(int count) => count is >= 0 and <= 127
        ? count : throw new InvalidDataException($"Invalid map monster random drop count {count}.");

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

        var path = $"{AiCharacterDirectory}/{relativePath}";
        var document = PvfDocument.Open(archive, path);
        if (!document.TryRead(Fields.MinimumInfoField, out var values)) return 0;
        Require(values.Count == 16 && values[0].Kind == PvfTokenKind.QuotedString
            && values.Skip(1).Take(11).All(token => token.Kind == PvfTokenKind.Int32)
            && values[12].Kind == PvfTokenKind.QuotedString
            && values.Skip(13).All(token => token.Kind == PvfTokenKind.Int32),
            path, Fields.MinimumInfoField, "expected name, eleven Int32 values, creature name and three Int32 values");
        var level = values[AiCharacterLevelPosition].Value;
        Require(level is > 0 and <= byte.MaxValue, path, Fields.MinimumInfoField, "level is outside [1, 255]");
        return (byte)level;
    }

}
