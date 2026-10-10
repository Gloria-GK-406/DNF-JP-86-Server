using Dfo.Pvf.Schema;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>Projects the archive's quest scripts into neutral, validated definitions.</summary>
internal sealed class PvfQuestDefinitionSource : IQuestDefinitionSource
{
    private static class Fields
    {
        internal static readonly PvfField<string> NameField = PvfField.String("[name]", optional: true);
        internal static readonly PvfField<string> GradeField = PvfField.String("[grade]", optional: true);
        internal static readonly PvfField<IReadOnlyList<string>> JobField = PvfField.Strings("[job]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> LevelField = PvfField.Int32List("[level]", optional: true);
        internal static readonly PvfField<int> GrowTypeField = PvfField.Int32("[grow type]", optional: true);
        internal static readonly PvfField<int> NpcIndexField = PvfField.Int32("[npc index]", optional: true);
        internal static readonly PvfField<int> CompleteNpcIndexField = PvfField.Int32("[complete npc index]", optional: true);
        internal static readonly PvfField<string> TypeField = PvfField.String("[type]", optional: true);
        internal static readonly PvfField<int> SubTypeField = PvfField.Int32("[sub type]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> IntDataField = PvfField.Int32List("[int data]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> MonsterRewardItemField = PvfField.Tokens("[monster reward item]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> EnemyRewardItemField = PvfField.Tokens("[enemy reward item]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> DependGiveItemField = PvfField.Tokens("[depend give item]", optional: true);
        internal static readonly PvfField<string> DifficultyField = PvfField.String("[difficulty]", optional: true);
        internal static readonly PvfField<string> RewardTypeField = PvfField.String("[reward type]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> RewardIntDataField = PvfField.Tokens("[reward int data]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> RewardSelectionIntDataField = PvfField.Tokens("[reward selection int data]", optional: true);
        internal static readonly PvfField<int> GoldMultipleField = PvfField.Int32("[gold multiple]", optional: true);
        internal static readonly PvfField<int> ExposedByNpcField = PvfField.Int32("[exposed by npc]", optional: true);
        internal static readonly PvfField<int> EventField = PvfField.Int32("[event]", optional: true);
        internal static readonly PvfField<int> CreatureKindField = PvfField.Int32("[creature kind]", optional: true);
        internal static readonly PvfField<int> CreatureLevelField = PvfField.Int32("[creature level]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> ExpertjobLevelField = PvfField.Int32List("[expertjob level]", optional: true);
        internal static readonly PvfField<IReadOnlyList<string>> TargetCharacterField = PvfField.Custom<IReadOnlyList<string>>("[target character]",
            (archive, values, path) =>
            {
                var jobs = new List<string>();
                for (var index = 0; index < values.Count;)
                {
                    if (!values[index].IsString) throw new InvalidDataException($"PVF '{path}', field '[target character]': expected job string.");
                    jobs.Add(archive.ResolveString(values[index++].Value));
                    if (index < values.Count && values[index].Kind == PvfTokenKind.Int32)
                    {
                        if (index + 1 >= values.Count || values[index + 1].Kind != PvfTokenKind.Int32)
                            throw new InvalidDataException($"PVF '{path}', field '[target character]': incomplete growtype/awakening pair.");
                        index += 2;
                    }
                }
                return jobs.AsReadOnly();
            }, optional: true);
        internal static readonly PvfField<int> JobChangeQuestField = PvfField.Int32("[job change quest]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> PreRequiredQuestField = PvfField.Int32List("[pre required quest]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> PreRequiredQuestAnswerField = PvfField.Int32List("[pre required quest answer]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> CollisionQuestField = PvfField.Int32List("[collision quest]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> AccountCollisionQuestField = PvfField.Int32List("[account collision quest]", optional: true);
        internal static readonly PvfField<bool> SpecialRewardStatusField = PvfField.Custom<bool>("[special reward status]", (_, values, path) =>
        {
            if (values.Count == 0 || values.Count % 3 != 0)
                throw new InvalidDataException($"PVF '{path}', field '[special reward status]': incomplete attribute/operator/value triple.");
            for (var i = 0; i < values.Count; i += 3)
                if (!values[i].IsString || !values[i + 1].IsString || values[i + 2].Kind is not (PvfTokenKind.Int32 or PvfTokenKind.Single)
                    || values[i + 2].Kind == PvfTokenKind.Single && !float.IsFinite(values[i + 2].AsSingle()))
                    throw new InvalidDataException($"PVF '{path}', field '[special reward status]': expected attribute/operator/finite-number triples.");
            return true;
        }, optional: true);
        internal static readonly PvfField<int> IgnoreQuestLevel4ExpField = PvfField.Int32("[ignore quest level 4 exp]", optional: true);
    }

    private const string ListPath = "n_quest/quest.lst";
    private const string QuestDirectory = "n_quest";
    private const string TrainingListPath = "n_quest/TrainingQuest.lst";
    private const int MinimumClientQuestId = 1;
    private const int MaximumClientQuestId = 29_999;

    private static readonly Dictionary<string, QuestGrade> GradeTokens =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["[normal]"] = QuestGrade.Normal,
            ["[training]"] = QuestGrade.Training,
            ["[epic]"] = QuestGrade.Epic,
            ["[achievement]"] = QuestGrade.Achievement,
            ["[crack]"] = QuestGrade.Crack,
            ["[challenge]"] = QuestGrade.Challenge,
            ["[common unique]"] = QuestGrade.CommonUnique,
            ["[title]"] = QuestGrade.Title,
            ["[side]"] = QuestGrade.Side,
            ["[daily]"] = QuestGrade.Daily,
            ["[daily random]"] = QuestGrade.DailyRandom,
            ["[special daily]"] = QuestGrade.SpecialDaily,
            ["[normaly repeat]"] = QuestGrade.NormallyRepeat,
            ["[circle]"] = QuestGrade.Circle,
            ["[sub]"] = QuestGrade.Sub,
            ["[system]"] = QuestGrade.System,
            ["[schedule]"] = QuestGrade.Schedule,
            ["[urgent]"] = QuestGrade.Urgent,
            ["[wedding]"] = QuestGrade.Wedding,
            ["[village attack revenge]"] = QuestGrade.VillageAttackRevenge,
        };

    private static readonly Dictionary<string, QuestRewardKind> RewardTokens =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["[item]"] = QuestRewardKind.Item,
            ["[title]"] = QuestRewardKind.Title,
            ["[grow type]"] = QuestRewardKind.GrowType,
            ["[awakening type]"] = QuestRewardKind.AwakeningType,
            ["[creature evolution]"] = QuestRewardKind.CreatureEvolution,
            ["[event creature evolution]"] = QuestRewardKind.EventCreatureEvolution,
            ["[ridable]"] = QuestRewardKind.Ridable,
            ["[expert job]"] = QuestRewardKind.ExpertJob,
            ["[event skill]"] = QuestRewardKind.EventSkill,
            ["[hell challenge]"] = QuestRewardKind.HellChallenge,
            ["[slot expansion]"] = QuestRewardKind.SlotExpansion,
            ["[crack of dimension]"] = QuestRewardKind.CrackOfDimension,
            ["[circle dungeon]"] = QuestRewardKind.CircleDungeon,
        };

    private readonly PvfArchiveProvider _provider;
    private readonly Lock _gate = new();
    private Catalog? _catalog;

    internal sealed record RelationshipProjection(
        IReadOnlyList<IReadOnlyList<int>> CompletedQuestGroups,
        IReadOnlyList<QuestAnswerRequirement> RequiredAnswers,
        IReadOnlyList<int> CollisionQuestIds,
        IReadOnlyList<int> AccountCollisionQuestIds);

    public PvfQuestDefinitionSource(PvfArchiveProvider provider) => _provider = provider;

    public ValueTask<IReadOnlyList<QuestDefinition>> GetAllAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Load().Ordered);
    }

    public ValueTask<QuestDefinition?> GetAsync(int questId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Load().ById.GetValueOrDefault(questId));
    }

    public ValueTask<IReadOnlyList<TrainingQuestAvailability>> GetTrainingAvailabilityAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Load().TrainingAvailability);
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
        var tags = new QuestTags(archive);
        var rawOrdered = new List<RawQuestDefinition>();
        var knownQuestIds = new HashSet<int>();
        foreach (var (questId, relativePath) in NormalizeListEntries(PvfListFile.Read(archive, ListPath)))
        {
            var fileIndex = archive.FindFile($"{QuestDirectory}/{relativePath}");
            if (fileIndex < 0)
                continue;
            knownQuestIds.Add(questId);

            try
            {
                var raw = Project(archive, tags, questId, fileIndex);
                var parts = Path.GetFileNameWithoutExtension(relativePath).Split('_', 3);
                var stage = parts.Length == 3 && parts[0].Equals("epic", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(parts[1], out var value) && value >= 0 ? (int?)value : null;
                rawOrdered.Add(raw with { Definition = raw.Definition with { StoryStageLevel = stage } });
            }
            catch (InvalidDataException)
            {
                // A declared restriction that cannot be interpreted is not unrestricted.
                // Excluding this definition is the conservative catalogue answer.
            }
        }

        var ordered = new List<QuestDefinition>(rawOrdered.Count);
        var byId = new Dictionary<int, QuestDefinition>();

        foreach (var raw in rawOrdered)
        {
            var relationships = NormalizeRelationships(
                raw.Definition.QuestId,
                raw.CompletedQuestGroups,
                raw.RequiredAnswerGroups,
                raw.CollisionQuestGroups,
                raw.AccountCollisionQuestGroups,
                knownQuestIds);
            if (relationships is null)
                continue;

            var definition = raw.Definition with
            {
                CompletedQuestPrerequisiteGroups = relationships.CompletedQuestGroups,
                RequiredQuestAnswers = relationships.RequiredAnswers,
                CollisionQuestIds = relationships.CollisionQuestIds,
                AccountCollisionQuestIds = relationships.AccountCollisionQuestIds,
            };
            ordered.Add(definition);
            byId.Add(definition.QuestId, definition);
        }

        if (ordered.Count == 0)
            throw new InvalidOperationException($"{ListPath} yielded no valid quest definitions.");

        return new Catalog(ordered, byId, ReadTrainingAvailability(archive, tags));
    }

