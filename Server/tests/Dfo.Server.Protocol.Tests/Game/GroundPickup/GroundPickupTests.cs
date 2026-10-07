using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.GroundPickup;
using Dfo.Server.Protocol.Game;

namespace Dfo.Server.Protocol.Tests.Game.GroundPickup;

/// <summary>
/// Taking something off a room's floor, whichever way it got there.
/// </summary>
/// <remarks>
/// Two kinds of thing lie on a floor and only one of them can be read back out of the archive. A
/// room's breakable objects are declared and are found by looking them up again; what a monster
/// dropped exists only in the run, and until 2026-08-30 the pick-up path could not see it at all.
/// </remarks>
public sealed class GroundPickupTests
{
    private static readonly CharacterId Character = new(1);
    private static readonly AccountId Account = new(1);
    private static readonly CharacterId Helper = new(2);
    private static readonly AccountId HelperAccount = new(2);

    /// <summary>The 铁脊蛇矛, which a monster may drop and the tutorial's box may not.</summary>
    private const int Weapon = 114000088;

    /// <summary>What the tutorial's second room declares in its box.</summary>
    private const int Shoulder = 14400;

    private static readonly DungeonLayout Layout = new(
        DungeonId: 144,
        MazeIndex: 0,
        Width: 2,
        Height: 1,
        StartCell: new MazeCell(0, 0),
        BossCell: new MazeCell(1, 0),
        StartRoomMapId: 8153,
        Seed: 0x0BADF00D,
        Cells: new Dictionary<MazeCell, DungeonCellDefinition> {
            [new MazeCell(0, 0)] = DungeonCellDefinition.Fixed(8153),
            [new MazeCell(1, 0)] = DungeonCellDefinition.Fixed(8154),
        },
        BasisLevel: 86);

    private static (PickUpGroundDropCommandHandler Handler, DungeonRunDomainService Runs,
        FakeContainer Bag, CarriedGold Carried) Build()
        => Build(new NoSeekingProgress());

    private static (PickUpGroundDropCommandHandler Handler, DungeonRunDomainService Runs,
        FakeContainer Bag, CarriedGold Carried) Build(
        ISeekingQuestProgressDomainService seeking)
    {
        var runs = new DungeonRunDomainService();
        var bag = new FakeContainer();
        var carried = new CarriedGold(0);
        var handler = new PickUpGroundDropCommandHandler(
            runs,
            new GroundPickupDomainService(),
            new StubContainers(bag),
            new OnePlayingCharacter(carried),
            new QuestProgressFromSeeking(seeking),
            new Dfo.Server.Protocol.Tests.Support.OrdinaryItemGrantDomainService());
        return (handler, runs, bag, carried);
    }

    private static ValueTask<GroundPickupResult> PickUp(
        PickUpGroundDropCommandHandler handler, ushort number) =>
        handler.HandleAsync(
            default!,
            new PickUpGroundDropCommand(Account, Character, number),
            CancellationToken.None);

    /// <summary>What 铁脊蛇矛's own file declares.</summary>
    private const ushort WeaponDurability = 45;

    private static RolledDrop TheWeapon() =>
        new(Weapon, 1, DropRarity.Epic, DropItemKind.Equipment, WeaponDurability);

    private static DungeonRunState BeginWithItems(
        DungeonRunDomainService runs,
        IReadOnlyList<DungeonGroundItem> items,
        IReadOnlyList<DungeonRunParticipant>? participants = null)
    {
        participants ??=
        [
            new DungeonRunParticipant(Account, Character, Slot: 0, IsLeader: true),
        ];
        return runs.PreparePartyEntry(
            participants,
            Layout,
            difficulty: 0,
            new DungeonCellSeed(
                Layout.StartCell,
                Layout.StartRoomMapId,
                FirstActorSequence: 1,
                Actors: [],
                GroundItems: items),
            DungeonRunId.New(),
            DateTimeOffset.UtcNow).Run;
    }

    private static DungeonGroundItem MonsterItem(
        ushort number,
        ushort actorSequence,
        RolledDrop drop) =>
        new(
            number,
            drop.ItemId,
            drop.Count,
            drop.Durability,
            drop.Kind == DropItemKind.Equipment,
            DungeonGroundItemSource.FromActor(actorSequence),
            drop.Rarity,
            drop.Kind,
            drop.OwnerCharacterId);

