using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>
/// Projects the archive's daily-challenge table into <see cref="DailyActivityGroupDefinition"/>.
/// </summary>
/// <remarks>
/// The table is a sequence of <c>[group]</c> blocks. Inside one, the allowance table lists
/// level-band triples, each <c>[slot]</c> opens with its own index followed by the quests
/// that may fill it, and the reward table lists level-band quadruples.
/// </remarks>
internal sealed class PvfDailyActivityDefinitionSource : IDailyActivityDefinitionSource
{
    private const string TablePath = "etc/dailychallengetable.etc";

    private readonly PvfArchiveProvider _provider;
    private readonly Lock _gate = new();
    private Catalog? _catalog;

    public PvfDailyActivityDefinitionSource(PvfArchiveProvider provider) => _provider = provider;

    public ValueTask<IReadOnlyList<DailyActivityGroupDefinition>> GetAllAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Load().Ordered);
    }

    public ValueTask<DailyActivityGroupDefinition?> GetAsync(int groupId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Load().ById.GetValueOrDefault(groupId));
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
        var fileIndex = archive.FindFile(TablePath);
        if (fileIndex < 0)
            throw new InvalidOperationException($"The archive does not contain {TablePath}.");

        var groupTag = archive.GetTag("[group]").Offset;
        var groupClose = archive.GetTag("[/group]").Offset;
        var levelTag = archive.GetTag("[level]").Offset;
        var allowanceTag = archive.GetTag("[slot num table]").Offset;
        var slotTag = archive.GetTag("[slot]").Offset;
        var slotClose = archive.GetTag("[/slot]").Offset;
        var rewardTag = archive.GetTag("[reward table]").Offset;

        var ordered = new List<DailyActivityGroupDefinition>();
        var byId = new Dictionary<int, DailyActivityGroupDefinition>();
        var reader = archive.OpenScript(fileIndex);

        Draft? draft = null;
        var section = -1;

        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier)
            {
                if (token.Value == groupTag)
                {
                    draft = new Draft();
                    section = groupTag;
                }
                else if (token.Value == groupClose && draft is not null)
                {
                    var definition = draft.ToDefinition();
                    ordered.Add(definition);
                    byId.TryAdd(definition.GroupId, definition);
                    draft = null;
                }
                else if (token.Value == slotTag && draft is not null)
                {
                    draft.Slots.Add([]);
                    section = slotTag;
                }
                else if (token.Value == slotClose)
                {
                    section = -1;
                }
                else
                {
                    section = token.Value;
                }

                continue;
            }

            if (draft is null || token.Kind != PvfTokenKind.Int32)
                continue;

            if (section == groupTag && draft.GroupId < 0)
                draft.GroupId = token.Value;
            else if (section == levelTag)
                draft.LevelOperands.Add(token.Value);
            else if (section == allowanceTag)
                draft.AllowanceOperands.Add(token.Value);
            else if (section == slotTag && draft.Slots.Count > 0)
                draft.Slots[^1].Add(token.Value);
            else if (section == rewardTag)
                draft.RewardOperands.Add(token.Value);
        }

        if (ordered.Count == 0)
            throw new InvalidOperationException($"{TablePath} declared no activity groups.");

        return new Catalog(ordered, byId);
    }

    private sealed class Draft
    {
        public int GroupId { get; set; } = -1;

        public List<int> LevelOperands { get; } = [];

        public List<int> AllowanceOperands { get; } = [];

        public List<List<int>> Slots { get; } = [];

        public List<int> RewardOperands { get; } = [];

        public DailyActivityGroupDefinition ToDefinition()
        {
            var level = LevelOperands.Count >= 2
                ? new LevelRange(LevelOperands[0], LevelOperands[1])
                : LevelRange.Any;

            var allowances = new List<DailyActivitySlotAllowance>();
            for (var i = 0; i + 2 < AllowanceOperands.Count; i += 3)
            {
                allowances.Add(new DailyActivitySlotAllowance(
                    new LevelRange(AllowanceOperands[i], AllowanceOperands[i + 1]),
                    AllowanceOperands[i + 2]));
            }

            // A slot block opens with its own index, then lists its quests.
            var slots = new List<DailyActivitySlot>();
            foreach (var operands in Slots)
            {
                if (operands.Count == 0)
                    continue;
                slots.Add(new DailyActivitySlot(operands[0], operands[1..]));
            }

            var rewards = new List<DailyActivityReward>();
            for (var i = 0; i + 3 < RewardOperands.Count; i += 4)
            {
                rewards.Add(new DailyActivityReward(
                    new LevelRange(RewardOperands[i], RewardOperands[i + 1]),
                    RewardOperands[i + 2],
                    RewardOperands[i + 3]));
            }

            return new DailyActivityGroupDefinition(GroupId, level, allowances, slots, rewards);
        }
    }

    private sealed record Catalog(
        IReadOnlyList<DailyActivityGroupDefinition> Ordered,
        Dictionary<int, DailyActivityGroupDefinition> ById);
}
