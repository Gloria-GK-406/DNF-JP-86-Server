using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
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
using Microsoft.Extensions.Logging.Abstractions;

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
/// sending it again when a move changed what it would say -- and building it from what the store
/// holds rather than from the session's own copy, which is the copy taken at character selection
/// and which no move refreshes. <c>The_frame_follows_the_store_and_not_the_session_copy</c> is the
/// case that separates those two: without it, an implementation reading the session copy passes
/// every other case in this file.
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

    /// <summary>And the copy is left exactly as it was.</summary>
    /// <remarks>
    /// Refreshing it would change what every other reader of it draws -- the dungeon selection
    /// screen takes its equipment from there -- and that is a different outcome from this one. The
    /// two disagree, and they disagreed before this frame existed.
    /// </remarks>
    [Fact]
    public async Task The_session_copy_is_left_as_it_was()
    {
        var world = Build(inBagAt: BagSlot, sessionCopyWears: OtherCostume);

        await world.MoveAsync(InventoryContainerType.Main, BagSlot, InventoryContainerType.Equipment, WornCostumeSlot);

        Assert.Equal(0, world.Runtime.Replacements);
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

    private World Build(
        short? inBagAt = null,
        short? wornAt = null,
        int? sessionCopyWears = null,
        bool storeRefusesToLoad = false)
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

        var commandHandler = new MoveItemSpaceCommandHandler(
            containers,
            new ItemMovementDomainService(
                new ItemPlacementRuleService(archive.Placements, archive.EquipmentSlots),
                new ItemSlotRuleService()));

        var runtime = new RecordingRuntime(sessionCopyWears);
        var sender = new RecordingSender();
        return new World(
            new MoveItemSpaceHandler(
                new OneSession(),
                runtime,
                storeRefusesToLoad
                    ? new InventoryThatRaises()
                    : new StoreBackedInventory(worn, character),
                new DirectMoveBus(commandHandler),
                sender,
                NullLogger<MoveItemSpaceHandler>.Instance),
            sender,
            runtime);
    }

    private sealed record World(
        MoveItemSpaceHandler Handler, RecordingSender Sender, RecordingRuntime Runtime)
    {
        public ValueTask MoveAsync(
            InventoryContainerType source,
            short sourceSlot,
            InventoryContainerType destination,
            short destinationSlot) =>
            this.Handler.HandleAsync(
                Dispatch,
                new MoveItemSpaceRequest(
                    GameMessageMetadata.Empty,
                    (byte)source,
                    sourceSlot,
                    SourceInstanceValue: 0,
                    MoveCount: 1,
                    (byte)destination,
                    destinationSlot,
                    DestinationInstanceValue: 0),
                CancellationToken.None);
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
        public async ValueTask<CharacterInventoryEntryState> LoadAsync(
            AccountId accountId, CharacterId characterId, CancellationToken cancellationToken)
        {
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

    /// <summary>The session's own copy, and a count of how often anyone replaced it.</summary>
    private sealed class RecordingRuntime(int? wears) : ISelectedSessionRuntimeDirectory
    {
        private readonly SelectCharacterResult state = Snapshot(wears);

        public int Replacements { get; private set; }

        public void Publish(
            SelectedSessionIdentity session, SelectCharacterResult result, bool shouldPersistPosition) =>
            throw new NotSupportedException("Nothing publishes here.");

        public void ReplaceExact(SelectedSessionIdentity session, SelectCharacterResult result) =>
            this.Replacements++;

        public SelectCharacterResult GetRequired(SelectedSessionIdentity session) => this.state;

        public bool TryGet(
            SelectedSessionIdentity session,
            out SelectCharacterResult? result,
            out bool shouldPersistPosition)
        {
            result = this.state;
            shouldPersistPosition = false;
            return true;
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

    private sealed class OneSession : ISelectedSessionDomainService
    {
        private static readonly SelectedSessionIdentity Session = SelectedSessionIdentity.Create(
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
