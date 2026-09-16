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

    private static QuestDefinition Definition() =>
        new(
            1779, "quest", null, null, null, null, null, null, [], "seeking", "",
            null, null, null, null, null, null, null, [], [], [], [],
            SeekingItems: [new SeekingItemRequirement(10149163, 15)]);

    private sealed class Rules(QuestDefinition definition) : IQuestContentRuleService
    {
        public ValueTask<IReadOnlyList<QuestDefinition>> AllAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<QuestDefinition>>([definition]);
        public ValueTask<QuestDefinition?> FindAsync(int questId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<QuestDefinition?>(questId == definition.QuestId ? definition : null);
        public ValueTask<IReadOnlyList<TrainingQuestAvailability>> TrainingAvailabilityAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<TrainingQuestAvailability>>([]);
        public QuestParameters Parameters() => throw new NotSupportedException();
    }

    private sealed class Bag : IItemContainer
    {
        private readonly IReadOnlyList<ContainerSlotItem> items;
        public Bag(int itemId, int count) => this.items = count <= 0
            ? []
            : [new ContainerSlotItem(0, StackableItemCore.Create(1, itemId, count), ItemWorldClass.Interchangeable)];
        public ContainerId Id => new("seeking-test-bag");
        public InventoryContainerType Type => InventoryContainerType.Main;
        public ContainerShape Shape => new(Type, 56);
        public bool IsRemovable => false;
        public ValueTask<IReadOnlyList<ContainerSlotItem>> ReadAllAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(this.items);
        public ValueTask<ContainerSlotItem?> ReadSlotAsync(int slotIndex, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ContainerSlotPair> ReadSlotsAsync(int firstSlotIndex, int secondSlotIndex, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<InventoryItemState>> SnapshotAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask PutAsync(int slotIndex, InventoryItemCoreState core, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask ClearAsync(int slotIndex, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ItemPlacementOutcome> AddAsync(int itemId, int count, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ItemPlacementOutcome> RefillAsync(ItemRefill refill, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ItemMoveOutcome> MoveToAsync(int fromSlotIndex, int toSlotIndex, int count, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
