using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>
/// Projects the archive's quest scripts into <see cref="QuestDefinition"/>.
/// </summary>
/// <remarks>
/// This is the only place that knows how a quest is spelled in the archive: which tags
/// carry which field, and which token text maps to which modelled distinction. Everything
/// above it sees typed values, so the vocabulary is normalised exactly once.
/// </remarks>
internal sealed class PvfQuestDefinitionSource : IQuestDefinitionSource
{
    private const string ListPath = "n_quest/quest.lst";
    private const string QuestDirectory = "n_quest";

    private static readonly Dictionary<string, QuestJobRequirement> JobTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        ["[all]"] = QuestJobRequirement.Any,
        ["[swordman]"] = QuestJobRequirement.Swordman,
        ["[fighter]"] = QuestJobRequirement.Fighter,
        ["[gunner]"] = QuestJobRequirement.Gunner,
        ["[mage]"] = QuestJobRequirement.Mage,
        ["[priest]"] = QuestJobRequirement.Priest,
        ["[thief]"] = QuestJobRequirement.Thief,
        ["[knight]"] = QuestJobRequirement.Knight,
        ["[demonic swordman]"] = QuestJobRequirement.DemonicSwordman,
        ["[at swordman]"] = QuestJobRequirement.AwakenedSwordman,
        ["[at fighter]"] = QuestJobRequirement.AwakenedFighter,
        ["[at gunner]"] = QuestJobRequirement.AwakenedGunner,
        ["[at mage]"] = QuestJobRequirement.AwakenedMage,
    };

    private static readonly Dictionary<string, QuestGrade> GradeTokens = new(StringComparer.OrdinalIgnoreCase)
    {
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

    private static readonly Dictionary<string, QuestRewardKind> RewardTokens = new(StringComparer.OrdinalIgnoreCase)
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

    private Catalog Load()
    {
        if (_catalog is not null)
            return _catalog;

        lock (_gate)
            return _catalog ??= Build(_provider.Archive);
    }

    private static Catalog Build(PvfArchive archive)
    {
        var listIndex = archive.FindFile(ListPath);
        if (listIndex < 0)
            throw new InvalidOperationException($"The archive does not contain {ListPath}.");

        var tags = new QuestTags(archive);
        var ordered = new List<QuestDefinition>();
        var byId = new Dictionary<int, QuestDefinition>();

        foreach (var (questId, relativePath) in ReadList(archive, listIndex))
        {
            var fileIndex = archive.FindFile($"{QuestDirectory}/{relativePath}");
            if (fileIndex < 0)
                continue;

            var definition = Project(archive, tags, questId, fileIndex);
            ordered.Add(definition);
            byId.TryAdd(questId, definition);
        }

        if (ordered.Count == 0)
            throw new InvalidOperationException($"{ListPath} yielded no quest definitions.");

        return new Catalog(ordered, byId);
    }

    /// <summary>A list file alternates an integer identifier with the relative file name.</summary>
    private static List<(int Id, string Path)> ReadList(PvfArchive archive, int listIndex)
    {
        var entries = new List<(int, string)>();
        var reader = archive.OpenScript(listIndex);
        var pendingId = int.MinValue;

        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Int32)
            {
                pendingId = token.Value;
            }
            else if (token.IsString && pendingId != int.MinValue)
            {
                var name = archive.ResolveString(token.Value).Replace('\\', '/');
                if (name.Length > 0)
                    entries.Add((pendingId, name));
                pendingId = int.MinValue;
            }
        }

        return entries;
    }

    private static QuestDefinition Project(PvfArchive archive, QuestTags tags, int questId, int fileIndex)
    {
        var name = string.Empty;
        var grade = QuestGrade.Unrecognized;
        var job = QuestJobRequirement.Any;
        var level = LevelRange.Any;
        var growType = QuestDefinition.AnyGrowType;
        var npcIndex = -1;
        var completeNpcIndex = -1;
        var objectiveType = string.Empty;
        var difficulty = string.Empty;
        var rewards = new List<QuestReward>();

        var reader = archive.OpenScript(fileIndex);
        var section = -1;

        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier)
            {
                section = token.Value;
                if (token.Value == tags.RewardType)
                {
                    if (reader.TryReadSectionValue(out var kindToken) && kindToken.IsString)
                        rewards.Add(new QuestReward(
                            Map(RewardTokens, archive.ResolveString(kindToken.Value), QuestRewardKind.Unrecognized),
                            []));
                }

                continue;
            }

            if (section == tags.Name && token.IsString)
                name = archive.ResolveString(token.Value);
            else if (section == tags.Grade && token.IsString)
                grade = Map(GradeTokens, archive.ResolveString(token.Value), QuestGrade.Unrecognized);
            else if (section == tags.Job && token.IsString)
                job = Map(JobTokens, archive.ResolveString(token.Value), QuestJobRequirement.Unrecognized);
            else if (section == tags.Type && token.IsString)
                objectiveType = archive.ResolveString(token.Value);
            else if (section == tags.Difficulty && token.IsString)
                difficulty = archive.ResolveString(token.Value);
            else if (section == tags.GrowType && token.Kind == PvfTokenKind.Int32)
                growType = token.Value;
            else if (section == tags.NpcIndex && token.Kind == PvfTokenKind.Int32)
                npcIndex = token.Value;
            else if (section == tags.CompleteNpcIndex && token.Kind == PvfTokenKind.Int32)
                completeNpcIndex = token.Value;
            else if (section == tags.Level && token.Kind == PvfTokenKind.Int32)
                level = level.Minimum == LevelRange.Any.Minimum && level.Maximum == LevelRange.Any.Maximum
                    ? new LevelRange(token.Value, int.MaxValue)
                    : new LevelRange(level.Minimum, token.Value);
            else if (section == tags.RewardIntData && token.Kind == PvfTokenKind.Int32 && rewards.Count > 0)
                rewards[^1] = Append(rewards[^1], token.Value);
        }

        return new QuestDefinition(
            questId, name, grade, job, level, growType, npcIndex, completeNpcIndex, rewards, objectiveType, difficulty);
    }

    private static QuestReward Append(QuestReward reward, int value) =>
        reward with { Values = [.. reward.Values, value] };

    private static TValue Map<TValue>(Dictionary<string, TValue> table, string token, TValue fallback) =>
        table.GetValueOrDefault(token.Trim(), fallback);

    /// <summary>Tag offsets resolved once so the projection loop compares integers.</summary>
    private sealed class QuestTags
    {
        public QuestTags(PvfArchive archive)
        {
            Name = archive.GetTag("[name]").Offset;
            Grade = archive.GetTag("[grade]").Offset;
            Job = archive.GetTag("[job]").Offset;
            Level = archive.GetTag("[level]").Offset;
            GrowType = archive.GetTag("[grow type]").Offset;
            NpcIndex = archive.GetTag("[npc index]").Offset;
            CompleteNpcIndex = archive.GetTag("[complete npc index]").Offset;
            Type = archive.GetTag("[type]").Offset;
            Difficulty = archive.GetTag("[difficulty]").Offset;
            RewardType = archive.GetTag("[reward type]").Offset;
            RewardIntData = archive.GetTag("[reward int data]").Offset;
        }

        public int Name { get; }
        public int Grade { get; }
        public int Job { get; }
        public int Level { get; }
        public int GrowType { get; }
        public int NpcIndex { get; }
        public int CompleteNpcIndex { get; }
        public int Type { get; }
        public int Difficulty { get; }
        public int RewardType { get; }
        public int RewardIntData { get; }
    }

    private sealed record Catalog(IReadOnlyList<QuestDefinition> Ordered, Dictionary<int, QuestDefinition> ById);
}