    private static DungeonGroundItem DeclaredItem(ushort number, DungeonRoomDrop drop) =>
        new(
            number,
            drop.ItemId,
            Math.Max(1, drop.Count),
            drop.Durability,
            IsEquipment: drop.Durability > 0,
            DungeonGroundItemSource.Declared(drop.ObjectIndex));

    /// <summary>A piece a monster dropped can be taken by the number it was given.</summary>
    [Fact]
    public async Task A_piece_a_monster_dropped_can_be_taken_by_its_number()
    {
        var (handler, runs, bag, carried) = Build();
        var dropped = MonsterItem(1, 30001, TheWeapon());
        _ = BeginWithItems(runs, [dropped]);

        var result = await PickUp(handler, dropped.Number);

        Assert.Equal(GroundPickupStatus.PickedUp, result.Status);
        Assert.Equal(Weapon, result.ItemId);
        Assert.Equal(Weapon, bag.Slots[result.SlotIndex].ItemId);

        // Not broken. A piece banked at zero durability is a broken one, and nothing on the wire
        // would have said so -- the kill's frame carries no durability field at all.
        Assert.Equal(WeaponDurability, bag.Slots[result.SlotIndex].Durability);
    }

    [Fact]
    public async Task A_quest_owned_piece_refuses_another_member_without_consuming_the_floor()
    {
        var (handler, runs, bag, _) = Build();
        DungeonRunParticipant[] party =
        [
            new DungeonRunParticipant(Account, Character, 0, IsLeader: true),
            new DungeonRunParticipant(HelperAccount, Helper, 1, IsLeader: false),
        ];
        var dropped = MonsterItem(
            1, 30001, TheWeapon() with { OwnerCharacterId = Character });
        _ = BeginWithItems(runs, [dropped], party);

        var refused = await handler.HandleAsync(
            default!,
            new PickUpGroundDropCommand(HelperAccount, Helper, dropped.Number),
            CancellationToken.None);

        Assert.Equal(GroundPickupStatus.NotOwner, refused.Status);
        Assert.Empty(bag.Slots);
        Assert.NotNull(runs.Find(Character)!.CurrentCellState.FindGroundItem(dropped.Number));
        Assert.Equal(GroundPickupStatus.PickedUp, (await PickUp(handler, dropped.Number)).Status);
    }

    [Fact]
    public async Task Item_pickup_derives_quest_progress_before_committing_the_floor_change()
    {
        var progress = new SeekingQuestProgressSynchronization(
            [new CarriedQuest(0, 1779, 14)]);
        var seeking = new RecordingSeekingProgress(progress);
        var (handler, runs, _, _) = Build(seeking);
        var dropped = MonsterItem(1, 30001, TheWeapon());
        _ = BeginWithItems(runs, [dropped]);

        var result = await PickUp(handler, dropped.Number);

        Assert.Equal(progress.Quests, result.QuestProgress?.Quests);
        Assert.Equal(1, seeking.Calls);
        Assert.NotNull(result.Change);
    }

    [Fact]
    public async Task Failed_quest_progress_does_not_commit_the_shared_floor_change()
    {
        var (handler, runs, _, _) = Build(new ThrowingSeekingProgress());
        var dropped = MonsterItem(1, 30001, TheWeapon());
        _ = BeginWithItems(runs, [dropped]);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await PickUp(handler, dropped.Number));

