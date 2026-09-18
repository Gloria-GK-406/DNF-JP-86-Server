using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.MoveItemSpace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Game;

namespace Dfo.Server.Application.Tests.Game;

/// <summary>
/// What the server decides when the client asks for an item to change slots.
/// </summary>
/// <remarks>
/// The reversed request is the one that matters here. The tutorial's potion arrived in slot 65 and
/// the client immediately asked for slot 3 -- naming the empty quick slot as the source and the
/// slot holding the potion as the destination. Reading that literally finds nothing to move; the
/// reference server carries the same reversal, and so does this.
/// </remarks>
public sealed class MoveItemSpaceHandlerTests
{
    /// <summary>The live frame: quick slot 3 named as source, the potion's slot 65 as destination.</summary>
    private static readonly MoveItemSpaceRequest ObservedRequest = Request(3, 65, 0, 8474);

    [Fact]
    public async Task The_reversed_request_moves_the_potion_onto_the_bar()
    {
        var inventory = new FakeInventory { [65] = (8474, 1) };
        var (handler, sender) = Build(inventory);

        await handler.HandleAsync(Context, ObservedRequest, CancellationToken.None);

        Assert.Equal([(3, 8474, 1)], inventory.Rows());
        var response = Assert.IsType<MoveItemSpaceResponse>(Assert.Single(sender.Sent));
        Assert.Null(response.ErrorCode);
        Assert.Equal(1, response.MoveValue);
    }

    /// <remarks>
    /// The client matches the acknowledgement against what it sent, so both ends come back as it
    /// wrote them -- not as the server rearranged them to carry the move out.
    /// </remarks>
    [Fact]
    public async Task The_acknowledgement_echoes_the_ends_the_client_sent()
    {
        var (handler, sender) = Build(new FakeInventory { [65] = (8474, 1) });

        await handler.HandleAsync(Context, ObservedRequest, CancellationToken.None);

        var response = (MoveItemSpaceResponse)sender.Sent.Single();
        Assert.Equal(3, response.SourceSlotIndex);
        Assert.Equal(65, response.DestinationSlotIndex);
    }

    [Fact]
    public async Task An_ordinary_drag_moves_the_named_source()
    {
        var inventory = new FakeInventory { [65] = (8474, 1) };
        var (handler, sender) = Build(inventory);

        await handler.HandleAsync(Context, Request(65, 4, 8474, 0), CancellationToken.None);

        Assert.Equal([(4, 8474, 1)], inventory.Rows());
        Assert.Null(((MoveItemSpaceResponse)sender.Sent.Single()).ErrorCode);
    }

    [Fact]
    public async Task Two_items_trade_places_when_the_target_is_taken()
    {
        var inventory = new FakeInventory { [65] = (8474, 1), [66] = (9000, 1) };
        var (handler, sender) = Build(inventory);

        await handler.HandleAsync(Context, Request(65, 66, 8474, 9000), CancellationToken.None);

        Assert.Equal([(65, 9000, 1), (66, 8474, 1)], inventory.Rows());
        Assert.Null(((MoveItemSpaceResponse)sender.Sent.Single()).ErrorCode);
    }

    [Fact]
    public async Task Two_piles_of_one_item_merge()
    {
        var inventory = new FakeInventory { [65] = (8474, 3), [66] = (8474, 4) };
        var (handler, _) = Build(inventory);

        await handler.HandleAsync(Context, Request(65, 66, 8474, 8474), CancellationToken.None);

        Assert.Equal([(66, 8474, 7)], inventory.Rows());
    }

    /// <remarks>
    /// A potion's band starts at 65. Slot 130 belongs to materials, and an item dropped there is
    /// one the client would not draw where the player looks for it.
    /// </remarks>
    [Fact]
    public async Task A_slot_outside_the_items_band_is_refused()
    {
        var inventory = new FakeInventory { [65] = (8474, 1) };
        var (handler, sender) = Build(inventory);

        await handler.HandleAsync(Context, Request(65, 130, 8474, 0), CancellationToken.None);

        Assert.Equal([(65, 8474, 1)], inventory.Rows());
        Assert.Equal(
            MoveItemSpaceResponse.NotMovable,
            ((MoveItemSpaceResponse)sender.Sent.Single()).ErrorCode);
    }

