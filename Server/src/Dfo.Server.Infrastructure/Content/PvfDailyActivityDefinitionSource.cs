using Dfo.Pvf.Schema;
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
internal sealed class PvfDailyActivityDefinitionSource : IDailyActivityDefinitionSource, Dfo.Server.Abstractions.Content.IContentWarmup
{
    private static class Fields
    {
        internal static readonly PvfField<int> GroupField = PvfField.Int32("[group]", optional: true);
        internal static readonly PvfField<bool> CloseGroupField = PvfField.Marker("[/group]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> LevelField = PvfField.Int32List("[level]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> SlotNumTableField = PvfField.Int32List("[slot num table]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> SlotField = PvfField.Int32List("[slot]", optional: true);
        internal static readonly PvfField<bool> CloseSlotField = PvfField.Marker("[/slot]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> RewardTableField = PvfField.Int32List("[reward table]", optional: true);
    }

    private const string TablePath = "etc/dailychallengetable.etc";

    private readonly PvfArchiveProvider _provider;
    private readonly Lock _gate = new();
    private Catalog? _catalog;

    public PvfDailyActivityDefinitionSource(PvfArchiveProvider provider) => _provider = provider;

    public string Name => "daily-activities";

    public ValueTask<int> WarmAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Load().Ordered.Count);

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

        var ordered = new List<DailyActivityGroupDefinition>();
        var byId = new Dictionary<int, DailyActivityGroupDefinition>();
        Draft? draft = null;
        var slotOpen = false;
        foreach (var section in PvfDocument.Open(archive, TablePath).Sections)
        {
            if (Fields.GroupField.Matches(section.Name))
            {
                var id = Fields.GroupField.Decode(archive, section.Values, TablePath);
                if (draft is not null) throw new InvalidDataException($"{TablePath}: nested or unclosed group.");
                draft = new Draft { GroupId = id };
            }
            else if (Fields.CloseGroupField.Matches(section.Name))
            {
                Fields.CloseGroupField.Decode(archive, section.Values, TablePath);
                if (draft is null || slotOpen) throw new InvalidDataException($"{TablePath}: group close without opening or before slot close.");
                var definition = draft.ToDefinition();
                ordered.Add(definition); byId.TryAdd(definition.GroupId, definition); draft = null;
            }
            else if (Fields.LevelField.Matches(section.Name))
            {
                var values = Fields.LevelField.Decode(archive, section.Values, TablePath);
                if (values.Count != 2) throw new InvalidDataException($"{TablePath}: {Fields.LevelField.Name} requires two level bounds.");
                RequireDraft(draft).LevelOperands.AddRange(values);
            }
            else if (Fields.SlotNumTableField.Matches(section.Name))
            {
                var values = Fields.SlotNumTableField.Decode(archive, section.Values, TablePath);
                if (values.Count % 3 != 0) throw new InvalidDataException($"{TablePath}: {Fields.SlotNumTableField.Name} requires level/level/count triples.");
                RequireDraft(draft).AllowanceOperands.AddRange(values);
            }
            else if (Fields.SlotField.Matches(section.Name))
            {
                var values = Fields.SlotField.Decode(archive, section.Values, TablePath);
                if (values.Count == 0 || slotOpen) throw new InvalidDataException($"{TablePath}: {Fields.SlotField.Name} requires index and a non-nested slot.");
                RequireDraft(draft).Slots.Add(values.ToList()); slotOpen = true;
            }
            else if (Fields.CloseSlotField.Matches(section.Name))
            {
                Fields.CloseSlotField.Decode(archive, section.Values, TablePath);
                if (!slotOpen) throw new InvalidDataException($"{TablePath}: slot close without opening.");
                slotOpen = false;
            }
            else if (Fields.RewardTableField.Matches(section.Name))
            {
                var values = Fields.RewardTableField.Decode(archive, section.Values, TablePath);
                if (values.Count % 4 != 0) throw new InvalidDataException($"{TablePath}: {Fields.RewardTableField.Name} requires level/level/item/count quadruples.");
                RequireDraft(draft).RewardOperands.AddRange(values);
            }
        }
        if (draft is not null || slotOpen) throw new InvalidDataException($"{TablePath}: unclosed daily activity group or slot.");

        if (ordered.Count == 0)
            throw new InvalidOperationException($"{TablePath} declared no activity groups.");

        return new Catalog(ordered, byId);
    }

    private static Draft RequireDraft(Draft? draft) => draft
        ?? throw new InvalidDataException($"{TablePath}: daily activity field outside a group.");

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
