using Dfo.Pvf.Schema;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>
/// Projects the archive's dungeon list and dungeon files into <see cref="DungeonDefinition"/>.
/// </summary>
/// <remarks>
/// A dungeon file is a flat sequence of tags. <c>[maze info]</c> opens a maze and is not closed by
/// a matching tag in the observed files -- the tutorial's ends where <c>[special passive object
/// item]</c> begins -- so a maze is closed by the next <c>[maze info]</c>, by an explicit
/// <c>[/maze info]</c> where one exists, or by the end of the file. Reading it as a properly
/// bracketed block would silently drop every maze in a file that never closes one.
/// <para>
/// Inside a maze, <c>[size]</c> gives the grid, <c>[map specification]</c> lists the rooms as
/// repeated <c>"map" x y mapId</c>, and <c>[start map]</c> and <c>[boss map]</c> list candidate
/// cells as bare x/y pairs -- candidates rather than single cells, which is why choosing among
/// them belongs to a rule and not here.
/// </para>
/// <para>
/// <c>[gold card use]</c> sits at file scope rather than inside a maze -- <c>Grimseeker.dgn</c>
/// writes it between <c>[revision table]</c> and <c>[recommend equipment]</c> -- and takes one
/// integer. What that integer means is argued on
/// <see cref="DungeonDefinition.OffersPaidCard"/>; nothing is decided here beyond reading it.
/// </para>
/// </remarks>
internal sealed class PvfDungeonDefinitionSource : IDungeonDefinitionSource
{
    private static class Fields
    {
        internal static readonly PvfField<IReadOnlyList<PvfToken>> MazeInfoField = PvfField.Tokens("[maze info]", optional: true);
        internal static readonly PvfField<bool> CloseMazeInfoField = PvfField.Marker("[/maze info]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> SizeField = PvfField.Int32List("[size]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> MapSpecificationField = PvfField.Tokens("[map specification]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> StartMapField = PvfField.Int32List("[start map]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> BossMapField = PvfField.Int32List("[boss map]", optional: true);
        internal static readonly PvfField<int> MinimumRequiredLevelField = PvfField.Int32("[minimum required level]", optional: true);
        internal static readonly PvfField<int> BasisLevelField = PvfField.Int32("[basis level]", optional: true);
        internal static readonly PvfField<double> ExperienceIncreasingPointField = PvfField.Number("[experience increasing point]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> NoFatigueField = PvfField.Tokens("[no fatigue]", optional: true);
        internal static readonly PvfField<int> GoldCardUseField = PvfField.Int32("[gold card use]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> QuestConnectionField = PvfField.Int32List("[quest connection]", optional: true);
    }

    private const string ListPath = "dungeon/dungeon.lst";
    private const string DungeonDirectory = "dungeon";
    private const string RoomKeyword = "map";
    private const string BossRoomKeyword = "boss";

    private readonly PvfArchiveProvider _provider;
    private readonly Lock _gate = new();
    private readonly Dictionary<int, DungeonDefinition?> _cache = [];
    private IReadOnlyDictionary<int, string>? _paths;

    public PvfDungeonDefinitionSource(PvfArchiveProvider provider) => _provider = provider;

    public ValueTask<DungeonDefinition?> GetAsync(int dungeonId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_cache.TryGetValue(dungeonId, out var cached))
                return ValueTask.FromResult(cached);

            var definition = Read(_provider.Archive, Paths(), dungeonId);
            _cache[dungeonId] = definition;
            return ValueTask.FromResult(definition);
        }
    }

    private IReadOnlyDictionary<int, string> Paths()
    {
        if (_paths is not null)
            return _paths;

        var paths = new Dictionary<int, string>();
        foreach (var (dungeonId, relativePath) in PvfListFile.Read(_provider.Archive, ListPath))
            paths.TryAdd(dungeonId, relativePath);
        if (paths.Count == 0)
            throw new InvalidOperationException($"{ListPath} resolved no dungeon files.");
        return _paths = paths;
    }

    private static DungeonDefinition? Read(
        PvfArchive archive,
        IReadOnlyDictionary<int, string> paths,
        int dungeonId)
    {
        if (!paths.TryGetValue(dungeonId, out var relativePath))
            return null;

        var archivePath = $"{DungeonDirectory}/{relativePath}";
        var fileIndex = archive.FindFile(archivePath);
        if (fileIndex < 0)
            return null;

        var document = PvfDocument.Open(archive, archivePath);
        var mazes = new List<MazeDefinition>();
        MazeDraft? draft = null;
        var seen = new HashSet<PvfField>();
        byte minimumLevel = 1;
        byte basisLevel = 1;
        var experienceWeight = 1.0;
        var consumesFatigue = true;
        var offersPaidCard = true;
        DungeonQuestConnection? questConnection = null;

        void Unique(PvfField field)
        {
            var scope = draft?.Seen ?? seen;
            Require(scope.Add(field), archivePath, field, "duplicate field in the same scope");
        }
        foreach (var section in document.Sections)
        {
            if (Fields.MazeInfoField.Matches(section.Name))
            {
                Require(section.Values.Count == 0, archivePath, Fields.MazeInfoField, "expected an empty maze marker");
                Close(mazes, draft);
                draft = new MazeDraft(mazes.Count);
            }
            else if (Fields.CloseMazeInfoField.Matches(section.Name))
            {
                Fields.CloseMazeInfoField.Decode(archive, section.Values, archivePath);
                Close(mazes, draft);
                draft = null;
            }
            else if (Fields.MinimumRequiredLevelField.Matches(section.Name))
            {
                // Real archives can restate this file-level value (Counterattackarea:
                // 1, then 46). Every occurrence must decode completely; the last valid
                // declaration retains the existing business precedence.
                var value = Fields.MinimumRequiredLevelField.Decode(archive, section.Values, archivePath);
                Require(value is >= 0 and <= byte.MaxValue, archivePath, Fields.MinimumRequiredLevelField, "level is outside byte range");
                minimumLevel = (byte)value;
            }
            else if (Fields.BasisLevelField.Matches(section.Name))
            {
                Unique(Fields.BasisLevelField);
                var value = Fields.BasisLevelField.Decode(archive, section.Values, archivePath);
                Require(value is >= 0 and <= byte.MaxValue, archivePath, Fields.BasisLevelField, "level is outside byte range");
                basisLevel = (byte)Math.Max(1, value);
            }
            else if (Fields.ExperienceIncreasingPointField.Matches(section.Name))
            {
                Unique(Fields.ExperienceIncreasingPointField);
                var value = Fields.ExperienceIncreasingPointField.Decode(archive, section.Values, archivePath);
                Require(value is >= 0 and <= 1000, archivePath, Fields.ExperienceIncreasingPointField, "weight is outside [0, 1000]");
                experienceWeight = value;
            }
            else if (Fields.GoldCardUseField.Matches(section.Name))
            {
                Unique(Fields.GoldCardUseField);
                offersPaidCard = Fields.GoldCardUseField.Decode(archive, section.Values, archivePath) != 0;
            }
            else if (Fields.NoFatigueField.Matches(section.Name))
            {
                Unique(Fields.NoFatigueField);
                Require(section.Values.Count == 0, archivePath, Fields.NoFatigueField, "expected an empty presence flag");
                consumesFatigue = false;
            }
            else if (Fields.QuestConnectionField.Matches(section.Name))
            {
                Unique(Fields.QuestConnectionField);
                var values = Fields.QuestConnectionField.Decode(archive, section.Values, archivePath);
                var connection = ProjectQuestConnection(values);
                Require(connection is not null, archivePath, Fields.QuestConnectionField, "expected state, positive quest id and optional difficulty");
                if (draft is null) questConnection = connection;
                else draft.QuestConnection = connection;
            }
            else if (Fields.SizeField.Matches(section.Name))
            {
                Unique(Fields.SizeField);
                var values = Fields.SizeField.Decode(archive, section.Values, archivePath);
                Require(values.Count == 2, archivePath, Fields.SizeField, "expected width and height");
                var width = CellCoordinate(values[0], archivePath, Fields.SizeField);
                var height = CellCoordinate(values[1], archivePath, Fields.SizeField);
                if (draft is not null) { draft.Width = width; draft.Height = height; }
            }
            else if (Fields.MapSpecificationField.Matches(section.Name))
            {
                Unique(Fields.MapSpecificationField);
                var values = section.Values;
                var offset = 0;
                while (offset < values.Count)
                {
                    Require(values[offset].IsString, archivePath, Fields.MapSpecificationField, "expected a room kind");
                    var kind = archive.ResolveString(values[offset++].Value);
                    var firstOperand = offset;
                    while (offset < values.Count && !values[offset].IsString)
                    {
                        Require(values[offset].Kind == PvfTokenKind.Int32, archivePath,
                            Fields.MapSpecificationField, "expected Int32 room coordinates and map candidates");
                        offset++;
                    }
                    var operandCount = offset - firstOperand;
                    Require(operandCount >= 3, archivePath, Fields.MapSpecificationField,
                        "expected two cell coordinates and at least one map candidate");
                    CellCoordinate(values[firstOperand].Value, archivePath, Fields.MapSpecificationField);
                    CellCoordinate(values[firstOperand + 1].Value, archivePath, Fields.MapSpecificationField);
                    for (var candidate = firstOperand + 2; candidate < offset; candidate++)
                        Require(values[candidate].Value > 0, archivePath, Fields.MapSpecificationField, "expected a positive map candidate id");
                    if (draft is null || !(string.Equals(kind, RoomKeyword, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(kind, BossRoomKeyword, StringComparison.OrdinalIgnoreCase))) continue;

                    // The archive permits multiple map candidates after a cell, while the
                    // existing deterministic business projection consumes complete integer
                    // triples and ignores one or two remaining valid candidates. Retain that
                    // projection here; validation above covers the entire declared entry.
                    for (var row = firstOperand; row + 2 < offset; row += 3)
                        draft.Rooms[new MazeCell(LegacyCoordinate(values[row].Value), LegacyCoordinate(values[row + 1].Value))]
                            = values[row + 2].Value;
                }
            }

            else if (Fields.StartMapField.Matches(section.Name) || Fields.BossMapField.Matches(section.Name))
            {
                var field = Fields.StartMapField.Matches(section.Name) ? Fields.StartMapField : Fields.BossMapField;
                Unique(field);
                var values = field.Decode(archive, section.Values, archivePath);
                Require(values.Count % 2 == 0, archivePath, field, "truncated cell pair");
                for (var offset = 0; offset < values.Count; offset += 2)
                {
                    var cell = new MazeCell(CellCoordinate(values[offset], archivePath, field), CellCoordinate(values[offset + 1], archivePath, field));
                    if (draft is not null) (field == Fields.StartMapField ? draft.StartCandidates : draft.BossCandidates).Add(cell);
                }
            }
        }
        Close(mazes, draft);
        return mazes.Count == 0 ? null : new DungeonDefinition(
            dungeonId, archivePath, minimumLevel, consumesFatigue, mazes, basisLevel,
            experienceWeight, offersPaidCard, questConnection);
    }

    private static byte LegacyCoordinate(int value) =>
        value is >= 0 and <= byte.MaxValue ? (byte)value : (byte)0;

    private static byte CellCoordinate(int value, string path, PvfField field)
    {
        Require(value is >= 0 and <= byte.MaxValue, path, field, "coordinate is outside byte range");
        return (byte)value;
    }

    private static void Require(bool valid, string path, PvfField field, string reason)
    {
        if (!valid) throw new InvalidDataException($"PVF '{path}', field '{field}': {reason}.");
    }

    private static void Close(List<MazeDefinition> mazes, MazeDraft? draft)
    {
        if (draft is null || draft.Rooms.Count == 0) return;
        mazes.Add(new MazeDefinition(draft.Index, draft.Width, draft.Height, draft.Rooms,
            draft.StartCandidates, draft.BossCandidates, draft.QuestConnection));
    }

    internal static DungeonQuestConnection? ProjectQuestConnection(IReadOnlyList<int> values)
    {
        if (values.Count is not (2 or 3) || values[1] <= 0 || values[0] is < 0 or > 1)
            return null;

        return new DungeonQuestConnection(
            (DungeonQuestConnectionState)values[0],
            values[1],
            values.Count == 3 ? values[2] : -1);
    }

    private sealed class MazeDraft(int index)
    {
        public int Index { get; } = index;

        public HashSet<PvfField> Seen { get; } = [];

        public byte Width { get; set; }

        public byte Height { get; set; }

        public Dictionary<MazeCell, int> Rooms { get; } = [];

        public List<MazeCell> StartCandidates { get; } = [];

        public List<MazeCell> BossCandidates { get; } = [];

        public DungeonQuestConnection? QuestConnection { get; set; }
    }

}