    /// <remarks>
    /// Cargo, equipment, pets and the rest each carry their own rules about what may enter them.
    /// Answering for them without those rules would open a door that should have stayed shut.
    /// <para>
    /// 「无法移动该物品」, which is what the Japanese table spells out as "an item of a kind that
    /// cannot be moved". The refusal used to send code 2, which the client draws nothing for at
    /// all -- a drag into the cargo looked exactly like a dropped frame.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_list_other_than_the_main_one_is_refused()
    {
        var inventory = new FakeInventory { [65] = (8474, 1) };
        var (handler, sender) = Build(inventory);

        await handler.HandleAsync(
            Context,
            ObservedRequest with { DestinationListType = 2 },
            CancellationToken.None);

        Assert.Equal([(65, 8474, 1)], inventory.Rows());
        Assert.Equal(
            MoveItemSpaceResponse.NotMovable,
            ((MoveItemSpaceResponse)sender.Sent.Single()).ErrorCode);
    }

    /// <remarks>
    /// The client settles a drag that ended where it began by sending both ends the same. That is
    /// not a failure and answering it as one would make the client draw an error over a drag the
    /// player abandoned.
    /// </remarks>
    [Fact]
    public async Task Naming_one_slot_twice_succeeds_without_moving_anything()
    {
        var inventory = new FakeInventory { [65] = (8474, 1) };
        var (handler, sender) = Build(inventory);

        await handler.HandleAsync(Context, Request(65, 65, 8474, 8474), CancellationToken.None);

        Assert.Equal([(65, 8474, 1)], inventory.Rows());
        Assert.Null(((MoveItemSpaceResponse)sender.Sent.Single()).ErrorCode);
    }

    /// <remarks>
    /// Answered without a message. Code 4, which this used to send, says 「金库空间不足， 无法放入
    /// 物品」 -- and two empty slots are not a space problem, they are the client and the server
    /// disagreeing about an inventory. The client owns no sentence for that, and every sentence it
    /// does own would blame the player for it.
    /// </remarks>
    [Fact]
    public async Task Two_empty_slots_are_refused_without_a_message()
    {
        var (handler, sender) = Build(new FakeInventory());

        await handler.HandleAsync(Context, Request(65, 66, 0, 0), CancellationToken.None);

        Assert.Equal(
            MoveItemSpaceResponse.Silent,
            ((MoveItemSpaceResponse)sender.Sent.Single()).ErrorCode);
    }

    /// <remarks>
    /// 「物品栏已有同类物品」 -- the Japanese table spells it "the inventory already holds an item
    /// at the same stack limit". It is the one refusal on this path the client can name exactly,
    /// and it used to be folded in with everything else under a code that showed nothing.
    /// </remarks>
    [Fact]
    public async Task A_merge_past_the_stack_limit_says_the_pile_is_full()
    {
        var inventory = new FakeInventory { [65] = (8474, 60), [66] = (8474, 60) };
        var (handler, sender) = Build(inventory);

        await handler.HandleAsync(Context, Request(65, 66, 8474, 8474), CancellationToken.None);

        Assert.Equal([(65, 8474, 60), (66, 8474, 60)], inventory.Rows());
        Assert.Equal(
            MoveItemSpaceResponse.StackLimitReached,
            ((MoveItemSpaceResponse)sender.Sent.Single()).ErrorCode);
    }

