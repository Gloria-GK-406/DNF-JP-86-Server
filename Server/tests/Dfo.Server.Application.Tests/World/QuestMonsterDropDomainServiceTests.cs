using System.Collections.Immutable;
using Dfo.Server.Data.World;
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

public sealed class QuestMonsterDropDomainServiceTests
{
    private static readonly AccountId Account = new(7);
    private static readonly CharacterId Leader = new(70);
    private static readonly CharacterId Member = new(71);

    [Fact]
    public async Task Exact_story_kill_draws_the_owned_task_item()
    {
        var fixture = Build(DungeonRunPurpose.QuestLinked(
            1779, DungeonQuestConnectionState.Active));

        var drops = await fixture.Service.DrawAsync(
            Kill(fixture.Run, monsterCode: 65601), new MonsterDropParticipant(Account, Leader), CancellationToken.None);

        var drop = Assert.Single(drops);
        Assert.Equal(10149163, drop.ItemId);
        Assert.Equal(1, drop.Count);
        Assert.Equal(Leader, drop.OwnerCharacterId);
        Assert.Equal(RolledDropDelivery.DirectQuestItem, drop.Delivery);
        Assert.Equal(1779, drop.QuestId);
    }

    [Fact]
    public async Task Ordinary_run_still_resolves_a_carried_quest_item_when_the_source_matches()
    {
        var fixture = Build(DungeonRunPurpose.Ordinary);

        var drop = Assert.Single(await fixture.Service.DrawAsync(
            Kill(fixture.Run, monsterCode: 65601), new MonsterDropParticipant(Account, Leader), CancellationToken.None));
        Assert.Equal(10149163, drop.ItemId);
        Assert.Equal(Leader, drop.OwnerCharacterId);
    }

    [Theory]
    [InlineData(99999, 145, DungeonDifficulty.Normal)]
    [InlineData(65601, 144, DungeonDifficulty.Normal)]
    [InlineData(65601, 145, DungeonDifficulty.Tier2)]
    public async Task Monster_dungeon_and_difficulty_scope_all_have_to_match(
        int monsterCode, int dungeonId, DungeonDifficulty difficulty)
    {
        var fixture = Build(
            DungeonRunPurpose.QuestLinked(1779, DungeonQuestConnectionState.Active),
            dungeonId,
            difficulty,
            rewardDifficulty: difficulty == DungeonDifficulty.Tier2 ? 1 : -1);

        Assert.Empty(await fixture.Service.DrawAsync(
            Kill(fixture.Run, monsterCode), new MonsterDropParticipant(Account, Leader), CancellationToken.None));
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
            Kill(fixture.Run, monsterCode: 65601), new MonsterDropParticipant(Account, Leader), CancellationToken.None));

        Assert.Equal(1, drop.Count);
    }

    [Fact]
    public async Task Quest_that_is_no_longer_carried_drops_nothing()
    {
        var fixture = Build(
            DungeonRunPurpose.QuestLinked(1779, DungeonQuestConnectionState.Active),
            carriesQuest: false);

        Assert.Empty(await fixture.Service.DrawAsync(
            Kill(fixture.Run, monsterCode: 65601), new MonsterDropParticipant(Account, Leader), CancellationToken.None));
    }

    [Fact]
    public async Task Each_participant_with_the_quest_gets_an_independent_direct_item()
    {
        var fixture = Build(DungeonRunPurpose.Ordinary, memberCarriesQuest: true);

        var drops = await fixture.Service.DrawAsync(
            Kill(fixture.Run, monsterCode: 65601), new MonsterDropParticipant(Account, Leader), CancellationToken.None);

        Assert.Equal(Leader, Assert.Single(drops).OwnerCharacterId);
        var memberDrops = await fixture.Service.DrawAsync(Kill(fixture.Run, 65601),
            new MonsterDropParticipant(new AccountId(8), Member), CancellationToken.None);
        Assert.Equal(Member, Assert.Single(memberDrops).OwnerCharacterId);
        Assert.All(drops, drop => Assert.Equal(RolledDropDelivery.DirectQuestItem, drop.Delivery));
    }

    [Fact]
    public async Task Reusing_identity_context_reads_current_quest_and_inventory_facts()
    {
        var fixture = Build(DungeonRunPurpose.Ordinary);
        var request = Kill(fixture.Run, 65601);
        Assert.Single(await fixture.Service.DrawAsync(request, new MonsterDropParticipant(Account, Leader), CancellationToken.None));

        fixture.Logs.Carried[Leader.Value] = [];
        Assert.Empty(await fixture.Service.DrawAsync(request, new MonsterDropParticipant(Account, Leader), CancellationToken.None));

        fixture.Logs.Carried[Leader.Value] = [new CarriedQuest(0, 1779, 15)];
        fixture.Bag.SetCount(10149163, 15);
        Assert.Empty(await fixture.Service.DrawAsync(request, new MonsterDropParticipant(Account, Leader), CancellationToken.None));
    }

    private static Fixture Build(
        DungeonRunPurpose purpose,
        int dungeonId = 145,
        DungeonDifficulty difficulty = 0,
        int held = 0,
        int maxStack = 15,
        int rewardCount = 1,
        int rewardDifficulty = -1,
        bool carriesQuest = true,
        bool memberCarriesQuest = false)
    {
        var definition = Definition(rewardCount, maxStack, rewardDifficulty);
        var logs = new RecordingQuestLogs();
        if (carriesQuest)
            logs.Carried[Leader.Value] = [new CarriedQuest(0, 1779, 15)];
        if (memberCarriesQuest)
            logs.Carried[Member.Value] = [new CarriedQuest(0, 1779, 15)];

        var containers = new CharacterContainerDomainService();
        var bag = new Bag(10149163, held);
        containers.Register(Leader, bag);
        if (memberCarriesQuest)
            containers.Register(Member, new Bag(10149163, 0));
        var service = new QuestMonsterDropDomainService(
            new QuestRules(definition), logs, containers);
        var layout = Layout(dungeonId, purpose);
        var run = new DungeonCellCreationContext(
            DungeonRunId.New(),
            layout,
            layout.StartCell,
            layout.StartRoomMapId,
            difficulty,
            DateTimeOffset.UnixEpoch,
            memberCarriesQuest
                ? [new DungeonRunParticipant(Account, Leader, 0, IsLeader: true),
                    new DungeonRunParticipant(new AccountId(8), Member, 1, IsLeader: false)]
                : [new DungeonRunParticipant(Account, Leader, 0, IsLeader: true)]);
        return new Fixture(service, run, logs, bag);
    }

    private static MonsterDropRequest Kill(DungeonCellCreationContext run, int monsterCode) =>
        new(monsterCode, new MonsterDropContext(
            run.RunId, run.Layout.DungeonId, run.Layout.Seed, run.Cell, run.MapId,
            run.Difficulty, 30001, 5, 0, Leader,
            run.Participants.Select(participant => new MonsterDropParticipant(
                participant.AccountId, participant.CharacterId)).ToImmutableArray()));

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
        QuestMonsterDropDomainService Service,
        DungeonCellCreationContext Run,
        RecordingQuestLogs Logs,
        Bag Bag);

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
        private IReadOnlyList<ContainerSlotItem> items = [];

        public Bag(int itemId, int count) => this.SetCount(itemId, count);

        public void SetCount(int itemId, int count) =>
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
