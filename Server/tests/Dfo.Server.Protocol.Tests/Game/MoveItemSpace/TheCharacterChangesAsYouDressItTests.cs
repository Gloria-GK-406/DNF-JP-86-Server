using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.MoveItemSpace;
using Dfo.Server.Protocol.Game.SelectCharacter;
using Dfo.Server.Protocol.Tests.Game.SelectCharacter;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Tests.Game.TownProjection;
using Dfo.Server.Protocol.Tests.Game.Party;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Tests.Game.MoveItemSpace;

/// <summary>
/// What follows a move that changed what the character is wearing.
/// </summary>
/// <remarks>
/// The acknowledgement says where an item went and nothing about how the character now looks, so a
/// client that has been told once at character selection goes on drawing what it was told then.
/// Observed live on 2026-09-08: eleven costumes went on, the acknowledgements all reported success,
/// the store held them, and the character did not change until the owner left to character
/// selection and came back.
/// <para>
/// The frame that answers this already exists and is already sent once, at entry. What is new is
/// sending it again when a move changed what it would say. The current view reads the store on
/// demand; the entry-time fixture is deliberately stale so a regression that reuses it is visible.
/// </para>
/// </remarks>
public sealed class TheCharacterChangesAsYouDressItTests(ArchiveContentFixture archive)
    : IClassFixture<ArchiveContentFixture>
{
    private const int Character = 60;

    /// <summary>A costume the archive declares under <c>[coat avatar]</c>, worn at slot 3.</summary>
    private const int Costume = 413500000;

    /// <summary>A second costume, so the store and the session copy can disagree.</summary>
    private const int OtherCostume = 101500690;

    private const short BagSlot = 12;

    private const short OtherBagSlot = 13;

    private const short AvatarSlot = 0;

    private const short WornCostumeSlot = 3;

    private static readonly NetworkConnectionId Connection = new("dressing");

    private static readonly NetworkDispatchContext Dispatch = new(
        new NetworkConnectionContext(Connection, NetworkEndpointKind.Game, 10011));

    [Fact]
    public async Task Expired_wear_qualification_refuses_equipping_but_allows_taking_off()
    {
        var equip = Build(inBagAt: BagSlot, canWear: false);
        await equip.MoveAsync(InventoryContainerType.Main, BagSlot, InventoryContainerType.Equipment, WornCostumeSlot);
        Assert.NotNull(Assert.IsType<MoveItemSpaceResponse>(Assert.Single(equip.Sender.Sent)).ErrorCode);
        var remove = Build(wornAt: WornCostumeSlot, canWear: false);
        await remove.MoveAsync(InventoryContainerType.Equipment, WornCostumeSlot, InventoryContainerType.Main, BagSlot);
        Assert.Null(Assert.IsType<MoveItemSpaceResponse>(remove.Sender.Sent[0]).ErrorCode);
    }

    [Fact]
    public async Task Putting_a_costume_on_tells_the_client_what_the_character_now_wears()
    {
        var world = Build(inBagAt: BagSlot);

        await world.MoveAsync(InventoryContainerType.Main, BagSlot, InventoryContainerType.Equipment, WornCostumeSlot);

        Assert.Collection(
            world.Sender.Sent,
            first => Assert.Null(Assert.IsType<MoveItemSpaceResponse>(first).ErrorCode),
            second => Assert.Equal(
                Costume,
                Assert.Single(Assert.IsType<CurrentUserStateMessage>(second).Equipment).Core.ItemId));
    }

    [Fact]
    public async Task Taking_a_costume_off_tells_the_client_the_character_is_no_longer_wearing_it()
    {
        var world = Build(wornAt: WornCostumeSlot);

        await world.MoveAsync(InventoryContainerType.Equipment, WornCostumeSlot, InventoryContainerType.Main, BagSlot);

        Assert.Collection(
            world.Sender.Sent,
            first => Assert.Null(Assert.IsType<MoveItemSpaceResponse>(first).ErrorCode),
            second => Assert.Empty(Assert.IsType<CurrentUserStateMessage>(second).Equipment));
    }

    /// <summary>
    /// The frame reports the store, not the copy the session has been carrying since selection.
    /// </summary>
    /// <remarks>
    /// The session's copy is seeded with a different costume from the one the store holds. An
    /// implementation that built the frame from the session would send that other costume and pass
    /// every other case here, because everywhere else the two agree.
    /// </remarks>
    [Fact]
    public async Task The_frame_follows_the_store_and_not_the_session_copy()
    {
        var world = Build(inBagAt: BagSlot, sessionCopyWears: OtherCostume);

        await world.MoveAsync(InventoryContainerType.Main, BagSlot, InventoryContainerType.Equipment, WornCostumeSlot);

        var frame = Assert.IsType<CurrentUserStateMessage>(world.Sender.Sent[1]);
        Assert.Equal(Costume, Assert.Single(frame.Equipment).Core.ItemId);
        Assert.DoesNotContain(frame.Equipment, item => item.Core.ItemId == OtherCostume);
    }

    /// <summary>Subsequent projections read the store without replacing a session copy.</summary>
    [Fact]
    public async Task The_current_view_follows_the_move_without_session_copy_replacement()
    {
        var world = Build(inBagAt: BagSlot, sessionCopyWears: OtherCostume);

        await world.MoveAsync(InventoryContainerType.Main, BagSlot, InventoryContainerType.Equipment, WornCostumeSlot);

        Assert.Equal(Costume, Assert.Single(
            (await world.OpenDungeonSelectionAsync()).Equipment).Core.ItemId);
        Assert.Equal(OtherCostume, Assert.Single(
            world.Runtime.Original.CharacterInventory.Containers.Single(
                value => value.ContainerType == InventoryContainerType.Equipment).Items).Core.ItemId);
    }

    /// <summary>
    /// Building the current view changes only its inventory projection, not entry data.
    /// </summary>
    /// <remarks>
    /// These readers must retain their captured objects rather than newly prepared entry values.
    /// This checks an uncontended refresh; it does not assert aggregate concurrency safety.
    /// </remarks>
    [Fact]
    public async Task Current_view_reads_inventory_without_modifying_entry_data()
    {
        var world = Build(inBagAt: BagSlot, sessionCopyWears: OtherCostume);
        var before = world.Runtime.Original;

        await world.MoveAsync(InventoryContainerType.Main, BagSlot, InventoryContainerType.Equipment, WornCostumeSlot);

        var after = Assert.IsType<SelectCharacterResult>(
            await world.CurrentViews.TryLoadAsync(OneSession.Session, CancellationToken.None));
        Assert.NotSame(before.CharacterInventory, after.CharacterInventory);
        Assert.Same(before.Character, after.Character);
        Assert.Same(before.CharacterEntry, after.CharacterEntry);
        Assert.Same(before.AccountInventory, after.AccountInventory);
        Assert.Same(before.Account, after.Account);
        Assert.Same(before.World, after.World);
        Assert.Same(before.Session, after.Session);
        Assert.Same(before.Roster, after.Roster);
        Assert.Same(before.RosterEntry, after.RosterEntry);
    }

    /// <summary>
    /// A move that changed nothing about how the character is drawn needs no appearance projection.
    /// </summary>
    /// <remarks>
    /// The refresh is gated on the move having succeeded, not on it having changed the drawn
    /// appearance, and the two are different questions: the immediate frame reads slots zero to
    /// twelve, twenty-three and creature slot twenty-four; the readers take the whole container --
    /// so a move onto worn slot thirteen would leave them stale under the narrower rule. A move
    /// inside the bag is the cheapest case that tells the two rules apart, and without it a
    /// narrower gate passes every case in this file.
    /// </remarks>
    [Fact]
    public async Task A_move_that_changes_nothing_drawn_needs_no_appearance_projection()
    {
        var world = Build(inBagAt: BagSlot, sessionCopyWears: OtherCostume);

        await world.MoveAsync(
            InventoryContainerType.Main, BagSlot, InventoryContainerType.Main, OtherBagSlot);

        Assert.Equal(0, world.Inventory.Loads);
        Assert.IsType<MoveItemSpaceResponse>(Assert.Single(world.Sender.Sent));
    }

    /// <summary>A refused move needs no current appearance projection.</summary>
    [Fact]
    public async Task A_refused_move_needs_no_appearance_projection()
    {
        var world = Build(sessionCopyWears: OtherCostume);

        await world.MoveAsync(InventoryContainerType.Main, BagSlot, InventoryContainerType.Equipment, WornCostumeSlot);

        Assert.Equal(0, world.Inventory.Loads);
    }

    /// <summary>
    /// A session displaced before the appearance read still gets its acknowledgement.
    /// </summary>
    /// <remarks>
    /// The move has committed, so losing the optional current projection cannot cost the answer.
    /// </remarks>
    [Fact]
    public async Task A_displaced_session_view_still_gets_its_acknowledgement()
    {
        var world = Build(
            inBagAt: BagSlot, sessionCopyWears: OtherCostume, sessionRefusesReplacement: true);

        await world.MoveAsync(InventoryContainerType.Main, BagSlot, InventoryContainerType.Equipment, WornCostumeSlot);

        var answer = Assert.IsType<MoveItemSpaceResponse>(Assert.Single(world.Sender.Sent));
        Assert.Null(answer.ErrorCode);
        Assert.Equal(1, answer.MoveValue);
        Assert.Empty(world.Logger.Errors);
    }

    [Fact]
    public async Task A_move_inside_the_bag_sends_the_acknowledgement_alone()
    {
        var world = Build(inBagAt: BagSlot);

        await world.MoveAsync(InventoryContainerType.Main, BagSlot, InventoryContainerType.Main, OtherBagSlot);

        Assert.IsType<MoveItemSpaceResponse>(Assert.Single(world.Sender.Sent));
    }

    [Fact]
    public async Task A_move_between_the_bag_and_the_avatar_page_sends_the_acknowledgement_alone()
    {
        var world = Build(inBagAt: BagSlot);

        await world.MoveAsync(InventoryContainerType.Main, BagSlot, InventoryContainerType.Avatar, AvatarSlot);

        Assert.IsType<MoveItemSpaceResponse>(Assert.Single(world.Sender.Sent));
    }

    /// <summary>A worn slot the frame does not draw from does not make it worth sending.</summary>
    /// <remarks>
    /// Slot 22 is inside the worn list and outside every run subtype zero reads -- the appearance
    /// takes 0..12 and 23, and the creature triple takes 24. A move there changes what the
    /// character owns and not what it looks like.
    /// </remarks>
    [Fact]
    public async Task A_move_onto_a_worn_slot_the_frame_ignores_sends_the_acknowledgement_alone()
    {
        var world = Build(wornAt: 22);

        await world.MoveAsync(InventoryContainerType.Equipment, 22, InventoryContainerType.Main, BagSlot);

        Assert.IsType<MoveItemSpaceResponse>(Assert.Single(world.Sender.Sent));
    }

    /// <summary>
    /// A move that succeeded still gets its acknowledgement when the appearance cannot be built.
    /// </summary>
    /// <remarks>
    /// The move is committed before the appearance is worked out, and the client holds the
    /// inventory shut until it is answered -- so a failure here costing the acknowledgement would
    /// bring back the very symptom this frame removes, wearing an unrelated cause. The store is
    /// asked after the transaction has closed, and asking it can raise.
    /// </remarks>
    [Fact]
    public async Task A_move_is_still_acknowledged_when_the_appearance_cannot_be_built()
    {
        var world = Build(inBagAt: BagSlot, storeRefusesToLoad: true);

        await world.MoveAsync(InventoryContainerType.Main, BagSlot, InventoryContainerType.Equipment, WornCostumeSlot);

        var acknowledgement = Assert.IsType<MoveItemSpaceResponse>(Assert.Single(world.Sender.Sent));
        Assert.Null(acknowledgement.ErrorCode);
        Assert.Equal(1, acknowledgement.MoveValue);
    }

    [Fact]
    public async Task A_refused_move_sends_the_refusal_alone()
    {
        var world = Build();

        await world.MoveAsync(InventoryContainerType.Main, BagSlot, InventoryContainerType.Equipment, WornCostumeSlot);

        var refusal = Assert.IsType<MoveItemSpaceResponse>(Assert.Single(world.Sender.Sent));
        Assert.NotNull(refusal.ErrorCode);
    }

    [Fact]
    public async Task Opening_dungeon_selection_after_dressing_reads_the_current_equipment()
    {
        var world = Build(inBagAt: BagSlot, sessionCopyWears: OtherCostume);
        var before = await world.OpenDungeonSelectionAsync();
        Assert.Empty(before.Equipment);

        await world.MoveAsync(InventoryContainerType.Main, BagSlot,
            InventoryContainerType.Equipment, WornCostumeSlot);

        var after = await world.OpenDungeonSelectionAsync();
        Assert.Equal(Costume, Assert.Single(after.Equipment).Core.ItemId);
        Assert.Same(before.Character, after.Character);

        await world.MoveAsync(InventoryContainerType.Equipment, WornCostumeSlot,
            InventoryContainerType.Main, BagSlot);
        Assert.Empty((await world.OpenDungeonSelectionAsync()).Equipment);
    }

    [Fact]
    public async Task Dressing_reads_the_inventory_once_for_both_frames_and_copy()
    {
        var world = Build(inBagAt: BagSlot, sessionCopyWears: OtherCostume);
        await world.MoveAsync(InventoryContainerType.Main, BagSlot,
            InventoryContainerType.Equipment, WornCostumeSlot);
        Assert.Equal(1, world.Inventory.Loads);
        Assert.Equal(Costume, Assert.Single(
            Assert.Single(world.Sender.Sent.OfType<CurrentUserStateMessage>()).Equipment).Core.ItemId);
        Assert.Equal(Costume, Assert.Single(
            (await world.OpenDungeonSelectionAsync()).Equipment).Core.ItemId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Refresh_preserves_the_real_directorys_position_persistence_flag(bool persists)
    {
        var world = Build(inBagAt: BagSlot, shouldPersistPosition: persists);
        await world.MoveAsync(InventoryContainerType.Main, BagSlot,
            InventoryContainerType.Equipment, WornCostumeSlot);
        Assert.True(world.Runtime.TryGet(OneSession.Session, out _, out var after));
        Assert.Equal(persists, after);
    }

    [Theory]
    [InlineData((byte)1)]
    [InlineData((byte)5)]
    public async Task Dungeon_selection_reports_refreshed_character_values_in_the_existing_frame_order(byte level)
    {
        var world = Build(sessionCopyWears: OtherCostume);
        var current = world.Runtime.Original with
        {
            Character = world.Runtime.Original.Character with { Progression = new(level, 9000) },
            CharacterEntry = world.Runtime.Original.CharacterEntry with
            {
                Public = world.Runtime.Original.CharacterEntry.Public with
                {
                    DerivedStats = world.Runtime.Original.CharacterEntry.Public.DerivedStats with { HpMaximum = 9876 },
                },
                Skills = world.Runtime.Original.CharacterEntry.Skills with
                {
                    Pages = [new CharacterSkillPageState(0, [new CharacterSkillState(7, 1, 4, [8, 9])])],
                    ProtocolMirror = new(11, 12, 13, 14),
                },
            },
        };
        var frame = await world.OpenDungeonSelectionAsync(new FixedCurrentState(current));
        Assert.Equal(level, frame.Character.Progression.Level);
        Assert.Equal(9876, frame.Public.DerivedStats.HpMaximum);
        Assert.Equal(new CharacterSkillProtocolMirrorState(11, 12, 13, 14), frame.Skills.ProtocolMirror);
        Assert.Equal(4, Assert.Single(Assert.Single(frame.Skills.Pages).Skills).Level);
        Assert.Equal(OtherCostume, Assert.Single(frame.Equipment).Core.ItemId);
        var expected = new List<Type> { typeof(ExtendedUserStateMessage), typeof(EnterSelectDungeonAcknowledgement),
            typeof(UserStateNotification), typeof(UdpHostNotification) };
        if (level > 1)
        {
            expected.Add(typeof(DungeonPermissionStateMessage));
            expected.Add(typeof(EnterSelectDungeonNotification));
            Assert.Equal(current.CharacterEntry.Progression.Dungeons,
                Assert.Single(world.Sender.Sent.OfType<DungeonPermissionStateMessage>()).Dungeons);
        }
        Assert.Equal(expected, world.Sender.Sent.Select(message => message.GetType()));
    }

    private sealed class FixedCurrentState(SelectCharacterResult result) : ICharacterStateProjectionDomainService
    {
        public ValueTask<SelectCharacterResult> RefreshAsync(SelectCharacterResult ignored, CancellationToken token) => ValueTask.FromResult(result);
    }

    private World Build(
        short? inBagAt = null,
        short? wornAt = null,
        int? sessionCopyWears = null,
        bool storeRefusesToLoad = false,
        bool sessionRefusesReplacement = false,
        bool shouldPersistPosition = false, bool canWear = true)
    {
        var character = new CharacterId(Character);
        var containers = new CharacterContainerDomainService();
        var bag = new SlotBook(InventoryContainerType.Main, character);
        var avatars = new SlotBook(InventoryContainerType.Avatar, character);
        var worn = new SlotBook(InventoryContainerType.Equipment, character);
        if (inBagAt is { } bagSlot)
            bag.Seed(bagSlot, Costume);
        if (wornAt is { } wornSlot)
            worn.Seed(wornSlot, wornSlot == WornCostumeSlot ? Costume : OtherCostume);
        containers.Register(character, bag);
        containers.Register(character, avatars);
        containers.Register(character, worn);

        var mappings = new InventorySlotMappingRuleService();
        var commandHandler = new MoveItemSpaceCommandHandler(
            new BusinessInventorySlotDomainService(containers, mappings),
            new ItemMovementDomainService(
                new ItemPlacementRuleService(archive.Placements, archive.EquipmentSlots),
                new ItemSlotRuleService(), new TheCharacterChangesAsYouDressItTestsWearEligibility(canWear)));

        var runtime = new RecordingRuntime(sessionCopyWears, sessionRefusesReplacement, shouldPersistPosition);
        var inventory = new StoreBackedInventory(worn, character);
        var currentViews = new RuntimeCurrentView(runtime, inventory:
            storeRefusesToLoad ? new InventoryThatRaises() : inventory);
        var sender = new RecordingSender();
        var logger = new RecordingLogger();
        return new World(
            new MoveItemSpaceHandler(
                new OneSession(),
                currentViews,
                mappings,
                new DirectMoveBus(commandHandler),
                sender,
                logger),
            sender,
            runtime,
            currentViews,
            inventory,
            logger);
    }

    private sealed class UnchangedCurrentState : ICharacterStateProjectionDomainService
    {
        public ValueTask<SelectCharacterResult> RefreshAsync(SelectCharacterResult result, CancellationToken token) => ValueTask.FromResult(result);
    }

    private sealed record World(
        MoveItemSpaceHandler Handler, RecordingSender Sender, RecordingRuntime Runtime,
        RuntimeCurrentView CurrentViews,
        StoreBackedInventory Inventory, RecordingLogger Logger)
    {
        public async ValueTask<ExtendedUserStateMessage> OpenDungeonSelectionAsync(ICharacterStateProjectionDomainService? current = null)
        {
            Sender.Sent.Clear();
            await new EnterSelectDungeonHandler(current ?? new UnchangedCurrentState(), new OneSession(), CurrentViews, new SelectionVisits(), new IgnoredEntryIntents(),
                Sender,
                TownAreaProjectionTestFactory.Create(new OneSession(), CurrentViews, Sender),
                NullLogger<EnterSelectDungeonHandler>.Instance,
                new VirtualOnlyPartyService()).HandleAsync(
                    Dispatch, new EnterSelectDungeonRequest(GameMessageMetadata.Empty, 10000),
                    CancellationToken.None);
            return Assert.Single(Sender.Sent.OfType<ExtendedUserStateMessage>());
        }

        public ValueTask MoveAsync(
            InventoryContainerType source,
            short sourceSlot,
            InventoryContainerType destination,
            short destinationSlot) =>
            this.Handler.HandleAsync(
                Dispatch,
                new MoveItemSpaceRequest(
                    GameMessageMetadata.Empty,
                    new GameSlot(source, sourceSlot),
                    SourceInstanceValue: 0,
                    MoveCount: 1,
                    new GameSlot(destination, destinationSlot),
                    DestinationInstanceValue: 0),
                CancellationToken.None);
    }

    private sealed class IgnoredEntryIntents : IDungeonEntryIntentDomainService
    {
        public void Record(SelectedSessionIdentity session, DungeonEntryIntent intent) { }

        public DungeonEntryIntent? Consume(SelectedSessionIdentity session) => null;

        public bool ClearExact(SelectedSessionIdentity session) => false;
    }

    /// <summary>
    /// The inventory the store would hand back, read from the same worn container the move wrote to.
    /// </summary>
    /// <remarks>
    /// Reading it out of the container the command just wrote to is what makes this the store's
    /// answer rather than a fixture's: an implementation that never asked would not see the move.
    /// </remarks>
    private sealed class StoreBackedInventory(SlotBook worn, CharacterId character)
        : ICharacterInventoryEntryDomainService
    {
        public int Loads { get; private set; }

        public async ValueTask<CharacterInventoryEntryState> LoadAsync(
            AccountId accountId, CharacterId characterId, CancellationToken cancellationToken)
        {
            Loads++;
            var items = await worn.ReadAllAsync(cancellationToken);
            var shape = SelectCharacterPayloadTestData.CreateComplete().CharacterInventory;
            return shape with
            {
                CharacterId = character,
                Containers =
                [
                    .. shape.Containers
                        .Where(value => value.ContainerType != InventoryContainerType.Equipment),
                    new InventoryContainerState(
                        InventoryContainerType.Equipment,
                        0,
                        [.. items.Select(item => new InventoryItemState(
                            new ItemIdentity(item.SlotIndex + 1), item.SlotIndex, item.Core))]),
                ],
            };
        }
    }

    /// <summary>A store that will not answer, which is what a read after the transaction can do.</summary>
    private sealed class InventoryThatRaises : ICharacterInventoryEntryDomainService
    {
        public ValueTask<CharacterInventoryEntryState> LoadAsync(
            AccountId accountId, CharacterId characterId, CancellationToken cancellationToken) =>
            throw new InvalidDataException("The stored inventory could not be projected.");
    }

    /// <summary>Historical entry data used only to prove later reads use the store.</summary>
    private sealed class RecordingRuntime
        : ILegacySessionSnapshotFixture
    {
        private readonly SelectedSessionRuntimeDirectory directory = new();
        private readonly SelectCharacterResult state;
        private readonly bool unavailable;

        public RecordingRuntime(int? wears, bool refusesReplacement, bool shouldPersistPosition)
        {
            var source = Snapshot(wears);
            state = source with { Character = source.Character with
                { Identity = source.Character.Identity with { CharacterId = new CharacterId(Character) } } };
            this.unavailable = refusesReplacement;
            directory.Publish(OneSession.Session, shouldPersistPosition);
        }

        /// <summary>The copy as it was taken at selection.</summary>
        public SelectCharacterResult Original => this.state;


        public bool TryGet(
            SelectedSessionIdentity session,
            out SelectCharacterResult? result,
            out bool shouldPersistPosition)
        {
            shouldPersistPosition = false;
            var published = !unavailable
                && directory.TryGetPositionPersistence(session, out shouldPersistPosition);
            if (!published)
                shouldPersistPosition = false;
            result = published ? state : null;
            return published;
        }

        public bool RemoveExact(SelectedSessionIdentity session) => false;

        private static SelectCharacterResult Snapshot(int? wears)
        {
            var source = SelectCharacterPayloadTestData.CreateComplete();
            if (wears is not { } itemId)
                return source;

            return source with
            {
                CharacterInventory = source.CharacterInventory with
                {
                    Containers =
                    [
                        .. source.CharacterInventory.Containers
                            .Where(value => value.ContainerType != InventoryContainerType.Equipment),
                        new InventoryContainerState(
                            InventoryContainerType.Equipment,
                            0,
                            [new InventoryItemState(
                                new ItemIdentity(99),
                                WornCostumeSlot,
                                StackableItemCore.Create(8, itemId, 1))]),
                    ],
                },
            };
        }
    }

    private sealed class SlotBook(InventoryContainerType type, CharacterId character) : IItemContainer
    {
        private readonly Dictionary<int, ContainerSlotItem> bySlot = [];

        public ContainerId Id { get; } = ContainerId.ForCharacter(character, type);

        public InventoryContainerType Type => type;

        public ContainerShape Shape { get; } = new(type, 0);

        public bool IsRemovable => false;

        public void Seed(int slotIndex, int itemId) =>
            this.bySlot[slotIndex] = new ContainerSlotItem(
                slotIndex, StackableItemCore.Create(8, itemId, 1), ItemWorldClass.Avatar);

        public ValueTask<ContainerSlotItem?> ReadSlotAsync(
            int slotIndex, CancellationToken cancellationToken) =>
            ValueTask.FromResult(this.bySlot.GetValueOrDefault(slotIndex));

        public ValueTask<ContainerSlotPair> ReadSlotsAsync(
            int firstSlotIndex, int secondSlotIndex, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ContainerSlotPair(
                this.bySlot.GetValueOrDefault(firstSlotIndex),
                this.bySlot.GetValueOrDefault(secondSlotIndex)));

        public ValueTask PutAsync(
            int slotIndex, InventoryItemCoreState core, CancellationToken cancellationToken)
        {
            this.bySlot[slotIndex] = new ContainerSlotItem(slotIndex, core, ItemWorldClass.Avatar);
            return ValueTask.CompletedTask;
        }

        public ValueTask ClearAsync(int slotIndex, CancellationToken cancellationToken)
        {
            this.bySlot.Remove(slotIndex);
            return ValueTask.CompletedTask;
        }

        public ValueTask<ItemMoveOutcome> MoveToAsync(
            int fromSlotIndex, int toSlotIndex, int count, CancellationToken cancellationToken)
        {
            var moving = this.bySlot.GetValueOrDefault(fromSlotIndex);
            if (moving is null)
                return ValueTask.FromResult<ItemMoveOutcome>(
                    new ItemMoveOutcome.Refused(ItemMoveRefusal.BothSlotsEmpty));

            this.bySlot.Remove(fromSlotIndex);
            this.bySlot[toSlotIndex] = new ContainerSlotItem(
                toSlotIndex, moving.Core, ItemWorldClass.Avatar);
            return ValueTask.FromResult<ItemMoveOutcome>(
                new ItemMoveOutcome.Moved(count, moving.ItemId));
        }

        public ValueTask<IReadOnlyList<ContainerSlotItem>> ReadAllAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<ContainerSlotItem>>(
                [.. this.bySlot.Values.OrderBy(item => item.SlotIndex)]);

        public ValueTask<IReadOnlyList<InventoryItemState>> SnapshotAsync(
            CancellationToken cancellationToken) => throw Unused();

        public ValueTask<ItemPlacementOutcome> AddAsync(
            int itemId, int count, CancellationToken cancellationToken) => throw Unused();

        public ValueTask<ItemPlacementOutcome> RefillAsync(
            ItemRefill refill, CancellationToken cancellationToken) => throw Unused();

        private static NotSupportedException Unused() =>
            new("This container answers slot reads, writes, moves and a full read.");
    }

    private sealed class DirectMoveBus(
        ICommandHandler<MoveItemSpaceCommand, ItemMoveResult> move) : ICommandBus
    {
        public async ValueTask<TResult> SendAsync<TResult>(
            ICommand<TResult> command, CancellationToken cancellationToken = default)
            where TResult : notnull
        {
            var context = new CommandExecutionContext(
                Guid.Empty, command.GetType(), typeof(DirectMoveBus),
                CommandExecutionKind.Transactional);
            var result = await move.HandleAsync(
                context, (MoveItemSpaceCommand)(object)command, cancellationToken);
            return (TResult)(object)result;
        }
    }

    private sealed class RecordingLogger : ILogger<MoveItemSpaceHandler>
    {
        public List<Exception> Errors { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error && exception is not null) Errors.Add(exception);
        }
    }

    private sealed class SelectionVisits : IDungeonVisitDomainService
    {
        public bool DefersSelectionState(byte characterLevel) => characterLevel <= 1;
        public ValueTask<Dfo.Server.Abstractions.World.DungeonEntry?> EnterAsync(CharacterId characterId, int dungeonId,
            DungeonDifficulty difficulty, DungeonEntryFacts facts, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public ValueTask<DungeonRoomVisit?> MoveAsync(CharacterId characterId,
            DungeonRunState current, MazeCell target, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class OneSession : ISelectedSessionDomainService
    {
        public static readonly SelectedSessionIdentity Session = SelectedSessionIdentity.Create(
            new AccountId(6), new CharacterId(Character), Connection, 1, 10011);

        public SelectedSessionIdentity? Select(SelectedSessionIdentity session) => null;

        public bool ReleaseExact(SelectedSessionIdentity session) => false;

        public SelectedSessionIdentity? Find(AccountId accountId) => Session;

        public SelectedSessionIdentity? FindByCharacter(CharacterId characterId) => Session;

        public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId) => Session;

        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId) => Session;
    }

    private sealed class RecordingSender : IGameMessageSender
    {
        public List<IGameServerMessage> Sent { get; } = [];

        public ValueTask SendCmdAsync(
            NetworkConnectionId connectionId,
            IGameCmdMessage message,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A move answers explicitly.");

        public ValueTask SendNotiAsync(
            NetworkConnectionId connectionId,
            IGameNotiMessage message,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A move answers explicitly.");

        public ValueTask SendExplicitAsync(
            NetworkConnectionId requiredDirectConnectionId,
            GameExplicitMessageDelivery delivery,
            CancellationToken cancellationToken)
        {
            this.Sent.AddRange(delivery.RequiredDirectMessages);
            return ValueTask.CompletedTask;
        }
    }
}

internal sealed class TheCharacterChangesAsYouDressItTestsWearEligibility(bool allowed) : Dfo.Server.Abstractions.Inventory.IEquipmentWearDomainService
{
    public ValueTask<bool> CanWearAsync(Dfo.Server.Data.Characters.CharacterId characterId, int itemId, CancellationToken cancellationToken) => ValueTask.FromResult(allowed);
}