    /// <remarks>
    /// A swap has to satisfy both ends. Here the item being moved may go where it is aimed, but the
    /// one it would displace may not go where it would land, so nothing moves. The client is told
    /// 「无法移动该物品」 rather than nothing: the player did aim at a slot that cannot take the
    /// trade, and that is something they can act on.
    /// </remarks>
    [Fact]
    public async Task A_swap_whose_other_end_cannot_land_says_the_item_will_not_move()
    {
        var inventory = new FakeInventory
        {
            [65] = (TwoBandPlacements.Consumable, 1),
            [100] = (TwoBandPlacements.Equipment, 1),
        };
        var (handler, sender) = Build(inventory, new TwoBandPlacements());

        await handler.HandleAsync(
            Context,
            Request(65, 100, TwoBandPlacements.Consumable, TwoBandPlacements.Equipment),
            CancellationToken.None);

        Assert.Equal(
            [(65, TwoBandPlacements.Consumable, 1), (100, TwoBandPlacements.Equipment, 1)],
            inventory.Rows());
        Assert.Equal(
            MoveItemSpaceResponse.NotMovable,
            ((MoveItemSpaceResponse)sender.Sent.Single()).ErrorCode);
    }

    /// <summary>
    /// Dragging part of a pile onto an empty slot leaves the rest where it was.
    /// </summary>
    /// <remarks>
    /// This is the split, and nothing asserted it before: making every move a whole relocation left
    /// the suite green. The acknowledgement carries the part that moved, not the pile that was
    /// dragged, because the client draws both slots from what it is told here.
    /// </remarks>
    [Fact]
    public async Task Dragging_part_of_a_pile_onto_an_empty_slot_leaves_the_rest_behind()
    {
        var inventory = new FakeInventory { [65] = (8474, 5) };
        var (handler, sender) = Build(inventory);

        await handler.HandleAsync(
            Context, Request(65, 66, 0, 0) with { MoveCount = 2 }, CancellationToken.None);

        Assert.Equal([(65, 8474, 3), (66, 8474, 2)], inventory.Rows());
        var response = (MoveItemSpaceResponse)sender.Sent.Single();
        Assert.Null(response.ErrorCode);
        Assert.Equal(2, response.MoveValue);
    }

    /// <summary>
    /// Dragging part of a pile onto a slot holding something else is refused, not carried out as a
    /// whole swap.
    /// </summary>
    /// <remarks>
    /// A swap trades what two slots hold, and there is nowhere for the remainder of a split pile to
    /// go: obeying the count would leave the other item nowhere, and ignoring it would move more
    /// than the player asked for. The reference server refuses, and so does this.
    ///
    /// Nothing asserted this before. Deleting the refusal left the whole suite green, which is how
    /// it came to be written down here.
    /// </remarks>
    [Fact]
    public async Task Dragging_part_of_a_pile_onto_another_item_will_not_swap()
    {
        var inventory = new FakeInventory { [65] = (8474, 5), [66] = (9000, 1) };
        var (handler, sender) = Build(inventory);

        await handler.HandleAsync(
            Context, Request(65, 66, 0, 0) with { MoveCount = 2 }, CancellationToken.None);

        Assert.Equal([(65, 8474, 5), (66, 9000, 1)], inventory.Rows());
        Assert.Equal(
            MoveItemSpaceResponse.NotMovable,
            ((MoveItemSpaceResponse)sender.Sent.Single()).ErrorCode);
    }

    /// <remarks>
    /// A merge that fits is still a merge; the refusal above must not be reachable by every pair
    /// of like piles.
    /// </remarks>
    [Fact]
    public async Task A_merge_within_the_stack_limit_still_happens()
    {
        var inventory = new FakeInventory { [65] = (8474, 10), [66] = (8474, 10) };
        var (handler, sender) = Build(inventory);

        await handler.HandleAsync(Context, Request(65, 66, 8474, 8474), CancellationToken.None);

        Assert.Equal([(66, 8474, 20)], inventory.Rows());
        Assert.Null(((MoveItemSpaceResponse)sender.Sent.Single()).ErrorCode);
    }