    internal static IReadOnlyList<(int QuestId, string RelativePath)> NormalizeListEntries(
        IReadOnlyList<(int Id, string RelativePath)> entries)
    {
        var normalized = new List<(int, string)>();
        var seen = new HashSet<int>();
        foreach (var entry in entries)
        {
            if (entry.Id is < MinimumClientQuestId or > MaximumClientQuestId
                || !seen.Add(entry.Id))
            {
                continue;
            }

            normalized.Add((entry.Id, entry.RelativePath));
        }

        return normalized;
    }

    private static RawQuestDefinition Project(
        PvfArchive archive,
        QuestTags tags,
        int questId,
        int fileIndex)
    {
        var name = string.Empty;
        string? gradeText = null;
        string? jobText = null;
        string? targetCharacterText = null;
        var targetCharacterDeclared = false;
        var levelValues = new List<int>();
        var growTypeValues = new List<int>();
        var npcValues = new List<int>();
        var completeNpcValues = new List<int>();
        var exposureValues = new List<int>();
        var eventValues = new List<int>();
        var creatureKindValues = new List<int>();
        var creatureLevelValues = new List<int>();
        var expertJobValues = new List<int>();
        var jobChangeValues = new List<int>();
        var objectiveType = string.Empty;
        var objectiveSubType = new List<int>();
        var objectiveIntData = new List<int>();
        var enemyRewardValues = new List<int>();
        IReadOnlyList<QuestEnemyRewardItem> enemyRewards = [];
        var eventItemValues = new List<int>();
        var fixedItemRewardOperands = new List<ItemRewardOperand>();
        var selectableItemRewardOperands = new List<ItemRewardOperand>();
        var goldMultiplierValues = new List<int>();
        var difficulty = string.Empty;
        var rewards = new List<QuestReward>();
        var completedGroups = new List<IReadOnlyList<int>>();
        var requiredAnswerGroups = new List<IReadOnlyList<int>>();
        var collisionGroups = new List<IReadOnlyList<int>>();
        var accountCollisionGroups = new List<IReadOnlyList<int>>();
        var hasSpecialRewardStatus = false;
        var paysAtCharacterLevel = false;
        List<int>? currentRelationshipGroup = null;

        var scriptEntry = archive.GetEntry(fileIndex);
        var scriptPath = $"{archive.ResolveString(scriptEntry.PathOffset)}/{archive.ResolveString(scriptEntry.NameOffset)}";
        PvfDocument.Open(archive, scriptPath).ValidateFields(
            Fields.NameField,
            Fields.GradeField,
            Fields.JobField,
            Fields.LevelField,
            Fields.GrowTypeField,
            Fields.NpcIndexField,
            Fields.CompleteNpcIndexField,
            Fields.TypeField,
            Fields.SubTypeField,
            Fields.IntDataField,
            Fields.DifficultyField,
            Fields.RewardTypeField,
            Fields.GoldMultipleField,
            Fields.ExposedByNpcField,
            Fields.EventField,
            Fields.CreatureKindField,
            Fields.CreatureLevelField,
            Fields.ExpertjobLevelField,
            Fields.TargetCharacterField,
            Fields.JobChangeQuestField,
            Fields.PreRequiredQuestField,
            Fields.PreRequiredQuestAnswerField,
            Fields.CollisionQuestField,
            Fields.AccountCollisionQuestField,
            Fields.SpecialRewardStatusField,
            Fields.IgnoreQuestLevel4ExpField);
        var reader = archive.OpenScript(fileIndex);
        var section = -1;
        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier)
            {
                if (section >= 0 && (section == tags.MonsterRewardItem || section == tags.EnemyRewardItem))
                {
                    enemyRewards = ProjectEnemyRewardItems(enemyRewardValues, questId, section == tags.MonsterRewardItem);
                    enemyRewardValues.Clear();
                }
                section = token.Value;
                if (section == tags.SpecialRewardStatus)
                    hasSpecialRewardStatus = true;
                // Read as presence rather than as its value. Every declaration in the archive
                // carries a 1, and the reference server tests the flag rather than the number.
                if (section == tags.IgnoreQuestLevelForExperience)
                    paysAtCharacterLevel = true;
                if (section == tags.TargetCharacter)
                    targetCharacterDeclared = true;
                currentRelationshipGroup = StartRelationshipGroup(
                    section,
                    tags,
                    completedGroups,
                    requiredAnswerGroups,
                    collisionGroups,
                    accountCollisionGroups);
                continue;
            }

