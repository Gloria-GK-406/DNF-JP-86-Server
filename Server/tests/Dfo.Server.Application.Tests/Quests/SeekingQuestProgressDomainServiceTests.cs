using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Quests;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Tests.Quests;

public sealed class SeekingQuestProgressDomainServiceTests
{
    private static readonly AccountId Account = new(1);
    private static readonly CharacterId Character = new(60);

    [Theory]
    [InlineData(0, 15)]
    [InlineData(1, 14)]
    [InlineData(15, 0)]
    [InlineData(20, 0)]
    public async Task Trigger_is_the_remaining_count_derived_from_the_current_bag(
        int held,
        int expected)
    {
        var logs = new RecordingQuestLogs
        {
            Carried = { [Character.Value] = [new CarriedQuest(0, 1779, 15)] },
        };
        var containers = new CharacterContainerDomainService();
        containers.Register(Character, new Bag(10149163, held));
        var service = new SeekingQuestProgressDomainService(
            new Rules(Definition()),
            logs,
            containers,
            CurrentCharacterStub.Domain(new CurrentCharacterStub { Id = Character }));

        var result = await service.SynchronizeAsync(Account, Character, CancellationToken.None);

        if (expected == 15)
        {
            Assert.Null(result);
            Assert.Empty(logs.ProgressWrites);
        }
        else
        {
            Assert.NotNull(result);
            Assert.Equal(expected, Assert.Single(result.Quests).TriggerValue);
            Assert.Equal(expected, Assert.Single(logs.ProgressWrites).TriggerValue);
        }
    }

    [Fact]
    public async Task Synchronization_returns_the_complete_carried_progress_not_only_the_changed_row()
    {
        var logs = new RecordingQuestLogs
        {
            Carried =
            {
                [Character.Value] =
                [new CarriedQuest(0, 1779, 15), new CarriedQuest(1, 2000, 7)],
            },
        };
        var containers = new CharacterContainerDomainService();
        containers.Register(Character, new Bag(10149163, 15));
        var service = new SeekingQuestProgressDomainService(
            new Rules(Definition()),
            logs,
            containers,
            CurrentCharacterStub.Domain(new CurrentCharacterStub { Id = Character }));

        var result = await service.SynchronizeAsync(Account, Character, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal([(1779, 0), (2000, 7)],
            result.Quests.Select(quest => (quest.QuestId, quest.TriggerValue)));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(14, true)]
    [InlineData(15, false)]
    [InlineData(20, false)]
    public async Task Outstanding_materials_use_holdings_ignore_gold_and_do_not_write_progress(int held, bool needed)
    {
        var logs = new RecordingQuestLogs { Carried = { [Character.Value] = [new(0, 1779, 0)] } };
        var containers = new CharacterContainerDomainService();
        containers.Register(Character, new Bag(10149163, held));
        var definition = Definition() with { SeekingItems = [new(10149163, 15), new(10149163, 10), new(0, 100)] };
        var service = new QuestMaterialRequirementDomainService(new Rules(definition), logs, containers);
        var result = await service.GetOutstandingMaterialIdsAsync(Account, Character, default);
        Assert.Equal(needed, result.Contains(10149163));
        Assert.Equal(needed ? 1 : 0, result.Count);
        Assert.Empty(logs.ProgressWrites);
        logs.Carried[Character.Value] = [];
        Assert.Empty(await service.GetOutstandingMaterialIdsAsync(Account, Character, default));
    }

    [Fact]
    public async Task Outstanding_union_includes_only_carried_unmet_requirements_without_summing_across_quests()
    {
        var logs = new RecordingQuestLogs { Carried = { [Character.Value] = [new(0, 1779, 15), new(1, 1780, 0)] } };
        var containers = new CharacterContainerDomainService();
        containers.Register(Character, new Bag(10149163, 15));
        var first = Definition();
        var second = first with { QuestId = 1780, SeekingItems = [new(10149163, 20), new(3205, 5)] };
        var unaccepted = first with { QuestId = 1781, SeekingItems = [new(999, 10)] };
        var service = new QuestMaterialRequirementDomainService(new Rules(first, second, unaccepted), logs, containers);
        Assert.Equal(new[] { 3205, 10149163 }, (await service.GetOutstandingMaterialIdsAsync(Account, Character, default)).Order());
        logs.Carried[Character.Value] = [new(0, 1779, 15)];
        Assert.Empty(await service.GetOutstandingMaterialIdsAsync(Account, Character, default));
        Assert.Empty(logs.ProgressWrites);
    }

    private static QuestDefinition Definition() =>
        new(
            1779, "quest", null, null, null, null, null, null, [], "seeking", "",
            null, null, null, null, null, null, null, [], [], [], [],
            SeekingItems: [new SeekingItemRequirement(10149163, 15)]);

    private sealed class Rules(params QuestDefinition[] definitions) : IQuestContentRuleService
    {
        public ValueTask<IReadOnlyList<QuestDefinition>> AllAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<QuestDefinition>>(definitions);
        public ValueTask<QuestDefinition?> FindAsync(int questId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<QuestDefinition?>(definitions.FirstOrDefault(q => q.QuestId == questId));
        public ValueTask<IReadOnlyList<TrainingQuestAvailability>> TrainingAvailabilityAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<TrainingQuestAvailability>>([]);
        public QuestParameters Parameters() => throw new NotSupportedException();
    }

    private sealed class Bag : IItemContainer
    {
        private readonly IReadOnlyList<ItemReference> items;
        public Bag(int itemId, int count) => this.items = count <= 0
            ? []
            : [new ItemReference(this, Id, 0, 0, ItemSnapshot.FromCore(StackableItemCore.Create(1, itemId, count), true))];
        public ContainerId Id => new("seeking-test-bag");
        public InventoryContainerType Type => InventoryContainerType.Main;
        public ContainerShape Shape => new(Type, 56);
        public bool IsRemovable => false;
        public ValueTask<IReadOnlyList<ItemReference>> GetAllAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(this.items);
        public ValueTask<ItemReference?> GetAsync(int slotIndex, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<InventoryItemState>> SnapshotAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ItemReference> PutAsync(int slotIndex, ItemRecord record, CancellationToken ct) =>
            throw new NotSupportedException("This fixture only adds through AddAsync and projects its result.");
        public ValueTask<ItemRecord> TakeAsync(ItemReference reference, int? count, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<ItemReference> UpdateAsync(ItemReference reference, Func<ItemSnapshot, ItemSnapshot> change, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask FlushAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask<ItemPlacementOutcome> AddAsync(int itemId, int count, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ItemPlacementOutcome> RefillAsync(ItemRefill refill, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ItemMoveOutcome> MoveToAsync(int fromSlotIndex, int toSlotIndex, int count, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