    /// <summary>
    /// Two moves of one character's inventory arriving at once, which is what a lost race is.
    /// </summary>
    /// <remarks>
    /// This is the attempt to construct the refusal that used to be called `SlotsChanged`, and it
    /// is recorded here because it does not succeed. Storage used to re-read the displaced slot
    /// inside its own transaction and answer false when it had changed; a move is now a read and
    /// some writes inside one use case boundary, and there is no second read to disagree with the
    /// first. Two of them therefore both go through, the later one settling over the earlier.
    ///
    /// Nothing is asserted here about which of the two wins -- that is a race and has no answer.
    /// What is asserted is that neither is refused, which is the fact the removal of that refusal
    /// rests on.
    /// </remarks>
    [Fact]
    public async Task Two_moves_arriving_at_once_are_both_carried_out_and_neither_is_refused()
    {
        var inventory = new FakeInventory { [65] = (8474, 1), [66] = (9000, 1) };
        var placements = new ConsumablePlacements();
        var handler = new MoveItemSpaceCommandHandler(
            inventory.Containers(placements),
            new ItemMovementDomainService(
                new ItemPlacementRuleService(placements, new NoWearables()),
                new ItemSlotRuleService()));

        var results = await Task.WhenAll(
            Dispatch(handler, inventory, sourceSlot: 65, destinationSlot: 3),
            Dispatch(handler, inventory, sourceSlot: 66, destinationSlot: 3));

        Assert.All(results, result => Assert.Equal(ItemMoveStatus.Moved, result.Status));
        Assert.Single(inventory.Rows(), row => row.Slot == 3);

        // Which item moved is reported, not only how much of it. Nothing else asserts this: the
        // identifier reaches the log rather than the acknowledgement, so a move that named the
        // wrong item would read correctly to the player and wrongly to anyone reading the log.
        Assert.All(results, result => Assert.Contains(result.ItemId, (int[])[8474, 9000]));
        Assert.Equal(2, results.Select(result => result.ItemId).Distinct().Count());
    }

    /// <summary>One command, in its own use case boundary, on its own flow.</summary>
    private static Task<ItemMoveResult> Dispatch(
        MoveItemSpaceCommandHandler handler,
        FakeInventory inventory,
        short sourceSlot,
        short destinationSlot) =>
        Task.Run(async () =>
        {
            var bus = new DirectCommandBus(move: handler, scope: inventory.Scope);
            return await bus.SendAsync(
                new MoveItemSpaceCommand(new CharacterId(60), 0, sourceSlot, 0, 0, destinationSlot),
                CancellationToken.None);
        });

    /// <summary>Nothing is wearable here; these tests move things inside the bag.</summary>
    private sealed class NoWearables : IEquipmentSlotDefinitionSource
    {
        public ValueTask<byte?> GetAsync(int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<byte?>(null);
    }

    private static readonly NetworkDispatchContext Context = new(
        new NetworkConnectionContext(new NetworkConnectionId("game-move"), NetworkEndpointKind.Game, 10011));

    private static MoveItemSpaceRequest Request(
        short sourceSlot, short destinationSlot, int sourceInstance, int destinationInstance) =>
        new(GameMessageMetadata.Empty, 0, sourceSlot, sourceInstance, 0, 0, destinationSlot,
            destinationInstance);

    private static (MoveItemSpaceHandler Handler, RecordingSender Sender) Build(
        FakeInventory inventory, IItemPlacementDefinitionSource? placements = null)
    {
        var sender = new RecordingSender();
        var resolved = placements ?? new ConsumablePlacements();
        var commandHandler = new MoveItemSpaceCommandHandler(
            inventory.Containers(resolved),
            new ItemMovementDomainService(
                new ItemPlacementRuleService(resolved, new NoWearables()),
                new ItemSlotRuleService()));

        return (
            new MoveItemSpaceHandler(
                new OneSession(),
                new NoPublishedRuntime(),
                new DirectCommandBus(move: commandHandler, scope: inventory.Scope),
                sender,
                NullLogger<MoveItemSpaceHandler>.Instance),
            sender);
    }


    /// <summary>Every item here is an ordinary consumable: kind 2, band 65 to 120.</summary>
    private sealed class ConsumablePlacements : IItemPlacementDefinitionSource
    {
        public ValueTask<ItemPlacement?> GetAsync(int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ItemPlacement?>(new ItemPlacement(2, 100, 65, 120));
    }

