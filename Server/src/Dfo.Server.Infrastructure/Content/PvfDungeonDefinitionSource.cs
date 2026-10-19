using System.Collections.Immutable;
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
/// Inside a maze, <c>[size]</c> gives the grid, <c>[map specification]</c> lists cell candidates as
/// repeated <c>"map" x y mapId...</c> or <c>"boss" x y mapId...</c>, and <c>[start map]</c> and <c>[boss map]</c> list candidate
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
        internal static readonly PvfField<string> GreedField = PvfField.String("[greed]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> MapSpecificationField = PvfField.Tokens("[map specification]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> StartMapField = PvfField.Int32List("[start map]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> BossMapField = PvfField.Int32List("[boss map]", optional: true);
        internal static readonly PvfField<int> MinimumRequiredLevelField = PvfField.Int32("[minimum required level]", optional: true);
        internal static readonly PvfField<int> BasisLevelField = PvfField.Int32("[basis level]", optional: true);
        internal static readonly PvfField<double> ExperienceIncreasingPointField = PvfField.Number("[experience increasing point]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> NoFatigueField = PvfField.Tokens("[no fatigue]", optional: true);
        internal static readonly PvfField<int> GoldCardUseField = PvfField.Int32("[gold card use]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> QuestConnectionField = PvfField.Int32List("[quest connection]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> DifficultyLevelField = PvfField.Int32List("[difficulty level]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> DesignatedDifficultyField = PvfField.Int32List("[designate dungeon difficulty]", optional: true);
        internal static readonly PvfField<int> DifficultyField = PvfField.Int32("[difficulty]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> StoryModeField = PvfField.Tokens("[story mode]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> RequiredItemsField = PvfField.Int32List("[required item]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> EventRequiredItemsField = PvfField.Int32List("[event required item]", optional: true);
        internal static readonly PvfField<bool> SpiciesField = PvfField.Marker("[spicies dungeon]", optional: true);
        internal static readonly PvfField<int> PartyLimitField = PvfField.Int32("[limit party count]", optional: true);
        internal static readonly PvfField<int> ProhibitPracticeField = PvfField.Int32("[prohibit practice]", optional: true);
        internal static readonly PvfField<int> TournamentField = PvfField.Int32("[tournament dungeon]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> NamedMonsterField = PvfField.Int32List("[named monster]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> RevengeField = PvfField.Int32List("[village attack revenge dungeon]", optional: true);
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
        var noGiveupPenalty = false;
        var offersPaidCard = true;
        DungeonQuestConnection? questConnection = null;
        IReadOnlyList<byte>? difficultyLevels = null;
        IReadOnlyList<byte>? designatedDifficulties = null;
        int? fixedDifficulty = null;
        var entryMode = DungeonEntryMode.Maze;
        IReadOnlyList<DungeonRequiredItem> requiredItems = [];
        IReadOnlyList<DungeonRequiredItem> eventRequiredItems = [];
        var isSpicies = false;
        int? partyLimit = null;
        var prohibitsPractice = false;
        var hasStoryMode = false;
        var namedMonsterCodes = ImmutableHashSet.CreateBuilder<int>();

        void Unique(PvfField field)
        {
            var scope = draft?.Seen ?? seen;
            Require(scope.Add(field), archivePath, field, "duplicate field in the same scope");
        }
        foreach (var section in document.Sections)
        {
            if (Fields.NamedMonsterField.Matches(section.Name))
            {
                foreach (var code in Fields.NamedMonsterField.Decode(archive, section.Values, archivePath))
                {
                    Require(code > 0, archivePath, Fields.NamedMonsterField, "monster code must be positive");
                    namedMonsterCodes.Add(code);
                }
                continue;
            }
            if (Fields.MazeInfoField.Matches(section.Name))
            {
                Require(section.Values.Count == 0, archivePath, Fields.MazeInfoField, "expected an empty maze marker");
                Close(mazes, draft, archivePath);
                draft = new MazeDraft(mazes.Count);
            }
            else if (Fields.CloseMazeInfoField.Matches(section.Name))
            {
                Fields.CloseMazeInfoField.Decode(archive, section.Values, archivePath);
                Close(mazes, draft, archivePath);
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
            else if (Fields.DifficultyLevelField.Matches(section.Name))
            {
                Unique(Fields.DifficultyLevelField);
                var values = Fields.DifficultyLevelField.Decode(archive, section.Values, archivePath);
                Require(values.Count <= 5, archivePath, Fields.DifficultyLevelField, "expected at most five A21 difficulty flags");
                difficultyLevels = values.Select((value, index) => (value, index))
                    .Where(pair => pair.value != 0).Select(pair => (byte)pair.index).ToArray();
            }
            else if (Fields.DesignatedDifficultyField.Matches(section.Name))
            {
                Unique(Fields.DesignatedDifficultyField);
                var values = Fields.DesignatedDifficultyField.Decode(archive, section.Values, archivePath);
                Require(values.All(value => value is >= 0 and <= 4), archivePath, Fields.DesignatedDifficultyField, "difficulty outside A21 range");
                designatedDifficulties = values.Select(value => (byte)value).Distinct().Order().ToArray();
            }
            else if (Fields.DifficultyField.Matches(section.Name))
            {
                Unique(Fields.DifficultyField);
                fixedDifficulty = Fields.DifficultyField.Decode(archive, section.Values, archivePath);
                Require(fixedDifficulty is >= -1 and <= 4, archivePath, Fields.DifficultyField, "difficulty outside A21 range");
            }
            else if (Fields.StoryModeField.Matches(section.Name))
            {
                hasStoryMode = true;
            }
            else if (Fields.RequiredItemsField.Matches(section.Name) || Fields.EventRequiredItemsField.Matches(section.Name))
            {
                var field = Fields.RequiredItemsField.Matches(section.Name) ? Fields.RequiredItemsField : Fields.EventRequiredItemsField;
                Unique(field);
                var values = field.Decode(archive, section.Values, archivePath);
                Require(values.Count % 3 == 0, archivePath, field, "truncated item/count/consume triple");
                var items = new List<DungeonRequiredItem>();
                for (var i = 0; i < values.Count; i += 3)
                {
                    Require(values[i] > 0 && values[i + 1] > 0 && values[i + 2] is 0 or 1,
                        archivePath, field, "invalid item/count/consume triple");
                    items.Add(new(values[i], values[i + 1], values[i + 2] == 1));
                }
                if (field == Fields.RequiredItemsField) requiredItems = items.AsReadOnly();
                else eventRequiredItems = items.AsReadOnly();
            }
            else if (Fields.SpiciesField.Matches(section.Name))
            {
                Unique(Fields.SpiciesField);
                isSpicies = Fields.SpiciesField.Decode(archive, section.Values, archivePath);
                // Explicit server policy: this activity family is exempt even without [no fatigue].
                consumesFatigue = false;
            }
            else if (Fields.PartyLimitField.Matches(section.Name))
            {
                Unique(Fields.PartyLimitField);
                partyLimit = Fields.PartyLimitField.Decode(archive, section.Values, archivePath);
                Require(partyLimit is >= 1 and <= 4, archivePath, Fields.PartyLimitField, "party limit outside [1,4]");
            }
            else if (Fields.ProhibitPracticeField.Matches(section.Name))
            {
                // Tau Kingdom declares 0 near the header and 1 later in the same DGN.
                // Preserve valid existing content; the final declaration is authoritative here.
                var value = Fields.ProhibitPracticeField.Decode(archive, section.Values, archivePath);
                Require(value is 0 or 1, archivePath, Fields.ProhibitPracticeField, "expected boolean");
                prohibitsPractice = value != 0;
            }
            else if (Fields.TournamentField.Matches(section.Name))
            {
                if (Fields.TournamentField.Decode(archive, section.Values, archivePath) != 0)
                    entryMode = DungeonEntryMode.Tournament;
            }
            else if (Fields.RevengeField.Matches(section.Name))
            {
                Fields.RevengeField.Decode(archive, section.Values, archivePath);
                entryMode = DungeonEntryMode.VillageRevenge;
            }
            else if (section.Name == "[no giveup panalty]")
            {
                if (noGiveupPenalty || section.Values.Count != 0)
                    throw new InvalidDataException($"PVF '{archivePath}': invalid [no giveup panalty] marker.");
                noGiveupPenalty = true;
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
            else if (Fields.GreedField.Matches(section.Name))
            {
                Unique(Fields.GreedField);
                var values = PvfDungeonConnections.Decode(
                    Fields.GreedField.Decode(archive, section.Values, archivePath), archivePath);
                if (draft is not null) draft.Patterns = values;
            }
            else if (Fields.MapSpecificationField.Matches(section.Name))
            {
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

                    var cell = new MazeCell((byte)values[firstOperand].Value, (byte)values[firstOperand + 1].Value);
                    var declarations = string.Equals(kind, BossRoomKeyword, StringComparison.OrdinalIgnoreCase)
                        ? draft.BossMaps : draft.Maps;
                    // Multiple declarations at one coordinate append resource candidates. They
                    // remain scoped to this maze and kind, including across repeated sections.
                    var existing = declarations.GetValueOrDefault(cell) ?? [];
                    declarations[cell] = Array.AsReadOnly(existing.Concat(values.Skip(firstOperand + 2)
                        .Take(offset - firstOperand - 2).Select(value => value.Value)).Distinct().ToArray());
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
        Close(mazes, draft, archivePath);
        return mazes.Count == 0 ? null : new DungeonDefinition(
            dungeonId, archivePath, minimumLevel, consumesFatigue, mazes, basisLevel,
            experienceWeight, offersPaidCard, questConnection,
            designatedDifficulties ?? difficultyLevels ?? (fixedDifficulty is >= 0 ? [(byte)fixedDifficulty.Value] : []), entryMode)
        { RecommendedLevels = document.Read(PvfField.Int32List("[recommended level]", optional: true)) ?? [], RequiredItems = requiredItems,
            EventRequiredItems = eventRequiredItems, IsSpiciesDungeon = isSpicies, PartyLimit = partyLimit, ProhibitsPractice = prohibitsPractice,
            HasStoryMode = hasStoryMode, NoGiveupPenalty = noGiveupPenalty, NamedMonsterCodes = namedMonsterCodes.ToImmutable() };
    }

    private static byte CellCoordinate(int value, string path, PvfField field)
    {
        Require(value is >= 0 and <= byte.MaxValue, path, field, "coordinate is outside byte range");
        return (byte)value;
    }

    private static void Require(bool valid, string path, PvfField field, string reason)
    {
        if (!valid) throw new InvalidDataException($"PVF '{path}', field '{field}': {reason}.");
    }

    private static void Close(List<MazeDefinition> mazes, MazeDraft? draft, string path)
    {
        if (draft is null) return;
        var cells = new Dictionary<MazeCell, DungeonCellDefinition>();
        var configurationErrors = new List<string>();
        if (draft.Width == 0 || draft.Height == 0)
            configurationErrors.Add("maze size must have positive width and height");
        if (draft.Patterns is not null)
        {
            if (draft.Patterns.Count != draft.Width * draft.Height)
                configurationErrors.Add("connection grid does not match maze size");
            for (var index = 0; draft.Width > 0 && index < draft.Patterns.Count; index++)
            {
                var pattern = draft.Patterns[index];
                if (pattern == DungeonPathDirection.None) continue;
                // A malformed oversized grid is retained as a maze diagnostic, not allowed
                // to wrap byte coordinates and throw while another maze is being loaded.
                if (index / draft.Width > byte.MaxValue) continue;
                var cell = new MazeCell((byte)(index % draft.Width), (byte)(index / draft.Width));
                cells.Add(cell, new(pattern, draft.Maps.GetValueOrDefault(cell) ?? [],
                    draft.BossMaps.GetValueOrDefault(cell) ?? []));
            }
        }
        else
        {
            // Explicit-only declarations carry no door restrictions. Keep the absence distinct
            // from an AA cell in a declared grid, which is unoccupied.
            foreach (var cell in draft.Maps.Keys.Concat(draft.BossMaps.Keys).Distinct())
                cells.Add(cell, new(DungeonPathDirection.All, draft.Maps.GetValueOrDefault(cell) ?? [],
                    draft.BossMaps.GetValueOrDefault(cell) ?? []) { HasDeclaredConnections = false });
        }
        foreach (var cell in draft.Maps.Keys.Concat(draft.BossMaps.Keys).Distinct())
        {
            if (!cells.ContainsKey(cell) || cell.X >= draft.Width || cell.Y >= draft.Height)
                configurationErrors.Add($"map ({cell.X},{cell.Y}) declared outside occupied maze cells");
            // Preserve explicit declarations even when they conflict with the grid. Only
            // selecting this maze should refuse entry, while diagnostics can still name it.
            cells.TryAdd(cell, new(DungeonPathDirection.None, draft.Maps.GetValueOrDefault(cell) ?? [],
                draft.BossMaps.GetValueOrDefault(cell) ?? []));
        }
        if (cells.Count == 0)
            configurationErrors.Add("maze contains no occupied cells");
        mazes.Add(new MazeDefinition(draft.Index, draft.Width, draft.Height,
            new System.Collections.ObjectModel.ReadOnlyDictionary<MazeCell, DungeonCellDefinition>(cells),
            draft.StartCandidates.AsReadOnly(), draft.BossCandidates.AsReadOnly(), draft.QuestConnection) {
            ConfigurationError = configurationErrors.Count == 0 ? null
                : $"PVF '{path}', maze '{draft.Index}': {string.Join("; ", configurationErrors)}.",
        });
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

        public IReadOnlyList<DungeonPathDirection>? Patterns { get; set; }
        public Dictionary<MazeCell, IReadOnlyList<int>> Maps { get; } = [];
        public Dictionary<MazeCell, IReadOnlyList<int>> BossMaps { get; } = [];

        public List<MazeCell> StartCandidates { get; } = [];

        public List<MazeCell> BossCandidates { get; } = [];

        public DungeonQuestConnection? QuestConnection { get; set; }
    }

}