        Assert.NotNull(runs.Find(Character)!.CurrentCellState.FindGroundItem(dropped.Number));
    }

    /// <summary>And only once.</summary>
    /// <remarks>
    /// The claim is what makes the second request fail. Without it a player could ask twice and be
    /// handed the piece twice, which for a dropped piece is worse than for a declared one: there
    /// is no room description to disagree with, so nothing else would ever notice.
    /// </remarks>
    [Fact]
    public async Task The_same_number_is_not_given_twice()
    {
        var (handler, runs, _, carried) = Build();
        var dropped = MonsterItem(1, 30001, TheWeapon());
        _ = BeginWithItems(runs, [dropped]);

        Assert.Equal(GroundPickupStatus.PickedUp, (await PickUp(handler, dropped.Number)).Status);

        var second = await PickUp(handler, dropped.Number);

        Assert.Equal(GroundPickupStatus.NothingThere, second.Status);
    }

    [Fact]
    public async Task A_failed_persistent_insert_releases_the_floor_reservation_for_one_retry()
    {
        var (handler, runs, bag, _) = Build();
        var dropped = MonsterItem(1, 30001, TheWeapon());
        _ = BeginWithItems(runs, [dropped]);
        bag.FailNextAdd = true;

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await PickUp(handler, dropped.Number));
        Assert.NotNull(runs.Find(Character)!.CurrentCellState.FindGroundItem(dropped.Number));

        Assert.Equal(GroundPickupStatus.PickedUp, (await PickUp(handler, dropped.Number)).Status);
        Assert.Equal(GroundPickupStatus.NothingThere, (await PickUp(handler, dropped.Number)).Status);
        Assert.Single(bag.Slots);
    }

    /// <summary>
    /// A room's own pieces keep their numbers and a dropped piece takes the next one.
    /// </summary>
    /// <remarks>
    /// The collision this numbering exists to prevent: the room declares piece one, so a dropped
    /// piece must not also be piece one. Both are asked for here, and each has to answer with what
    /// it actually is.
    /// </remarks>
    [Fact]
    public async Task A_declared_piece_and_a_dropped_one_do_not_answer_to_one_number()
    {
        var (handler, runs, _, carried) = Build();
        var declared = DeclaredItem(1, new DungeonRoomDrop(0, Shoulder, 25));
        var dropped = MonsterItem(2, 30001, TheWeapon());
        _ = BeginWithItems(runs, [declared, dropped]);

        Assert.Equal(2, dropped.Number);
        Assert.Equal(Shoulder, (await PickUp(handler, 1)).ItemId);
        Assert.Equal(Weapon, (await PickUp(handler, 2)).ItemId);
    }

    /// <summary>
    /// Gold goes onto the character, not into a bag.
    /// </summary>
    /// <remarks>
    /// It lands on the character -- <c>character_stats.carried_gold</c> -- and is drawn from there
    /// into the main list's first virtual slot, which is where the client shows what a player is
    /// carrying. The account cargo is a different pot and is not touched by a floor pick-up.
    /// </remarks>
    [Fact]
    public async Task Gold_goes_onto_the_character()
    {
        var (handler, runs, bag, carried) = Build();
        var gold = MonsterItem(
            1,
            30001,
            new RolledDrop(0, 1357, DropRarity.Common, DropItemKind.Gold));
        _ = BeginWithItems(runs, [gold]);

        var result = await PickUp(handler, gold.Number);

        Assert.Equal(GroundPickupStatus.Banked, result.Status);
        Assert.Equal(1357, result.Amount);
        Assert.Equal(1357, result.Total);
        Assert.Equal(1357, carried.Total);

        // And it never touched the bag.
        Assert.Empty(bag.Slots);
    }

    /// <summary>The same coins are not paid twice.</summary>
    [Fact]
    public async Task Gold_is_paid_once()
    {
        var (handler, runs, _, carried) = Build();
        var gold = MonsterItem(
            1,
            30001,
            new RolledDrop(0, 44, DropRarity.Common, DropItemKind.Gold));
        _ = BeginWithItems(runs, [gold]);

        Assert.Equal(GroundPickupStatus.Banked, (await PickUp(handler, gold.Number)).Status);
        var second = await PickUp(handler, gold.Number);

        Assert.Equal(GroundPickupStatus.NothingThere, second.Status);
        Assert.Equal(44, carried.Total);
    }

    /// <summary>A number nothing was ever given is refused.</summary>
    [Fact]
    public async Task A_number_nothing_holds_is_refused()
    {
        var (handler, runs, _, carried) = Build();
        _ = BeginWithItems(runs, []);

        Assert.Equal(GroundPickupStatus.NothingThere, (await PickUp(handler, 9)).Status);
    }

    /// <summary>
    /// A dropped piece is not on the floor of the room next door.
    /// </summary>
    /// <remarks>
    /// The numbering is per room, so the same number means different things in two rooms and must
    /// not carry across. A maze may lead through one map twice for the same reason.
    /// </remarks>
    [Fact]
    public async Task A_dropped_piece_stays_in_the_room_it_fell_in()
    {
        var (handler, runs, _, carried) = Build();
        var dropped = MonsterItem(1, 30001, TheWeapon());
        var run = BeginWithItems(runs, [dropped]);

        var target = new MazeCell(1, 0);
        _ = runs.TryMove(
            Character,
            run.RunId,
            run.RoomVersion,
            target,
            new DungeonCellSeed(target, Layout.Cells[target].MapCandidates[0], 1, [], []));

        Assert.Equal(GroundPickupStatus.NothingThere, (await PickUp(handler, dropped.Number)).Status);
    }


    /// <summary>
    /// The piece in the bag carries the value the kill announced for it.
    /// </summary>
    /// <remarks>
    /// The client is told a 32-bit value with each piece that lands, and the piece the player ends
    /// up holding has to carry the same one -- that is what tells one piece of equipment from
    /// another. It is derived from the kill rather than from the piece's number, because every
    /// piece of one kill carries one value: the captured frames show a kill that dropped twice
    /// announcing the same value for both.
    /// <para>
    /// Deriving it from the drop's number instead would agree with itself everywhere and disagree
    /// with the frame, which is exactly what it did until this assertion existed.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_banked_piece_carries_the_value_its_kill_announced()
    {
        const ushort Actor = 30007;
        var (handler, runs, bag, carried) = Build();
        var dropped = MonsterItem(1, Actor, TheWeapon());
        var run = BeginWithItems(runs, [dropped]);

        var result = await PickUp(handler, dropped.Number);

        Assert.Equal(GroundPickupStatus.PickedUp, result.Status);
        Assert.Equal(
            (int)GameDropIdentity.ForKill(run, Actor),
            bag.Slots[result.SlotIndex].Value);
    }

    /// <summary>Two pieces of one kill carry one value; two kills carry two.</summary>
    [Fact]
    public async Task Every_piece_of_one_kill_carries_one_value()
    {
        var (handler, runs, bag, carried) = Build();
        DungeonGroundItem[] pair =
        [
            MonsterItem(1, 30007, TheWeapon()),
            MonsterItem(2, 30007, TheWeapon()),
        ];
        var other = MonsterItem(3, 30008, TheWeapon());
        _ = BeginWithItems(runs, [.. pair, other]);

        foreach (var piece in pair)
            await PickUp(handler, piece.Number);
        await PickUp(handler, other.Number);

        var values = bag.Slots.Values.Select(core => core.Value).ToList();
        Assert.Equal(3, values.Count);
        Assert.Equal(values[0], values[1]);
        Assert.NotEqual(values[0], values[2]);
    }

    /// <summary>
    /// A declared stack is banked at the count the room declares, not at one.
    /// </summary>
    /// <remarks>
    /// A deliberate change to an already-shipped path. The room's own description already
    /// announces that count to the client -- dungeon 144's boss room declares a material three at
    /// a time -- so banking one of it left the floor and the bag disagreeing about what the player
    /// had just picked up.
    /// </remarks>
    [Fact]
    public async Task A_declared_stack_is_banked_at_the_count_the_room_declares()
    {
        var (handler, runs, bag, carried) = Build();
        _ = BeginWithItems(
            runs,
            [DeclaredItem(1, new DungeonRoomDrop(0, 10088621, 0, Count: 3))]);

        var result = await PickUp(handler, 1);

        Assert.Equal(GroundPickupStatus.PickedUp, result.Status);
        Assert.Equal(3, bag.Counts[result.SlotIndex]);
    }


    [Fact]
    public async Task PickingUpACreaturePreservesItsAllocatedIdentityAndCompleteDetail()
    {
        var fixture = new Dfo.Server.Protocol.Tests.Support.MoveItemFixture();
        var physical = new Dfo.Server.Protocol.Tests.Support.LifecycleTestContainer(InventoryContainerType.Pet, Character, fixture);
        var bag = new BusinessItemContainer(BusinessInventorySpace.CreatureInventory, physical);
        var runs = new DungeonRunDomainService();
        var handler = new PickUpGroundDropCommandHandler(runs, new GroundPickupDomainService(),
            new StubContainers(physical), new OnePlayingCharacter(new CarriedGold(0)),
            new QuestProgressFromSeeking(new NoSeekingProgress()), new CreatureDropGrant(fixture, bag));
        const ushort actor = 30001;
        var run = BeginWithItems(runs, [MonsterItem(1, actor, new RolledDrop(600, 1,
            DropRarity.Common, DropItemKind.Equipment, 17))]);
        var item = await fixture.RunAsync(async () =>
        {
            Assert.Equal(GroundPickupStatus.PickedUp, (await PickUp(handler, 1)).Status);
            return (await bag.GetAsync(0, default))!.Snapshot;
        });
        Assert.NotNull(item.ItemUid);
        Assert.Equal(item.ItemUid.Value.Value, item.InstanceValue);
        Assert.NotEqual(checked((int)GameDropIdentity.ForKill(run, actor)), item.InstanceValue);
        Assert.Equal(17, item.Durability);
        Assert.Equal(27, item.CreatureDetail!.ProgressValue);
        Assert.Equal(3, item.CreatureDetail.FieldAfterValue);
        Assert.Equal(new byte[] { 65, 66 }, item.CreatureDetail.CreatureText);
        Assert.Equal(9, item.CreatureDetail.TailFlag);
    }

    private sealed class CreatureDropGrant(Dfo.Server.Protocol.Tests.Support.MoveItemFixture fixture,
        IBusinessItemContainer bag) : IItemGrantDomainService
    {
        public async ValueTask<BusinessItemAdditionResult> GrantAsync(AccountId account, CharacterId character,
            ICharacterContainerDomainService containers, int itemId, int count, CancellationToken ct, bool isDefault = false)
        {
            var record = await fixture.Session.CreateAsync(new ItemSnapshot
            {
                ItemId = itemId, LegacyKind = 5, Type = ItemTraits.Creature,
                CreatureDetail = new(100, 0, 27, 0, 0, 3, [65, 66], 9),
            }, ct);
            await bag.PutAsync(0, record, ct);
            return new(bag, new ItemPlacementOutcome.PutInEmptySlot(0, 1));
        }
    }

    private sealed class StubContainers(IItemContainer bag) : ICharacterContainerDomainService
    {
        public IItemContainer? FindContainer(CharacterId characterId, InventoryContainerType type, bool isDefault = false) =>
            bag;

        public void Register(CharacterId characterId, IItemContainer container) =>
            throw new NotSupportedException();

        public bool Remove(CharacterId characterId, ContainerId containerId) =>
            throw new NotSupportedException();

        public IReadOnlyList<IItemContainer> GetContainers(
            CharacterId characterId, InventoryContainerType type) => [bag];

        public IItemContainer GetContainer(CharacterId characterId, InventoryContainerType type, bool isDefault = false) =>
            bag;

        public bool UseContainer(CharacterId characterId, ContainerId containerId) =>
            throw new NotSupportedException();

        public void Clear(CharacterId characterId) => throw new NotSupportedException();
    }

    private sealed class NoSeekingProgress : ISeekingQuestProgressDomainService
    {
        public ValueTask<SeekingQuestProgressSynchronization?> SynchronizeAsync(
            AccountId accountId,
            CharacterId characterId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<SeekingQuestProgressSynchronization?>(null);
    }

    private sealed class RecordingSeekingProgress(
        SeekingQuestProgressSynchronization result) : ISeekingQuestProgressDomainService
    {
        public int Calls { get; private set; }

        public ValueTask<SeekingQuestProgressSynchronization?> SynchronizeAsync(
            AccountId accountId,
            CharacterId characterId,
            CancellationToken cancellationToken)
        {
            this.Calls++;
            return ValueTask.FromResult<SeekingQuestProgressSynchronization?>(result);
        }
    }

    private sealed class ThrowingSeekingProgress : ISeekingQuestProgressDomainService
    {
        public ValueTask<SeekingQuestProgressSynchronization?> SynchronizeAsync(
            AccountId accountId,
            CharacterId characterId,
            CancellationToken cancellationToken) =>
            throw new InvalidDataException("injected quest progress failure");
    }

    private sealed class QuestProgressFromSeeking(ISeekingQuestProgressDomainService seeking)
        : IQuestProgressDomainService
    {
        public async ValueTask<QuestProgressReport> ReportAsync(
            AccountId accountId, CharacterId characterId,
            IReadOnlyList<QuestProgressFact> facts, CancellationToken cancellationToken)
        {
            Assert.All(facts, fact => Assert.IsType<QuestHoldingsChanged>(fact));
            var result = await seeking.SynchronizeAsync(
                accountId, characterId, cancellationToken);
            return new QuestProgressReport(
                null, [], result is null ? null : new QuestProgressSnapshot(result.Quests));
        }
    }

    /// <summary>A bag that opens the next free slot and remembers what went in it.</summary>
    internal sealed class FakeContainer : IItemContainer, Dfo.Server.Protocol.Tests.Support.IItemGrantStub
    {
        public bool FailNextAdd { get; set; }

        private readonly Dictionary<int, long> generations = [];

        public Dictionary<int, InventoryItemCoreState> Slots { get; } = [];

        /// <summary>How many of each slot's item was handed over.</summary>
        public Dictionary<int, int> Counts { get; } = [];

        public ContainerId Id => new("bag");

        public InventoryContainerType Type => InventoryContainerType.Main;

        public ContainerShape Shape => new(InventoryContainerType.Main, 56);

        public bool IsRemovable => false;

        public ValueTask<ItemPlacementOutcome> GrantAsync(
            int itemId, int count, CancellationToken cancellationToken)
        {
            if (this.FailNextAdd)
            {
                this.FailNextAdd = false;
                throw new InvalidOperationException("injected persistent insert failure");
            }
            var slot = 9 + this.Slots.Count;
            this.Slots[slot] = StackableItemCore.Create(1, itemId, count);
            this.Counts[slot] = count;
            return ValueTask.FromResult<ItemPlacementOutcome>(
                new ItemPlacementOutcome.PutInEmptySlot(slot, count));
        }


        public ValueTask<ItemReference?> GetAsync(
            int slotIndex, CancellationToken cancellationToken) =>
            ValueTask.FromResult(
                this.Slots.TryGetValue(slotIndex, out var core)
                    ? new ItemReference(this, Id, slotIndex, generations.GetValueOrDefault(slotIndex), ItemSnapshot.FromCore(core, false, new ItemIdentity(slotIndex + 1)))
                    : null);

        public ValueTask<ItemReference> PutAsync(int slotIndex, ItemRecord record, CancellationToken ct) =>
            throw new NotSupportedException("This fixture uses injected grant outcomes and only projects their results.");


        public ValueTask<IReadOnlyList<ItemReference>> GetAllAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<ItemRecord> TakeAsync(ItemReference reference, int? count, CancellationToken ct) => throw new NotSupportedException();
        public async ValueTask<ItemReference> UpdateAsync(ItemReference reference, Func<ItemSnapshot, ItemSnapshot> change, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!reference.BelongsTo(this) || reference.ContainerId != Id
                || reference.Generation != generations.GetValueOrDefault(reference.StorageSlotIndex)
                || !Slots.ContainsKey(reference.StorageSlotIndex))
                throw new InvalidOperationException("The fixture reference is stale or foreign.");
            var next = change((await GetAsync(reference.StorageSlotIndex, ct))!.Snapshot);
            if (next.ItemId != reference.ItemId || next.ItemUid != reference.ItemUid
                || next.IsPile != reference.IsPile || next.Type != reference.Type)
                throw new InvalidOperationException("The fixture update cannot replace item identity.");
            Slots[reference.StorageSlotIndex] = next.ToCore();
            return (await GetAsync(reference.StorageSlotIndex, ct))!;
        }
        public ValueTask FlushAsync(CancellationToken ct) => ValueTask.CompletedTask;


    }
}