    /// <summary>
    /// One item in the consumable band and one in the equipment band, so a swap can fail on the
    /// end that is being displaced rather than the end being moved.
    /// </summary>
    private sealed class TwoBandPlacements : IItemPlacementDefinitionSource
    {
        public const int Consumable = 8474;
        public const int Equipment = 10000;

        public ValueTask<ItemPlacement?> GetAsync(int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ItemPlacement?>(
                itemId == Equipment
                    ? new ItemPlacement(1, 1, 9, 64)
                    : new ItemPlacement(2, 100, 65, 120));
    }

    /// <summary>The character's main container, seeded by slot.</summary>
    private sealed class FakeInventory : FakeMainInventory
    {
        public (int ItemId, int Count) this[int slotIndex]
        {
            set => this.Seed(slotIndex, value.ItemId, value.Count);
        }
    }

    private sealed class OneSession : ISelectedSessionDomainService
    {
        private static readonly SelectedSessionIdentity Session = SelectedSessionIdentity.Create(
            new AccountId(6), new CharacterId(60), new NetworkConnectionId("game-move"), 1, 10011);

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
            NetworkConnectionId connectionId, IGameCmdMessage message,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The move acknowledgement is sent explicitly.");

        public ValueTask SendNotiAsync(
            NetworkConnectionId connectionId, IGameNotiMessage message,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The move acknowledgement is a CMD.");

        public ValueTask SendExplicitAsync(
            NetworkConnectionId requiredDirectConnectionId,
            GameExplicitMessageDelivery delivery,
            CancellationToken cancellationToken)
        {
            Sent.AddRange(delivery.RequiredDirectMessages);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>A session directory that has nothing published, so no appearance frame is built.</summary>
    /// <remarks>
    /// These cases are about which moves the handler serves and what it answers, not about what
    /// follows a move that changed the character's look. A session that cannot be read is exactly
    /// the case the handler answers by sending the acknowledgement alone.
    /// </remarks>
    private sealed class NoPublishedRuntime : ISelectedSessionRuntimeDirectory, ISelectedCharacterCurrentViewDomainService
    {
        public ValueTask<SelectCharacterResult?> TryLoadAsync(
            SelectedSessionIdentity session, CancellationToken cancellationToken) =>
            ValueTask.FromResult<SelectCharacterResult?>(null);
        public void Publish(SelectedSessionIdentity session, bool shouldPersistPosition) =>
            throw new NotSupportedException("Nothing publishes here.");

        public bool IsPublishedExact(SelectedSessionIdentity session) => false;

        public bool TryGetPositionPersistence(
            SelectedSessionIdentity session, out bool shouldPersistPosition)
        {
            shouldPersistPosition = false;
            return false;
        }

        public void Publish(
            SelectedSessionIdentity session, SelectCharacterResult result, bool shouldPersistPosition) =>
            throw new NotSupportedException("Nothing publishes here.");

        public void ReplaceExact(SelectedSessionIdentity session, SelectCharacterResult result) =>
            throw new NotSupportedException("Nothing replaces here.");

        public SelectCharacterResult GetRequired(SelectedSessionIdentity session) =>
            throw new NotSupportedException("Nothing is published here.");

        public bool TryGet(
            SelectedSessionIdentity session,
            out SelectCharacterResult? result,
            out bool shouldPersistPosition)
        {
            result = null;
            shouldPersistPosition = false;
            return false;
        }

        public bool RemoveExact(SelectedSessionIdentity session) => false;
    }

    /// <summary>An inventory loader nobody reaches, because no session is published.</summary>
    private sealed class UnreachableInventory : ICharacterInventoryEntryDomainService
    {
        public ValueTask<CharacterInventoryEntryState> LoadAsync(
            AccountId accountId, CharacterId characterId, CancellationToken cancellationToken) =>
            throw new NotSupportedException(
                "The inventory is loaded only for a session that is published.");
    }

}
