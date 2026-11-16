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
internal sealed class PvfDungeonDefinitionSource : IDungeonDefinitionSource, IDungeonAdmissionDefinitionSource, Dfo.Server.Abstractions.Content.IContentWarmup
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
        internal static readonly PvfField<int> EntryOnlyFatigueField = PvfField.Int32("[use fatigue only start dungeon]", optional: true);
        internal static readonly PvfField<int> TournamentField = PvfField.Int32("[tournament dungeon]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> NamedMonsterField = PvfField.Int32List("[named monster]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> RevengeField = PvfField.Int32List("[village attack revenge dungeon]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> ChampionField = PvfField.Int32List("[champion]", optional: true);
    }

    private const string ListPath = "dungeon/dungeon.lst";
    private const string DungeonDirectory = "dungeon";
    private const string RoomKeyword = "map";
    private const string BossRoomKeyword = "boss";

    private readonly PvfArchiveProvider _provider;
    private readonly Lock _gate = new();
    private readonly Dictionary<int, DungeonDefinition?> _cache = [];
    private readonly Dictionary<int, DungeonAdmissionDefinition?> _admissions = [];
    private IReadOnlyDictionary<int, string>? _paths;

    public PvfDungeonDefinitionSource(PvfArchiveProvider provider) => _provider = provider;

    public string Name => "dungeon-index";

    public ValueTask<int> WarmAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) return ValueTask.FromResult(Paths().Count);
    }

    public ValueTask<DungeonDefinition?> GetAsync(int dungeonId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_cache.TryGetValue(dungeonId, out var cached))
                return ValueTask.FromResult(cached);

            var definition = Read(_provider.Archive, Paths(), dungeonId, out var admission);
            _admissions[dungeonId] = admission;
            _cache[dungeonId] = definition;
            return ValueTask.FromResult(definition);
        }
    }

    public ValueTask<DungeonAdmissionDefinition?> GetAdmissionAsync(int dungeonId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_admissions.ContainsKey(dungeonId)) _ = GetAsync(dungeonId, ct);
            return ValueTask.FromResult(_admissions[dungeonId]);
        }
    }

    private IReadOnlyDictionary<int, string> Paths()
    {
        if (_paths is not null)
            return _paths;

        var paths = PvfListFile.Paths(_provider.Archive, ListPath, Dfo.Pvf.PvfListDuplicates.First);
        if (paths.Count == 0)
            throw new InvalidOperationException($"{ListPath} resolved no dungeon files.");
        return _paths = paths;
    }

    private static DungeonDefinition? Read(
        PvfArchive archive,
        IReadOnlyDictionary<int, string> paths,
        int dungeonId, out DungeonAdmissionDefinition? admission)
    {
        admission = null;
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
        ushort? entryOnlyFatigue = null;
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
        IReadOnlyList<int> championRates = [];
        var ignoresDefaultClear = false;
        List<DungeonClearCondition>? clearBlock = null;
        var inTeamClearBlock = false;
        DungeonBossRoomEntranceCondition? bossRoomEntrance = null;

        void Unique(PvfField field)
        {
            var scope = draft?.Seen ?? seen;
            Require(scope.Add(field), archivePath, field, "duplicate field in the same scope");
        }
        foreach (var section in document.Sections)
        {
            if (section.Name == BossRoomEntranceStart)
            {
                Require(bossRoomEntrance is null, archivePath, BossRoomEntranceStart, "declared twice");
                bossRoomEntrance = ParseBossRoomEntrance(archive, section, archivePath);
                continue;
            }
            // [team clear condition] belongs to territory combat, which this server does not run;
            // its sub-tags are skipped whole so they are not mistaken for this maze's conditions.
            if (inTeamClearBlock)
            {
                inTeamClearBlock = section.Name != TeamClearConditionEnd;
                continue;
            }
            if (section.Name == TeamClearConditionStart)
            {
                inTeamClearBlock = true;
                continue;
            }
            if (clearBlock is not null)
            {
                if (section.Name == ClearConditionEnd)
                {
                    // A21 client 0x02AA0770 keeps one entry per block (a later sub-tag overwrites
                    // an earlier one); every A21 block declares exactly one, and anything else is
                    // refused rather than silently reduced to its last entry.
                    Require(clearBlock.Count == 1, archivePath, ClearConditionStart, "expected exactly one entry per block");
                    draft!.ClearConditions.AddRange(clearBlock);
                    clearBlock = null;
                }
                else
                    clearBlock.Add(ParseClearCondition(archive, section, archivePath));
                continue;
            }
            if (section.Name == ClearConditionStart)
            {
                Require(draft is not null && section.Values.Count == 0, archivePath, ClearConditionStart,
                    "expected an empty block opener inside a maze");
                clearBlock = [];
                continue;
            }
            if (section.Name == IgnoreDefaultClear)
            {
                Require(section.Values.Count == 0, archivePath, IgnoreDefaultClear, "expected a bare marker");
                ignoresDefaultClear = true;
                continue;
            }
            if (Fields.EntryOnlyFatigueField.Matches(section.Name))
            {
                Unique(Fields.EntryOnlyFatigueField);
                var value = Fields.EntryOnlyFatigueField.Decode(archive, section.Values, archivePath);
                Require(value is > 0 and <= ushort.MaxValue, archivePath, Fields.EntryOnlyFatigueField, "expected positive UInt16 fatigue");
                entryOnlyFatigue = (ushort)value;
                continue;
            }
            if (Fields.ChampionField.Matches(section.Name))
            {
                // 70 ImportDungeonScript @ 0x088E7BD8 stores five per-difficulty ints at
                // STDungeonScript+0xB4; CDungeon::set_dungeon copies them for SelectDungeon.
                Unique(Fields.ChampionField);
                var values = Fields.ChampionField.Decode(archive, section.Values, archivePath);
                Require(values.Count <= 5 && values.All(value => value >= 0), archivePath, Fields.ChampionField,
                    "expected at most five non-negative per-difficulty rates");
                championRates = values;
                continue;
            }
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
                var values = field.Decode(archive, section.Values, archivePath);
                Require(values.Count % 3 == 0, archivePath, field, "truncated item/count/consume triple");
                var items = new List<DungeonRequiredItem>();
                for (var i = 0; i < values.Count; i += 3)
                {
                    Require(values[i] > 0 && values[i + 1] > 0 && values[i + 2] is 0 or 1,
                        archivePath, field, "invalid item/count/consume triple");
                    items.Add(new(values[i], values[i + 1], values[i + 2] == 1));
                }
                // A21 despair floor 1 repeats the identical event ticket declaration. It is one cost,
                // not two; conflicting or malformed restatements remain invalid.
                if (field == Fields.EventRequiredItemsField && (draft?.Seen ?? seen).Contains(field))
                    Require(eventRequiredItems.SequenceEqual(items), archivePath, field, "conflicting repeated item requirements");
                else Unique(field);
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
        Require(clearBlock is null && !inTeamClearBlock, archivePath, ClearConditionStart, "unterminated block");

        Close(mazes, draft, archivePath);
        admission = new(dungeonId, archivePath, minimumLevel, consumesFatigue,
            designatedDifficulties ?? difficultyLevels ?? (fixedDifficulty is >= 0 ? [(byte)fixedDifficulty.Value] : []),
            hasStoryMode, requiredItems, eventRequiredItems, prohibitsPractice, partyLimit);
        return mazes.Count == 0 ? null : new DungeonDefinition(
            dungeonId, archivePath, minimumLevel, consumesFatigue, mazes, basisLevel,
            experienceWeight, offersPaidCard, questConnection,
            designatedDifficulties ?? difficultyLevels ?? (fixedDifficulty is >= 0 ? [(byte)fixedDifficulty.Value] : []), entryMode)
        { RecommendedLevels = document.Read(PvfField.Int32List("[recommended level]", optional: true)) ?? [], RequiredItems = requiredItems,
            EventRequiredItems = eventRequiredItems, IsSpiciesDungeon = isSpicies, PartyLimit = partyLimit, ProhibitsPractice = prohibitsPractice,
            HasStoryMode = hasStoryMode, NoGiveupPenalty = noGiveupPenalty, NamedMonsterCodes = namedMonsterCodes.ToImmutable(),
            LinkedDungeon = PvfLinkedDungeonParser.Parse(document.Sections, archivePath), EntryOnlyFatigue = entryOnlyFatigue,
            ChampionRates = championRates, IgnoresDefaultClear = ignoresDefaultClear,
            BossRoomEntrance = bossRoomEntrance };
    }

    private const string ClearConditionStart = "[clear condition]";
    private const string ClearConditionEnd = "[/clear condition]";
    private const string TeamClearConditionStart = "[team clear condition]";
    private const string TeamClearConditionEnd = "[/team clear condition]";
    private const string IgnoreDefaultClear = "[ignore default dungeon clear]";
    private const string BossRoomEntranceStart = "[boss room entrance condition]";

    /// <summary>
    /// One <c>[boss room entrance condition]</c>. Its words are tokens inside the one section, read
    /// in order: <c>[no check enter boss room]</c>, <c>[clear action]</c> followed by its action
    /// (<c>[summon monster] N code</c>, or another action such as 2005's <c>[time condition] a b</c>,
    /// kept unexecuted), and <c>[hunt monster] N</c> with N <c>code a b</c> triples.
    /// </summary>
    internal static DungeonBossRoomEntranceCondition ParseBossRoomEntrance(PvfArchive archive, PvfSection section, string path)
    {
        var values = section.Values;
        var at = 0;
        var skipsBossRoomCheck = false;
        var summonCode = 0;
        var summonCount = 0;
        string? unsupported = null;
        var hunts = new List<DungeonEntranceHuntTarget>();
        int Int()
        {
            Require(at < values.Count && values[at].Kind == PvfTokenKind.Int32, path, BossRoomEntranceStart, "expected an integer");
            return values[at++].Value;
        }
        while (at < values.Count)
        {
            Require(values[at].IsString, path, BossRoomEntranceStart, "expected a condition word");
            var word = archive.ResolveString(values[at++].Value).Trim();
            switch (word)
            {
                case "[no check enter boss room]":
                    skipsBossRoomCheck = true;
                    break;
                case "[clear action]":
                    break;
                case "[summon monster]":
                    summonCount = Int();
                    summonCode = Int();
                    Require(summonCount > 0 && summonCode > 0, path, BossRoomEntranceStart,
                        "expected a positive summon count and monster code");
                    break;
                case "[hunt monster]":
                {
                    var count = Int();
                    Require(count > 0, path, BossRoomEntranceStart, "hunt count must be positive");
                    for (var index = 0; index < count; index++)
                    {
                        var code = Int();
                        Require(code > 0, path, BossRoomEntranceStart, "hunt target code must be positive");
                        hunts.Add(new(code, Int(), Int()));
                    }
                    break;
                }
                default:
                    // An action this server does not run: keep its name, skip its integers.
                    unsupported ??= word;
                    while (at < values.Count && !values[at].IsString) at++;
                    break;
            }
        }
        return new(skipsBossRoomCheck, summonCode, summonCount, hunts.AsReadOnly())
        {
            UnsupportedAction = unsupported ?? (summonCode > 0 ? null : "[clear action]"),
        };
    }

    /// <summary>
    /// One sub-tag of a <c>[clear condition]</c> block: <c>id count</c>, or
    /// <c>list N id1 .. idN count</c> (A21 client 0x02AA0770 reads the "list" word, N, then N ids).
    /// </summary>
    internal static DungeonClearCondition ParseClearCondition(PvfArchive archive, PvfSection section, string path)
    {
        var kind = section.Name switch
        {
            "[destroy object]" => DungeonClearConditionKind.DestroyObject,
            "[clear map]" => DungeonClearConditionKind.ClearMap,
            "[hunt monster]" => DungeonClearConditionKind.HuntMonster,
            "[hunt apc]" => DungeonClearConditionKind.HuntApc,
            "[hunt boss]" => DungeonClearConditionKind.HuntBoss,
            "[limit time]" => DungeonClearConditionKind.LimitTime,
            _ => throw new InvalidDataException($"PVF '{path}', field '{ClearConditionStart}': unknown entry '{section.Name}'."),
        };
        var values = section.Values;
        var at = 0;
        int Int()
        {
            Require(at < values.Count && values[at].Kind == PvfTokenKind.Int32, path, ClearConditionStart,
                $"expected an integer in {section.Name}");
            return values[at++].Value;
        }
        List<int> targets = [];
        if (at < values.Count && values[at].IsString)
        {
            Require(string.Equals(archive.ResolveString(values[at++].Value), "list", StringComparison.OrdinalIgnoreCase),
                path, ClearConditionStart, $"expected 'list' in {section.Name}");
            var length = Int();
            Require(length > 0, path, ClearConditionStart, "list length must be positive");
            for (var index = 0; index < length; index++) targets.Add(Int());
        }
        else
            targets.Add(Int());
        var count = Int();
        Require(at == values.Count, path, ClearConditionStart, $"trailing values in {section.Name}");
        if (kind != DungeonClearConditionKind.LimitTime)
            Require(targets.All(target => target > 0) && count > 0, path, ClearConditionStart,
                $"{section.Name} needs positive targets and count");
        // Both A21 (0x02AA0B37) and 70 force a clear map entry's count to one.
        return new(kind, targets.AsReadOnly(), kind == DungeonClearConditionKind.ClearMap ? 1 : count);
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

    /// <summary>For block tags read by sequence rather than through a typed field.</summary>
    private static void Require(bool valid, string path, string tag, string reason)
    {
        if (!valid) throw new InvalidDataException($"PVF '{path}', field '{tag}': {reason}.");
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
        // A21 haze quest 14825 retains map bindings on two AA cells. Those bindings
        // are unused candidates, not additional rooms or a malformed route.
        foreach (var cell in draft.Maps.Keys.Concat(draft.BossMaps.Keys).Distinct())
        {
            if (cell.X >= draft.Width || cell.Y >= draft.Height)
                configurationErrors.Add($"map ({cell.X},{cell.Y}) declared outside occupied maze cells");
        }
        if (cells.Count == 0)
            configurationErrors.Add("maze contains no occupied cells");
        mazes.Add(new MazeDefinition(draft.Index, draft.Width, draft.Height,
            new System.Collections.ObjectModel.ReadOnlyDictionary<MazeCell, DungeonCellDefinition>(cells),
            draft.StartCandidates.AsReadOnly(), draft.BossCandidates.AsReadOnly(), draft.QuestConnection) {
            ClearConditions = draft.ClearConditions.ToArray(),
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

        public List<DungeonClearCondition> ClearConditions { get; } = [];
    }

}