            if (currentRelationshipGroup is not null)
            {
                if (token.Kind != PvfTokenKind.Int32)
                    throw Invalid(questId, "relationship contains a non-integer token");
                currentRelationshipGroup.Add(token.Value);
                continue;
            }

            if (section == tags.Name)
                name = ReadString(archive, token, questId, "name");
            else if (section == tags.Grade)
                gradeText = AppendText(gradeText, ReadString(archive, token, questId, "grade"));
            else if (section == tags.Job)
                jobText = AppendText(jobText, ReadString(archive, token, questId, "job"));
            else if (section == tags.TargetCharacter && token.IsString)
                targetCharacterText = AppendText(
                    targetCharacterText,
                    ReadString(archive, token, questId, "target character"));
            else if (section == tags.Type)
                objectiveType = StripBrackets(ReadString(archive, token, questId, "type"));
            else if (section == tags.SubType)
                AddInteger(objectiveSubType, token, questId, "sub type");
            else if (section == tags.IntData)
                AddInteger(objectiveIntData, token, questId, "int data");
            else if (section == tags.MonsterRewardItem || section == tags.EnemyRewardItem)
                AddInteger(enemyRewardValues, token, questId, "enemy reward item");
            else if (section == tags.DependGiveItem)
                AddInteger(eventItemValues, token, questId, "depend give item");
            else if (section == tags.Difficulty)
                difficulty = ReadString(archive, token, questId, "difficulty");
            else if (section == tags.RewardType)
                rewards.Add(new QuestReward(
                    MapRequired(RewardTokens, ReadString(archive, token, questId, "reward type"), questId),
                    []));
            else if (section == tags.RewardIntData)
            {
                if (rewards.Count == 0)
                    rewards.Add(new QuestReward(QuestRewardKind.Item, []));
                if (rewards[^1].Kind == QuestRewardKind.Item)
                    AddItemRewardOperand(fixedItemRewardOperands, archive, token, questId);
                else
                    ReadInteger(rewardData: rewards, token, questId);
            }
            else if (section == tags.RewardSelectionIntData)
            {
                if (rewards.Count == 0)
                    rewards.Add(new QuestReward(QuestRewardKind.Item, []));
                if (rewards[^1].Kind == QuestRewardKind.Item)
                    AddItemRewardOperand(selectableItemRewardOperands, archive, token, questId);
            }
            else if (section == tags.Level)
                AddInteger(levelValues, token, questId, "level");
            else if (section == tags.GrowType)
                AddInteger(growTypeValues, token, questId, "grow type");
            else if (section == tags.NpcIndex)
                AddInteger(npcValues, token, questId, "npc index");
            else if (section == tags.CompleteNpcIndex)
                AddInteger(completeNpcValues, token, questId, "complete npc index");
            else if (section == tags.ExposedByNpc)
                AddInteger(exposureValues, token, questId, "exposed by npc");
            else if (section == tags.Event)
                AddInteger(eventValues, token, questId, "event");
            else if (section == tags.CreatureKind)
                AddInteger(creatureKindValues, token, questId, "creature kind");
            else if (section == tags.CreatureLevel)
                AddInteger(creatureLevelValues, token, questId, "creature level");
            else if (section == tags.ExpertJobLevel)
                AddInteger(expertJobValues, token, questId, "expertjob level");
            else if (section == tags.JobChangeQuest)
                AddInteger(jobChangeValues, token, questId, "job change quest");
            else if (section == tags.GoldMultiplier)
                AddInteger(goldMultiplierValues, token, questId, "gold multiple");
        }

