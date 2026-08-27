using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>Projects the archive's quest scripts into neutral, validated definitions.</summary>
internal sealed class PvfQuestDefinitionSource : IQuestDefinitionSource
{
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
                rawOrdered.Add(Project(archive, tags, questId, fileIndex));
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
        var objectiveIntData = new List<int>();
        var eventItemValues = new List<int>();
        var difficulty = string.Empty;
        var rewards = new List<QuestReward>();
        var completedGroups = new List<IReadOnlyList<int>>();
        var requiredAnswerGroups = new List<IReadOnlyList<int>>();
        var collisionGroups = new List<IReadOnlyList<int>>();
        var accountCollisionGroups = new List<IReadOnlyList<int>>();
        var hasSpecialRewardStatus = false;
        List<int>? currentRelationshipGroup = null;

        var reader = archive.OpenScript(fileIndex);
        var section = -1;
        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier)
            {
                section = token.Value;
                if (section == tags.SpecialRewardStatus)
                    hasSpecialRewardStatus = true;
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
            else if (section == tags.IntData)
                AddInteger(objectiveIntData, token, questId, "int data");
            else if (section == tags.DependGiveItem)
                AddInteger(eventItemValues, token, questId, "depend give item");
            else if (section == tags.Difficulty)
                difficulty = ReadString(archive, token, questId, "difficulty");
            else if (section == tags.RewardType)
                rewards.Add(new QuestReward(
                    MapRequired(RewardTokens, ReadString(archive, token, questId, "reward type"), questId),
                    []));
            else if (section == tags.RewardIntData && token.Kind == PvfTokenKind.Int32)
            {
                ReadInteger(rewardData: rewards, token, questId);
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
        }

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
            ProjectEventItems(eventItemValues));

        return new RawQuestDefinition(
            definition,
            completedGroups,
            requiredAnswerGroups,
            collisionGroups,
            accountCollisionGroups);
    }

    /// <summary>What the acceptance answer reports as this quest's starting progress.</summary>
    /// <remarks>
    /// A trigger is three nine-bit channels packed into a word, and which counts fill them depends
    /// on what the quest is about. The reference server reads them out of <c>[int data]</c> at a
    /// stride set by the objective type, and falls back to one for everything it does not
    /// recognise.
    /// <para>
    /// <b>Three of its branches are not implemented here</b>, and each falls back to one rather
    /// than to a guess: <c>seek n meet npc</c> and the quest-clear family both need indexes this
    /// source does not build, and the <c>[sub type] 6</c> case needs a tag it does not read. The
    /// two quests observed being accepted on the wire are <c>meet npc</c>, which reaches the same
    /// fallback in the reference server itself -- so what is evidenced end to end is the fallback,
    /// and the strided branches are transcribed rather than confirmed. See
    /// protocol/game/client-to-server/0x001F.md.
    /// </para>
    /// </remarks>
    private static uint ComputeInitialTrigger(string objectiveType, IReadOnlyList<int> intData) =>
        objectiveType switch
        {
            "condition under clear" or "clear map" or "hunt monster" => PackChannels(intData, 4),
            "condition under clear2" or "hunt enemy" => PackChannels(intData, 5),
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
            Name = Offset(archive, "[name]");
            Grade = Offset(archive, "[grade]");
            Job = Offset(archive, "[job]");
            Level = Offset(archive, "[level]");
            GrowType = Offset(archive, "[grow type]");
            NpcIndex = Offset(archive, "[npc index]");
            CompleteNpcIndex = Offset(archive, "[complete npc index]");
            Type = Offset(archive, "[type]");
            IntData = Offset(archive, "[int data]");
            DependGiveItem = Offset(archive, "[depend give item]");
            Difficulty = Offset(archive, "[difficulty]");
            RewardType = Offset(archive, "[reward type]");
            RewardIntData = Offset(archive, "[reward int data]");
            ExposedByNpc = Offset(archive, "[exposed by npc]");
            Event = Offset(archive, "[event]");
            CreatureKind = Offset(archive, "[creature kind]");
            CreatureLevel = Offset(archive, "[creature level]");
            ExpertJobLevel = Offset(archive, "[expertjob level]");
            TargetCharacter = Offset(archive, "[target character]");
            JobChangeQuest = Offset(archive, "[job change quest]");
            PreRequiredQuest = Offset(archive, "[pre required quest]");
            PreRequiredQuestAnswer = Offset(archive, "[pre required quest answer]");
            CollisionQuest = Offset(archive, "[collision quest]");
            AccountCollisionQuest = Offset(archive, "[account collision quest]");
            SpecialRewardStatus = Offset(archive, "[special reward status]");
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
        public int IntData { get; }

        public int DependGiveItem { get; }
        public int Difficulty { get; }
        public int RewardType { get; }
        public int RewardIntData { get; }
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
        public int TrainingLevel { get; }

        private static int Offset(PvfArchive archive, string tag) => archive.GetTag(tag).Offset;
    }

    private sealed record RawQuestDefinition(
        QuestDefinition Definition,
        IReadOnlyList<IReadOnlyList<int>> CompletedQuestGroups,
        IReadOnlyList<IReadOnlyList<int>> RequiredAnswerGroups,
        IReadOnlyList<IReadOnlyList<int>> CollisionQuestGroups,
        IReadOnlyList<IReadOnlyList<int>> AccountCollisionQuestGroups);

    private sealed record Catalog(
        IReadOnlyList<QuestDefinition> Ordered,
        Dictionary<int, QuestDefinition> ById,
        IReadOnlyList<TrainingQuestAvailability> TrainingAvailability);
}
