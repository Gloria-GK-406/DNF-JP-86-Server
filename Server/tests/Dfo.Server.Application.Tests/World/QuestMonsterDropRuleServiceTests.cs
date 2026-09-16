using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Tests.World;

public sealed class QuestMonsterDropRuleServiceTests
{
    private static readonly AccountId Account = new(7);
    private static readonly CharacterId Leader = new(70);

    [Fact]
    public async Task Exact_story_kill_draws_the_owned_task_item()
    {
        var fixture = Build(DungeonRunPurpose.QuestLinked(
            1779, DungeonQuestConnectionState.Active));

        var drops = await fixture.Service.DrawAsync(
            Kill(fixture.Run, monsterCode: 65601), CancellationToken.None);

        var drop = Assert.Single(drops);
        Assert.Equal(10149163, drop.ItemId);
        Assert.Equal(1, drop.Count);
        Assert.Equal(Leader, drop.OwnerCharacterId);
    }

    [Fact]
    public async Task Ordinary_run_never_injects_a_carried_story_quests_item()
    {
        var fixture = Build(DungeonRunPurpose.Ordinary);

        Assert.Empty(await fixture.Service.DrawAsync(
            Kill(fixture.Run, monsterCode: 65601), CancellationToken.None));
    }

    [Theory]
    [InlineData(99999, 145, 0)]
    [InlineData(65601, 144, 0)]
    [InlineData(65601, 145, 2)]
    public async Task Monster_dungeon_and_difficulty_scope_all_have_to_match(
        int monsterCode, int dungeonId, byte difficulty)
    {
        var fixture = Build(
            DungeonRunPurpose.QuestLinked(1779, DungeonQuestConnectionState.Active),
            dungeonId,
            difficulty,
            rewardDifficulty: difficulty == 2 ? 1 : -1);

        Assert.Empty(await fixture.Service.DrawAsync(
            Kill(fixture.Run, monsterCode), CancellationToken.None));
    }

    [Fact]
    public async Task Required_count_caps_the_drop_even_when_archive_max_is_larger()
    {
        var fixture = Build(
            DungeonRunPurpose.QuestLinked(1779, DungeonQuestConnectionState.Active),
            held: 14,
            maxStack: 99,
            rewardCount: 3);

        var drop = Assert.Single(await fixture.Service.DrawAsync(
            Kill(fixture.Run, monsterCode: 65601), CancellationToken.None));

        Assert.Equal(1, drop.Count);
    }

    [Fact]
    public async Task Quest_that_is_no_longer_carried_drops_nothing()
    {
        var fixture = Build(
            DungeonRunPurpose.QuestLinked(1779, DungeonQuestConnectionState.Active),
            carriesQuest: false);

        Assert.Empty(await fixture.Service.DrawAsync(
            Kill(fixture.Run, monsterCode: 65601), CancellationToken.None));
    }

    private static Fixture Build(
        DungeonRunPurpose purpose,
        int dungeonId = 145,
        byte difficulty = 0,
        int held = 0,
        int maxStack = 15,
        int rewardCount = 1,
        int rewardDifficulty = -1,
        bool carriesQuest = true)
    {
        var definition = Definition(rewardCount, maxStack, rewardDifficulty);
        var logs = new RecordingQuestLogs();
        if (carriesQuest)
            logs.Carried[Leader.Value] = [new CarriedQuest(0, 1779, 15)];

        var containers = new CharacterContainerDomainService();
        containers.Register(Leader, new Bag(10149163, held));
        var service = new QuestMonsterDropRuleService(
            new QuestRules(definition), logs, containers);
        var runs = new DungeonRunDomainService();
        var run = runs.BeginParty(
            [new DungeonRunParticipant(Account, Leader, 0, IsLeader: true)],
            Layout(dungeonId, purpose),
            difficulty);
        return new Fixture(service, run);
    }

    private static MonsterKill Kill(DungeonRunState run, int monsterCode) =>
        new(Leader, run, 30001, 5, 0, 1, monsterCode);

    private static QuestDefinition Definition(
        int rewardCount, int maxStack, int rewardDifficulty) =>
        new(
            1779, "quest", null, null, null, null, null, null, [], "seeking", "",
            null, null, null, null, null, null, null, [], [], [], [],
            SeekingItems: [new SeekingItemRequirement(10149163, 15)],
            MonsterRewardItems:
            [new QuestMonsterRewardItem(
                65601, 145, rewardDifficulty, 10149163, rewardCount, 100, maxStack)]);

    private static DungeonLayout Layout(int dungeonId, DungeonRunPurpose purpose) =>
        new(
            dungeonId,
            0,
            1,
            1,
            new MazeCell(0, 0),
            new MazeCell(0, 0),
            1,
            123,
            new Dictionary<MazeCell, int> { [new MazeCell(0, 0)] = 1 },
            Purpose: purpose);

    private sealed record Fixture(
        QuestMonsterDropRuleService Service,
        DungeonRunState Run);

    private sealed class QuestRules(QuestDefinition definition) : IQuestContentRuleService
    {
        public ValueTask<IReadOnlyList<QuestDefinition>> AllAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<QuestDefinition>>([definition]);

        public ValueTask<QuestDefinition?> FindAsync(
            int questId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<QuestDefinition?>(questId == definition.QuestId ? definition : null);

        public ValueTask<IReadOnlyList<TrainingQuestAvailability>> TrainingAvailabilityAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<TrainingQuestAvailability>>([]);

        public QuestParameters Parameters() => throw new NotSupportedException();
    }

    private sealed class Bag : IItemContainer
    {
        private readonly IReadOnlyList<ContainerSlotItem> items;

        public Bag(int itemId, int count) =>
            this.items = count <= 0
                ? []
                : [new ContainerSlotItem(
                    0,
                    StackableItemCore.Create(1, itemId, count),
                    ItemWorldClass.Interchangeable)];

        public ContainerId Id => new("quest-test-bag");
        public InventoryContainerType Type => InventoryContainerType.Main;
        public ContainerShape Shape => new(Type, 56);
        public bool IsRemovable => false;

        public ValueTask<IReadOnlyList<ContainerSlotItem>> ReadAllAsync(
            CancellationToken cancellationToken) => ValueTask.FromResult(this.items);

        public ValueTask<ContainerSlotItem?> ReadSlotAsync(int slotIndex, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public ValueTask<ContainerSlotPair> ReadSlotsAsync(int firstSlotIndex, int secondSlotIndex, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public ValueTask<IReadOnlyList<InventoryItemState>> SnapshotAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public ValueTask PutAsync(int slotIndex, InventoryItemCoreState core, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public ValueTask ClearAsync(int slotIndex, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public ValueTask<ItemPlacementOutcome> AddAsync(int itemId, int count, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public ValueTask<ItemPlacementOutcome> RefillAsync(ItemRefill refill, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public ValueTask<ItemMoveOutcome> MoveToAsync(int fromSlotIndex, int toSlotIndex, int count, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