        if (section >= 0 && (section == tags.MonsterRewardItem || section == tags.EnemyRewardItem))
            enemyRewards = ProjectEnemyRewardItems(enemyRewardValues, questId, section == tags.MonsterRewardItem);

        var grade = gradeText is null
            ? (QuestGrade?)null
            : MapRequired(GradeTokens, gradeText, questId);
        var requiredLevel = ProjectLevel(levelValues, questId);
        var growType = OptionalSingle(growTypeValues, questId, "grow type");
        if (growType == QuestDefinition.AnyGrowType)
            growType = null;

        var creatureKind = OptionalNonNegative(creatureKindValues, questId, "creature kind");
        var creatureLevel = OptionalNonNegative(creatureLevelValues, questId, "creature level");
        var expertJob = ProjectExpertJob(expertJobValues, questId);

        var definition = new QuestDefinition(
            questId,
            name,
            grade,
            jobText is null ? null : ProjectJobRestriction(jobText, questId),
            requiredLevel,
            growType,
            OptionalNonNegative(npcValues, questId, "npc index"),
            OptionalNonNegative(completeNpcValues, questId, "complete npc index"),
            rewards,
            objectiveType,
            difficulty,
            OptionalSingle(exposureValues, questId, "exposed by npc") is int exposure
                ? exposure != 0
                : null,
            OptionalSingle(eventValues, questId, "event") is int eventValue
                ? eventValue != 0
                : null,
            creatureKind,
            creatureLevel,
            expertJob,
            ProjectTargetCharacterRestriction(
                targetCharacterDeclared,
                targetCharacterText),
            OptionalSingle(jobChangeValues, questId, "job change quest"),
            [],
            [],
            [],
            [],
            hasSpecialRewardStatus,
            ProjectSeekingItems(objectiveType, objectiveIntData),
            ComputeInitialTrigger(objectiveType, objectiveIntData),
            ProjectEventItems(eventItemValues),
            paysAtCharacterLevel,
            QuestKillObjectiveParser.Parse(objectiveType, objectiveIntData),
            ProjectItemRewards(
                fixedItemRewardOperands,
                selectableItemRewardOperands,
                questId),
            OptionalNonNegative(goldMultiplierValues, questId, "gold multiple") ?? 0,
            enemyRewards,
            ProjectClearedQuestRequirements(objectiveType, objectiveIntData),
            ProjectDungeonClearRequirement(objectiveType, objectiveSubType, objectiveIntData));

        return new RawQuestDefinition(
            definition,
            completedGroups,
            requiredAnswerGroups,
            collisionGroups,
            accountCollisionGroups);
    }

    /// <summary>What the acceptance answer reports as this quest's starting progress.</summary>
    /// <remarks>
    /// Non-kill objectives retain their existing projection. Kill counts come exclusively from
    /// QuestDefinition.KillObjectives, shared with progress matching; they must not use a second
    /// stride-based interpretation here. A trigger holds three nine-bit remaining-count channels.
    /// See protocol/game/client-to-server/0x001F.md for the acceptance response.
    /// </remarks>
    private static uint ComputeInitialTrigger(string objectiveType, IReadOnlyList<int> intData) =>
        objectiveType switch
        {
            "condition under clear" or "clear map" => PackChannels(intData, 4),
            "condition under clear2" => PackChannels(intData, 5),
            "clear quest" or "quest clear" when intData.Count is > 0 and <= 3
                && intData.All(id => id > 0)
                && intData.Distinct().Count() == intData.Count => (uint)intData.Count,
            "normal clear" => PackTrigger(1, 1, 0),
            _ => DefaultInitialTrigger,
        };

    /// <summary>The trigger of a quest whose objective declares no counts.</summary>
    private const uint DefaultInitialTrigger = 1;

    /// <summary>
    /// Every stride-th value of the objective data, packed into the first three channels.
    /// </summary>
    private static uint PackChannels(IReadOnlyList<int> intData, int stride)
    {
        if (intData.Count == 0)
            return DefaultInitialTrigger;

        var channels = new List<int>();
        for (var index = 0; index + stride <= intData.Count; index += stride)
            channels.Add(intData[index + stride - 1]);

        return channels.Count == 0
            ? DefaultInitialTrigger
            : PackTrigger(
                channels[0],
                channels.Count > 1 ? channels[1] : 0,
                channels.Count > 2 ? channels[2] : 0);
    }

    /// <summary>Three counts in one word, nine bits each, low channel first.</summary>
    private static uint PackTrigger(int first, int second, int third) =>
        (uint)(((third & 0x1FF) << 18) | ((second & 0x1FF) << 9) | (first & 0x1FF));

    /// <summary>The <c>[depend give item]</c> pairs, read as item then count.</summary>
    /// <remarks>
    /// A trailing odd value is dropped rather than paired with a zero: a half-written pair is a
    /// file this source does not understand, and inventing the missing half would hand the
    /// character an item the archive never declared.
    /// </remarks>
    private static IReadOnlyList<QuestEventItem> ProjectEventItems(IReadOnlyList<int> values)
    {
        if (values.Count < 2)
            return [];

        var items = new List<QuestEventItem>(values.Count / 2);
        for (var index = 0; index + 1 < values.Count; index += 2)
        {
            if (values[index] > 0 && values[index + 1] > 0)
                items.Add(new QuestEventItem(values[index], values[index + 1]));
        }

        return items;
    }

    internal static IReadOnlyList<SeekingItemRequirement> ProjectSeekingItems(
        string objectiveType,
        IReadOnlyList<int> values)
    {
        var normalized = objectiveType.Trim().Trim('[', ']').ToLowerInvariant();
        var result = new List<SeekingItemRequirement>();
        if (normalized == "seek n meet npc")
        {
            if (values.Count >= 2 && values[0] > 0 && values[1] > 0)
                result.Add(new SeekingItemRequirement(values[0], values[1]));
            return result;
        }
        if (normalized != "seeking")
            return result;

        for (var index = 0; index + 1 < values.Count; index += 2)
        {
            if (values[index] < 0 || values[index + 1] <= 0)
                continue;
            result.Add(new SeekingItemRequirement(values[index], values[index + 1]));
        }
        return result;
    }

    internal static IReadOnlyList<QuestEnemyRewardItem> ProjectMonsterRewardItems(
        IReadOnlyList<int> values, int questId) => ProjectEnemyRewardItems(values, questId, legacyMonster: true);

    internal static IReadOnlyList<QuestEnemyRewardItem> ProjectEnemyRewardItems(
        IReadOnlyList<int> values, int questId, bool legacyMonster = false)
    {
        var width = legacyMonster ? 7 : 8;
        if (values.Count % width != 0)
            throw Invalid(questId, $"enemy reward item must contain {width}-field groups");
        var result = new List<QuestEnemyRewardItem>(values.Count / width);
        for (var index = 0; index < values.Count;)
        {
            var enemyCode = values[index++];
            var type = legacyMonster ? QuestEnemyType.Monster : (QuestEnemyType)values[index++];
            var dungeonId = values[index++];
            var difficulty = values[index++];
            var itemId = values[index++];
            var count = values[index++];
            var dropRate = values[index++];
            var maxStack = values[index++];
            if (enemyCode <= 0 || !Enum.IsDefined(type) || dungeonId < -1 || difficulty < -1
                || itemId <= 0 || count <= 0 || dropRate is < 0 or > 100 || maxStack < -1)
                throw Invalid(questId, "enemy reward item contains an invalid field");
            result.Add(new(enemyCode, type, dungeonId, difficulty, itemId, count, dropRate, maxStack));
        }
        return result;
    }

    internal static IReadOnlyList<int> ProjectClearedQuestRequirements(
        string objectiveType,
        IReadOnlyList<int> values)
    {
        var normalized = objectiveType.Trim().Trim('[', ']').ToLowerInvariant();
        return normalized is "clear quest" or "quest clear"
            && values.Count is > 0 and <= 3 && values.All(id => id > 0)
            && values.Distinct().Count() == values.Count
            ? values.ToArray()
            : [];
    }

    internal static DungeonClearRequirement? ProjectDungeonClearRequirement(
        string objectiveType,
        IReadOnlyList<int> subType,
        IReadOnlyList<int> values)
    {
        // Other clear objectives have different operand layouts and remain client-counted.
        return string.Equals(objectiveType.Trim().Trim('[', ']'), "condition under clear",
                StringComparison.OrdinalIgnoreCase)
            && subType.Count == 1 && subType[0] == 6
            && values.Count == 2 && values[0] > 0 && values[1] >= -1
            ? new DungeonClearRequirement(values[0], values[1])
            : null;
    }

    internal static RelationshipProjection? NormalizeRelationships(
        int ownerQuestId,
        IReadOnlyList<IReadOnlyList<int>> completedQuestGroups,
        IReadOnlyList<IReadOnlyList<int>> requiredAnswerGroups,
        IReadOnlyList<IReadOnlyList<int>> collisionQuestGroups,
        IReadOnlyList<IReadOnlyList<int>> accountCollisionQuestGroups,
        IReadOnlySet<int> knownQuestIds)
    {
        if (ownerQuestId <= 0 || knownQuestIds is null)
            return null;

        var completed = new List<IReadOnlyList<int>>();
        var hasDeclaredCompletedGroup = false;
        foreach (var group in completedQuestGroups)
        {
            if (group.Count == 0)
                continue;
            hasDeclaredCompletedGroup = true;
            if (group.Any(id => id <= 0 || id == ownerQuestId))
                return null;
            if (group.Any(id => !knownQuestIds.Contains(id)))
                continue;
            completed.Add(Distinct(group));
        }
        if (hasDeclaredCompletedGroup && completed.Count == 0)
            return null;

        var answers = new List<QuestAnswerRequirement>();
        foreach (var group in requiredAnswerGroups)
        {
            if (group.Count % 2 != 0)
                return null;
            for (var index = 0; index < group.Count; index += 2)
            {
                var questId = group[index];
                var answerIndex = group[index + 1];
                if (questId <= 0
                    || questId == ownerQuestId
                    || !knownQuestIds.Contains(questId)
                    || answerIndex < 0)
                {
                    return null;
                }
                answers.Add(new QuestAnswerRequirement(questId, answerIndex));
            }
        }

        var collisions = NormalizeCollisions(
            ownerQuestId,
            collisionQuestGroups,
            knownQuestIds);
        var accountCollisions = NormalizeCollisions(
            ownerQuestId,
            accountCollisionQuestGroups,
            knownQuestIds);
        if (collisions is null || accountCollisions is null)
            return null;

        return new RelationshipProjection(completed, answers, collisions, accountCollisions);
    }

    private static IReadOnlyList<int>? NormalizeCollisions(
        int ownerQuestId,
        IReadOnlyList<IReadOnlyList<int>> groups,
        IReadOnlySet<int> knownQuestIds)
    {
        var result = new List<int>();
        var seen = new HashSet<int>();
        foreach (var group in groups)
        {
            foreach (var questId in group)
            {
                if (questId <= 0 || questId == ownerQuestId)
                    return null;
                if (knownQuestIds.Contains(questId) && seen.Add(questId))
                    result.Add(questId);
            }
        }
        return result;
    }

    private static IReadOnlyList<TrainingQuestAvailability> ReadTrainingAvailability(
        PvfArchive archive,
        QuestTags tags)
    {
        var fileIndex = archive.FindFile(TrainingListPath);
        if (fileIndex < 0 || tags.TrainingLevel < 0)
            return [];

        var blocks = new List<IReadOnlyList<int?>>();
        var reader = archive.OpenScript(fileIndex);
        List<int?>? block = null;
        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier)
            {
                if (block is not null)
                    blocks.Add(block);
                block = token.Value == tags.TrainingLevel ? [] : null;
                continue;
            }
            if (block is not null)
                block.Add(token.Kind == PvfTokenKind.Int32 ? token.Value : null);
        }
        if (block is not null)
            blocks.Add(block);

        return NormalizeTrainingBlocks(blocks);
    }

    internal static IReadOnlyList<TrainingQuestAvailability> NormalizeTrainingBlocks(
        IReadOnlyList<IReadOnlyList<int?>> blocks)
    {
        var byLevel = new SortedDictionary<int, List<int>>();
        foreach (var block in blocks)
        {
            if (block.Count < 2 || block.Any(value => !value.HasValue))
                continue;

            var values = block.Select(value => value!.Value).ToList();
            if (values[0] is < 1 or > 70 || values.Skip(1).Any(value => value <= 0))
                continue;

            var level = values[0];
            foreach (var npcIndex in values.Skip(1))
            {
                if (!byLevel.TryGetValue(level, out var npcs))
                    byLevel[level] = npcs = [];
                if (!npcs.Contains(npcIndex))
                    npcs.Add(npcIndex);
            }
        }

        return byLevel
            .Where(pair => pair.Value.Count > 0)
            .Select(pair => new TrainingQuestAvailability(pair.Key, pair.Value))
            .ToList();
    }

    internal static QuestTargetCharacterRestriction? ProjectTargetCharacterRestriction(
        bool isDeclared,
        string? targetCharacterText)
    {
        if (!isDeclared)
            return null;

        if (targetCharacterText is null)
            return new QuestTargetCharacterRestriction([]);

        var jobFamilies = ArchiveJobFamilyTokens.All
            .Where(pair => targetCharacterText.Contains(pair.Key, StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Value)
            .Distinct()
            .ToList();
        return new QuestTargetCharacterRestriction(jobFamilies);
    }

    private static List<int>? StartRelationshipGroup(
        int section,
        QuestTags tags,
        List<IReadOnlyList<int>> completed,
        List<IReadOnlyList<int>> answers,
        List<IReadOnlyList<int>> collisions,
        List<IReadOnlyList<int>> accountCollisions)
    {
        var group = new List<int>();
        if (section == tags.PreRequiredQuest)
            completed.Add(group);
        else if (section == tags.PreRequiredQuestAnswer)
            answers.Add(group);
        else if (section == tags.CollisionQuest)
            collisions.Add(group);
        else if (section == tags.AccountCollisionQuest)
            accountCollisions.Add(group);
        else
            return null;
        return group;
    }

    internal static QuestJobRestriction ProjectJobRestriction(string value, int questId)
    {
        var normalized = value.Trim();
        if (normalized.Length == 0)
            throw Invalid(questId, "job declaration is empty");

        var requirements = new List<QuestJobRequirement>();
        var index = 0;
        while (index < normalized.Length)
        {
            while (index < normalized.Length && char.IsWhiteSpace(normalized[index]))
                index++;
            if (index >= normalized.Length)
                break;
            if (normalized[index] != '[')
                throw Invalid(questId, "job contains text outside a family token");

            var end = normalized.IndexOf(']', index + 1);
            if (end < 0)
                throw Invalid(questId, "job contains an unterminated family token");
            var token = normalized[index..(end + 1)];
            if (!ArchiveJobFamilyTokens.TryResolve(token, out var requirement))
                throw Invalid(questId, $"job contains unrecognized token '{token}'");
            requirements.Add(requirement);
            index = end + 1;
        }

        if (normalized.Equals("[all]", StringComparison.OrdinalIgnoreCase))
            return new QuestJobRestriction(IsExactAll: true, JobFamilies: []);

        var families = requirements
            .Where(requirement => requirement is not QuestJobRequirement.Any)
            .Distinct()
            .ToArray();
        if (families.Length == 0)
            throw Invalid(questId, "job contains no concrete family outside exact [all]");

        return new QuestJobRestriction(IsExactAll: false, families);
    }

    private static LevelRange? ProjectLevel(IReadOnlyList<int> values, int questId)
    {
        if (values.Count == 0)
            return null;
        if (values.Count is not (1 or 2))
            throw Invalid(questId, "level must contain one or two integers");
        var range = new LevelRange(values[0], values.Count == 2 ? values[1] : 99);
        if (range.Minimum < 0 || range.Maximum < range.Minimum)
            throw Invalid(questId, "level range is invalid");
        return range;
    }

    private static ExpertJobRequirement? ProjectExpertJob(IReadOnlyList<int> values, int questId)
    {
        if (values.Count == 0)
            return null;
        if (values.Count != 2 || values[0] < 0 || values[1] < 0)
            throw Invalid(questId, "expertjob level must contain a non-negative type/level pair");
        return new ExpertJobRequirement(values[0], values[1]);
    }

    private static int? OptionalNonNegative(List<int> values, int questId, string field)
    {
        var value = OptionalSingle(values, questId, field);
        if (value == -1)
            return null;
        if (value < 0)
            throw Invalid(questId, $"{field} must be non-negative");
        return value;
    }

    private static int? OptionalSingle(List<int> values, int questId, string field)
    {
        if (values.Count == 0)
            return null;
        if (values.Count != 1)
            throw Invalid(questId, $"{field} must contain one integer");
        return values[0];
    }

    private static void AddInteger(List<int> values, PvfToken token, int questId, string field)
    {
        if (token.Kind != PvfTokenKind.Int32)
            throw Invalid(questId, $"{field} contains a non-integer token");
        values.Add(token.Value);
    }

    private static void ReadInteger(List<QuestReward> rewardData, PvfToken token, int questId)
    {
        if (token.Kind != PvfTokenKind.Int32)
            throw Invalid(questId, "reward int data contains a non-integer token");
        if (rewardData.Count > 0)
        {
            var reward = rewardData[^1];
            rewardData[^1] = reward with { Values = [.. reward.Values, token.Value] };
        }
    }

    private static void AddItemRewardOperand(
        List<ItemRewardOperand> values,
        PvfArchive archive,
        PvfToken token,
        int questId)
    {
        if (token.Kind == PvfTokenKind.Int32)
        {
            values.Add(ItemRewardOperand.Number(token.Value));
            return;
        }

        if (token.IsString
            && archive.ResolveString(token.Value).Trim()
                .Equals("[job]", StringComparison.OrdinalIgnoreCase))
        {
            values.Add(ItemRewardOperand.JobMarker);
            return;
        }

        throw Invalid(questId, "item reward contains an unrecognized operand");
    }

    internal static QuestItemRewardDefinition ProjectItemRewards(
        IReadOnlyList<ItemRewardOperand> fixedOperands,
        IReadOnlyList<ItemRewardOperand> selectableOperands,
        int questId)
    {
        var fixedItems = ParseItemRewardRules(
            fixedOperands, allowGold: true, questId, out var formula, out var fixedGold);
        var selectableItems = ParseItemRewardRules(
            selectableOperands, allowGold: false, questId, out _, out _);
        return new QuestItemRewardDefinition(fixedItems, selectableItems, formula, fixedGold);
    }

    private static IReadOnlyList<QuestItemRewardRule> ParseItemRewardRules(
        IReadOnlyList<ItemRewardOperand> operands,
        bool allowGold,
        int questId,
        out bool hasGoldFormula,
        out int fixedGold)
    {
        var items = new List<QuestItemRewardRule>();
        hasGoldFormula = false;
        fixedGold = 0;
        var index = 0;
        while (index < operands.Count)
        {
            if (!operands[index++].TryNumber(out var itemId))
                throw Invalid(questId, "item reward requires an integer item id");

            if (index < operands.Count && operands[index].IsJobMarker)
            {
                index++;
                if (itemId <= 0
                    || !TakeNumber(operands, ref index, out var jobId)
                    || !TakeNumber(operands, ref index, out var growType)
                    || !TakeNumber(operands, ref index, out var count)
                    || jobId < -1
                    || growType < -1
                    || count <= 0)
                {
                    throw Invalid(questId, "job-filtered item reward is malformed");
                }

                items.Add(new QuestItemRewardRule(
                    itemId,
                    count,
                    new QuestRewardCharacterCondition(jobId, growType)));
                continue;
            }

            if (!TakeNumber(operands, ref index, out var ordinaryCount))
                throw Invalid(questId, "item reward has no integer count");

            if (itemId == 0)
            {
                if (!allowGold || ordinaryCount < 0)
                    throw Invalid(questId, "gold is not valid in this item reward list");
                if (ordinaryCount == 0)
                    hasGoldFormula = true;
                else
                    fixedGold = checked(fixedGold + ordinaryCount);
                continue;
            }

            if (itemId < 0 || ordinaryCount <= 0)
                throw Invalid(questId, "item reward contains a non-positive item or count");
            items.Add(new QuestItemRewardRule(itemId, ordinaryCount));
        }

        return items;
    }

    private static bool TakeNumber(
        IReadOnlyList<ItemRewardOperand> operands,
        ref int index,
        out int value)
    {
        if (index < operands.Count && operands[index++].TryNumber(out value))
            return true;
        value = 0;
        return false;
    }

    private static string ReadString(PvfArchive archive, PvfToken token, int questId, string field) =>
        token.IsString
            ? archive.ResolveString(token.Value)
            : throw Invalid(questId, $"{field} contains a non-string token");

    private static string AppendText(string? current, string value) =>
        current is null ? value : $"{current} {value}";

    private static TValue MapRequired<TValue>(
        Dictionary<string, TValue> table,
        string token,
        int questId) =>
        table.TryGetValue(token.Trim(), out var value)
            ? value
            : throw Invalid(questId, $"unrecognized token '{token.Trim()}'");

    private static IReadOnlyList<int> Distinct(IReadOnlyList<int> values)
    {
        var result = new List<int>();
        var seen = new HashSet<int>();
        foreach (var value in values)
        {
            if (seen.Add(value))
                result.Add(value);
        }
        return result;
    }

    private static InvalidDataException Invalid(int questId, string message) =>
        new($"Quest {questId}: {message}.");

    private static string StripBrackets(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[^1] == ']'
            ? trimmed[1..^1]
            : trimmed;
    }

    private sealed class QuestTags
    {
        public QuestTags(PvfArchive archive)
        {
            Name = Offset(archive, Fields.NameField);
            Grade = Offset(archive, Fields.GradeField);
            Job = Offset(archive, Fields.JobField);
            Level = Offset(archive, Fields.LevelField);
            GrowType = Offset(archive, Fields.GrowTypeField);
            NpcIndex = Offset(archive, Fields.NpcIndexField);
            CompleteNpcIndex = Offset(archive, Fields.CompleteNpcIndexField);
            Type = Offset(archive, Fields.TypeField);
            SubType = Offset(archive, Fields.SubTypeField);
            IntData = Offset(archive, Fields.IntDataField);
            MonsterRewardItem = Offset(archive, Fields.MonsterRewardItemField);
            EnemyRewardItem = Offset(archive, Fields.EnemyRewardItemField);
            DependGiveItem = Offset(archive, Fields.DependGiveItemField);
            Difficulty = Offset(archive, Fields.DifficultyField);
            RewardType = Offset(archive, Fields.RewardTypeField);
            RewardIntData = Offset(archive, Fields.RewardIntDataField);
            RewardSelectionIntData = Offset(archive, Fields.RewardSelectionIntDataField);
            GoldMultiplier = Offset(archive, Fields.GoldMultipleField);
            ExposedByNpc = Offset(archive, Fields.ExposedByNpcField);
            Event = Offset(archive, Fields.EventField);
            CreatureKind = Offset(archive, Fields.CreatureKindField);
            CreatureLevel = Offset(archive, Fields.CreatureLevelField);
            ExpertJobLevel = Offset(archive, Fields.ExpertjobLevelField);
            TargetCharacter = Offset(archive, Fields.TargetCharacterField);
            JobChangeQuest = Offset(archive, Fields.JobChangeQuestField);
            PreRequiredQuest = Offset(archive, Fields.PreRequiredQuestField);
            PreRequiredQuestAnswer = Offset(archive, Fields.PreRequiredQuestAnswerField);
            CollisionQuest = Offset(archive, Fields.CollisionQuestField);
            AccountCollisionQuest = Offset(archive, Fields.AccountCollisionQuestField);
            SpecialRewardStatus = Offset(archive, Fields.SpecialRewardStatusField);
            IgnoreQuestLevelForExperience = Offset(archive, Fields.IgnoreQuestLevel4ExpField);
            TrainingLevel = Level;
        }

        public int Name { get; }
        public int Grade { get; }
        public int Job { get; }
        public int Level { get; }
        public int GrowType { get; }
        public int NpcIndex { get; }
        public int CompleteNpcIndex { get; }
        public int Type { get; }
        public int SubType { get; }
        public int IntData { get; }
        public int MonsterRewardItem { get; }
        public int EnemyRewardItem { get; }

        public int DependGiveItem { get; }
        public int Difficulty { get; }
        public int RewardType { get; }
        public int RewardIntData { get; }
        public int RewardSelectionIntData { get; }
        public int GoldMultiplier { get; }
        public int ExposedByNpc { get; }
        public int Event { get; }
        public int CreatureKind { get; }
        public int CreatureLevel { get; }
        public int ExpertJobLevel { get; }
        public int TargetCharacter { get; }
        public int JobChangeQuest { get; }
        public int PreRequiredQuest { get; }
        public int PreRequiredQuestAnswer { get; }
        public int CollisionQuest { get; }
        public int AccountCollisionQuest { get; }
        public int SpecialRewardStatus { get; }

        public int IgnoreQuestLevelForExperience { get; }
        public int TrainingLevel { get; }

        private static int Offset(PvfArchive archive, PvfField tag) => archive.GetTag(tag).Offset;
    }

    private sealed record RawQuestDefinition(
        QuestDefinition Definition,
        IReadOnlyList<IReadOnlyList<int>> CompletedQuestGroups,
        IReadOnlyList<IReadOnlyList<int>> RequiredAnswerGroups,
        IReadOnlyList<IReadOnlyList<int>> CollisionQuestGroups,
        IReadOnlyList<IReadOnlyList<int>> AccountCollisionQuestGroups);

    internal readonly record struct ItemRewardOperand(int Value, bool IsJobMarker)
    {
        public static ItemRewardOperand Number(int value) => new(value, false);

        public static ItemRewardOperand JobMarker => new(0, true);

        public bool TryNumber(out int value)
        {
            value = this.Value;
            return !this.IsJobMarker;
        }
    }

    private sealed record Catalog(
        IReadOnlyList<QuestDefinition> Ordered,
        Dictionary<int, QuestDefinition> ById,
        IReadOnlyList<TrainingQuestAvailability> TrainingAvailability);
}
