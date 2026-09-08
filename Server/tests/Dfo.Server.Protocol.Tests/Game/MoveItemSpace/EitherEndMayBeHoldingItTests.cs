using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.MoveItemSpace;
using Microsoft.Extensions.Logging;
using Dfo.Server.Data.Game;

namespace Dfo.Server.Protocol.Tests.Game.MoveItemSpace;

/// <summary>
/// Which of a move's two ends is holding the item, and what follows from not knowing in advance.
/// </summary>
/// <remarks>
/// A request names two ends and does not say which one the item is on. The client puts it on the
/// end the drag started from, so a drag out of the bag names it first and a drag off the character
/// names it second -- both observed live, on 2026-09-06 and 2026-09-08 respectively.
/// <para>
/// The move inside one list has read both ends since before this file existed
/// (<c>StoreBackedItemContainer.MoveToAsync</c>). The three paths that cross two lists did not, so
/// every drag arriving the second way was refused as though the inventory were empty -- which is
/// what a player taking a costume off was answered with, `BothSlotsEmpty` naming no item at all.
/// </para>
/// <para>
/// The pairs these cases walk are not written out here. They are read back from the dispatch by
/// asking it about every pair of list numbers and keeping the ones it does not turn away, so a
/// list added to or taken from the dispatch changes what is walked instead of leaving a
/// hand-written list behind. What that enumeration comes to is asserted separately, once, so the
/// change shows up as a failure rather than as silently fewer cases.
/// </para>
/// </remarks>
public sealed class EitherEndMayBeHoldingItTests(ArchiveContentFixture archive)
    : IClassFixture<ArchiveContentFixture>
{
    private const int Character = 60;

    /// <summary>A costume the archive declares under <c>[coat avatar]</c>, worn at slot 3.</summary>
    private const int Costume = 413500000;

    /// <summary>An ordinary coat, which the archive declares under <c>[coat]</c>, worn at 14.</summary>
    private const int Ordinary = 10018;

    /// <summary>An identifier the archive declares nothing under.</summary>
    private const int Unknown = 999999999;

    /// <summary>A bag slot inside the equipment band, where a costume may sit.</summary>
    private const short BagSlot = 12;

    /// <summary>A bag slot in the consumable band, where one may not.</summary>
    private const short ConsumableBagSlot = 65;

    private const short AvatarSlot = 0;

    /// <summary>Where the archive says a coat avatar is worn.</summary>
    private const short WornCostumeSlot = 3;

    private static readonly NetworkConnectionId Connection = new("either-end");

    private static readonly NetworkDispatchContext Dispatch = new(
        new NetworkConnectionContext(Connection, NetworkEndpointKind.Game, 10011));

    /// <summary>The lists the dispatch serves, as the dispatch answers rather than as a list.</summary>
    /// <remarks>
    /// Read by asking about every ordered pair of container types with both ends empty and keeping
    /// the pairs that come back as anything other than <c>ListNotServed</c> -- that refusal is
    /// decided from the two list numbers alone, before either slot is read, so an empty inventory
    /// separates served from unserved and nothing else does.
    /// </remarks>
    [Fact]
    public async Task The_dispatch_serves_these_pairs_of_lists_and_no_others()
    {
        var served = await this.ServedPairsAsync();

        Assert.Equal(
            [
                (InventoryContainerType.Main, InventoryContainerType.Main),
                (InventoryContainerType.Main, InventoryContainerType.Avatar),
                (InventoryContainerType.Main, InventoryContainerType.Equipment),
                (InventoryContainerType.Avatar, InventoryContainerType.Main),
                (InventoryContainerType.Avatar, InventoryContainerType.Avatar),
                (InventoryContainerType.Avatar, InventoryContainerType.Equipment),
                (InventoryContainerType.Equipment, InventoryContainerType.Main),
                (InventoryContainerType.Equipment, InventoryContainerType.Avatar),
            ],
            served);
    }

    /// <summary>
    /// For every served pair that crosses two lists, the end that is holding something is the end
    /// that moves.
    /// </summary>
    [Fact]
    public async Task Whichever_end_holds_the_item_is_the_end_that_moves()
    {
        foreach (var (first, second) in await this.CrossListPairsAsync())
        {
            var firstSlot = SlotIn(first);
            var secondSlot = SlotIn(second);
            var side = this.Build();
            side.Seed(second, secondSlot, Costume);

            var result = await side.MoveAsync(first, firstSlot, second, secondSlot);

            Assert.Equal(ItemMoveStatus.Moved, result.Status);
            Assert.Equal(Costume, result.ItemId);
            Assert.Equal(1, result.MoveValue);
            Assert.Equal(Costume, (await side.ReadAsync(first, firstSlot))!.ItemId);
            Assert.Null(await side.ReadAsync(second, secondSlot));
        }
    }

    /// <summary>The forward direction of each of those pairs still works, unchanged.</summary>
    [Fact]
    public async Task The_end_named_first_still_moves_when_it_is_the_one_holding_the_item()
    {
        foreach (var (first, second) in await this.CrossListPairsAsync())
        {
            var firstSlot = SlotIn(first);
            var secondSlot = SlotIn(second);
            var side = this.Build();
            side.Seed(first, firstSlot, Costume);

            var result = await side.MoveAsync(first, firstSlot, second, secondSlot);

            Assert.Equal(ItemMoveStatus.Moved, result.Status);
            Assert.Equal(Costume, result.ItemId);
            Assert.Equal(Costume, (await side.ReadAsync(second, secondSlot))!.ItemId);
            Assert.Null(await side.ReadAsync(first, firstSlot));
        }
    }

    /// <summary>
    /// A costume taken off into a bag slot that will not have it is refused the same way whichever
    /// end the client named it on.
    /// </summary>
    /// <remarks>
    /// The consumable band takes no equipment. This is the rule that would be lost if reversing a
    /// move meant writing it without asking, and the pair of assertions is the point: the reversed
    /// request and the forward one are refused alike, so nothing was widened to make the reversal
    /// work.
    /// </remarks>
    [Fact]
    public async Task A_reversed_take_off_is_refused_by_the_bag_rule_the_forward_one_obeys()
    {
        var reversed = this.Build();
        reversed.Seed(InventoryContainerType.Equipment, WornCostumeSlot, Costume);

        var whenNamedSecond = await reversed.MoveAsync(
            InventoryContainerType.Main, ConsumableBagSlot,
            InventoryContainerType.Equipment, WornCostumeSlot);

        var forward = this.Build();
        forward.Seed(InventoryContainerType.Equipment, WornCostumeSlot, Costume);

        var whenNamedFirst = await forward.MoveAsync(
            InventoryContainerType.Equipment, WornCostumeSlot,
            InventoryContainerType.Main, ConsumableBagSlot);

        Assert.Equal(ItemMoveStatus.ItemCannotOccupySlot, whenNamedFirst.Status);
        Assert.Equal(whenNamedFirst.Status, whenNamedSecond.Status);
        Assert.Equal(Costume, whenNamedSecond.ItemId);
        Assert.Null(await reversed.ReadAsync(InventoryContainerType.Main, ConsumableBagSlot));
        Assert.Equal(
            Costume,
            (await reversed.ReadAsync(InventoryContainerType.Equipment, WornCostumeSlot))!.ItemId);
    }

    /// <summary>
    /// A piece put on a slot its archive entry does not name is refused the same way whichever end
    /// the client named it on.
    /// </summary>
    [Fact]
    public async Task A_reversed_put_on_is_refused_by_the_slot_rule_the_forward_one_obeys()
    {
        var reversed = this.Build();
        reversed.Seed(InventoryContainerType.Main, BagSlot, Ordinary);

        var whenNamedSecond = await reversed.MoveAsync(
            InventoryContainerType.Equipment, WornCostumeSlot,
            InventoryContainerType.Main, BagSlot);

        var forward = this.Build();
        forward.Seed(InventoryContainerType.Main, BagSlot, Ordinary);

        var whenNamedFirst = await forward.MoveAsync(
            InventoryContainerType.Main, BagSlot,
            InventoryContainerType.Equipment, WornCostumeSlot);

        Assert.Equal(ItemMoveStatus.ItemCannotOccupySlot, whenNamedFirst.Status);
        Assert.Equal(whenNamedFirst.Status, whenNamedSecond.Status);
        Assert.Equal(Ordinary, whenNamedSecond.ItemId);
        Assert.Null(await reversed.ReadAsync(InventoryContainerType.Equipment, WornCostumeSlot));
        Assert.Equal(
            Ordinary, (await reversed.ReadAsync(InventoryContainerType.Main, BagSlot))!.ItemId);
    }

    /// <summary>
    /// A reversed carry-across is refused by the receiving list's rule, the same as the forward one.
    /// </summary>
    /// <remarks>
    /// The avatar list takes costumes and nothing else. Without this the reversed carry-across
    /// would be walked only where it succeeds, and the third of the three paths would be the one
    /// with no evidence that its rules survived the reversal.
    /// </remarks>
    [Fact]
    public async Task A_reversed_carry_across_is_refused_by_the_list_rule_the_forward_one_obeys()
    {
        var reversed = this.Build();
        reversed.Seed(InventoryContainerType.Main, BagSlot, Ordinary);

        var whenNamedSecond = await reversed.MoveAsync(
            InventoryContainerType.Avatar, AvatarSlot, InventoryContainerType.Main, BagSlot);

        var forward = this.Build();
        forward.Seed(InventoryContainerType.Main, BagSlot, Ordinary);

        var whenNamedFirst = await forward.MoveAsync(
            InventoryContainerType.Main, BagSlot, InventoryContainerType.Avatar, AvatarSlot);

        Assert.Equal(ItemMoveStatus.ItemCannotOccupySlot, whenNamedFirst.Status);
        Assert.Equal(whenNamedFirst.Status, whenNamedSecond.Status);
        Assert.Equal(Ordinary, whenNamedSecond.ItemId);
        Assert.Null(await reversed.ReadAsync(InventoryContainerType.Avatar, AvatarSlot));
        Assert.Equal(
            Ordinary, (await reversed.ReadAsync(InventoryContainerType.Main, BagSlot))!.ItemId);
    }

    /// <summary>
    /// A template the archive names no slot for is refused on the reversed path too.
    /// </summary>
    /// <remarks>
    /// <c>NotWearable</c> is the refusal left for a template the archive declares nothing wearable
    /// under, and it is asked through the same call the forward path asks it through. Walking it
    /// from the reversed side is what shows the delegation reaches the rule rather than going
    /// around it.
    /// </remarks>
    [Fact]
    public async Task A_template_the_archive_does_not_know_is_refused_on_the_reversed_path()
    {
        var side = this.Build();
        side.Seed(InventoryContainerType.Main, BagSlot, Unknown);

        var result = await side.MoveAsync(
            InventoryContainerType.Equipment, WornCostumeSlot,
            InventoryContainerType.Main, BagSlot);

        Assert.Equal(ItemMoveStatus.NotWearable, result.Status);
        Assert.Equal(Unknown, result.ItemId);
        Assert.Null(await side.ReadAsync(InventoryContainerType.Equipment, WornCostumeSlot));
    }

    /// <summary>Two empty ends are still two empty ends.</summary>
    [Fact]
    public async Task Neither_end_holding_anything_is_still_refused_as_both_slots_empty()
    {
        foreach (var (first, second) in await this.CrossListPairsAsync())
        {
            var side = this.Build();

            var result = await side.MoveAsync(first, SlotIn(first), second, SlotIn(second));

            Assert.Equal(ItemMoveStatus.BothSlotsEmpty, result.Status);
            Assert.Equal(0, result.ItemId);
        }
    }

    /// <summary>A list this server does not serve is still turned away.</summary>
    /// <remarks>
    /// The refusal is decided from the two list numbers before either slot is read, but that is
    /// read off the dispatch rather than observed here: what these assertions can see is the
    /// refusal and a store nobody touched.
    /// </remarks>
    [Fact]
    public async Task An_unserved_list_is_still_refused()
    {
        var side = this.Build();
        side.Seed(InventoryContainerType.Equipment, WornCostumeSlot, Costume);

        var fromCargo = await side.MoveAsync(
            InventoryContainerType.PersonalCargo, 0,
            InventoryContainerType.Equipment, WornCostumeSlot);
        var wornToWorn = await side.MoveAsync(
            InventoryContainerType.Equipment, WornCostumeSlot,
            InventoryContainerType.Equipment, 4);

        Assert.Equal(ItemMoveStatus.ListNotServed, fromCargo.Status);
        Assert.Equal(ItemMoveStatus.ListNotServed, wornToWorn.Status);
        Assert.Equal(
            Costume,
            (await side.ReadAsync(InventoryContainerType.Equipment, WornCostumeSlot))!.ItemId);
    }

    /// <summary>
    /// A request restating a move the client already has is left alone rather than undone.
    /// </summary>
    /// <remarks>
    /// The three cases together are what makes this narrow. Naming the worn piece's own
    /// identifier on the empty end is the whole trigger: naming nothing lets the take-off through,
    /// and naming something else lets it through too. The identifier equality is what does that
    /// work -- the guard's "greater than zero" clause is upstream's wording carried over, and no
    /// real template identifier is zero, so widening it changes nothing here.
    /// </remarks>
    [Fact]
    public async Task A_request_naming_the_worn_piece_on_its_empty_end_does_not_take_it_off()
    {
        var restated = this.Build();
        restated.Seed(InventoryContainerType.Equipment, WornCostumeSlot, Costume);

        var echoed = await restated.MoveAsync(
            InventoryContainerType.Main, BagSlot,
            InventoryContainerType.Equipment, WornCostumeSlot,
            sourceInstanceValue: Costume);

        Assert.Equal(ItemMoveStatus.MoveAlreadyMade, echoed.Status);
        Assert.Equal(Costume, echoed.ItemId);
        Assert.Equal(0, echoed.MoveValue);
        Assert.Null(await restated.ReadAsync(InventoryContainerType.Main, BagSlot));
        Assert.Equal(
            Costume,
            (await restated.ReadAsync(InventoryContainerType.Equipment, WornCostumeSlot))!.ItemId);
    }

    [Fact]
    public async Task A_request_naming_nothing_on_its_empty_end_takes_the_piece_off()
    {
        var side = this.Build();
        side.Seed(InventoryContainerType.Equipment, WornCostumeSlot, Costume);

        var result = await side.MoveAsync(
            InventoryContainerType.Main, BagSlot,
            InventoryContainerType.Equipment, WornCostumeSlot,
            sourceInstanceValue: 0);

        Assert.Equal(ItemMoveStatus.Moved, result.Status);
        Assert.Equal(Costume, (await side.ReadAsync(InventoryContainerType.Main, BagSlot))!.ItemId);
    }

    [Fact]
    public async Task A_request_naming_some_other_piece_on_its_empty_end_takes_the_piece_off()
    {
        var side = this.Build();
        side.Seed(InventoryContainerType.Equipment, WornCostumeSlot, Costume);

        var result = await side.MoveAsync(
            InventoryContainerType.Main, BagSlot,
            InventoryContainerType.Equipment, WornCostumeSlot,
            sourceInstanceValue: Ordinary);

        Assert.Equal(ItemMoveStatus.Moved, result.Status);
        Assert.Equal(Costume, (await side.ReadAsync(InventoryContainerType.Main, BagSlot))!.ItemId);
    }

    /// <summary>
    /// The acknowledgement names the request's own two ends, and the store agrees with a client
    /// that reads it as those two cells exchanging what they hold.
    /// </summary>
    /// <remarks>
    /// Upstream answers the same way and is explicit about it: on its reverse branch it sets the
    /// result's destination back to the request's own destination
    /// (<c>InventoryMoveService</c>, read from source). A move into an empty cell and a swap with
    /// an empty cell reach the same place, so a client that applies the acknowledgement as a swap
    /// of the two cells it names is left holding what the store holds.
    /// </remarks>
    [Fact]
    public async Task The_acknowledgement_for_a_reversed_move_names_the_two_cells_the_request_did()
    {
        var (handler, sender, _, side) = this.BuildHandler();
        side.Seed(InventoryContainerType.Equipment, WornCostumeSlot, Costume);

        await handler.HandleAsync(Dispatch, TakeOffRequest(), CancellationToken.None);

        var acknowledgement = (MoveItemSpaceResponse)Assert.Single(sender.Sent);
        Assert.Null(acknowledgement.ErrorCode);
        Assert.Equal((byte)InventoryContainerType.Main, acknowledgement.SourceListType);
        Assert.Equal(BagSlot, acknowledgement.SourceSlotIndex);
        Assert.Equal((byte)InventoryContainerType.Equipment, acknowledgement.DestinationListType);
        Assert.Equal(WornCostumeSlot, acknowledgement.DestinationSlotIndex);
        Assert.Equal(1, acknowledgement.MoveValue);

        Assert.Equal(Costume, (await side.ReadAsync(InventoryContainerType.Main, BagSlot))!.ItemId);
        Assert.Null(await side.ReadAsync(InventoryContainerType.Equipment, WornCostumeSlot));
    }

    /// <summary>The line a move leaves says which lists it was between.</summary>
    /// <remarks>
    /// It named the two slots and neither list until 2026-09-08, and the same pair of numbers
    /// belongs to several different paths -- which is why the take-off defect above took three
    /// live rounds to place rather than one.
    /// </remarks>
    [Fact]
    public async Task The_line_a_move_leaves_names_the_list_at_both_ends()
    {
        var (handler, _, logger, side) = this.BuildHandler();
        side.Seed(InventoryContainerType.Equipment, WornCostumeSlot, Costume);

        await handler.HandleAsync(Dispatch, TakeOffRequest(), CancellationToken.None);

        Assert.Equal(
            $"Move item space either-end character={Character}: item {Costume} "
                + $"from list {(byte)InventoryContainerType.Main} slot {BagSlot} "
                + $"to list {(byte)InventoryContainerType.Equipment} slot {WornCostumeSlot} "
                + "count=1",
            Assert.Single(logger.Entries).Message);
    }

    /// <summary>The drag that started all of this: a costume off the character, into the bag.</summary>
    private static MoveItemSpaceRequest TakeOffRequest() =>
        new(GameMessageMetadata.Empty,
            (byte)InventoryContainerType.Main,
            BagSlot,
            SourceInstanceValue: 0,
            MoveCount: 1,
            (byte)InventoryContainerType.Equipment,
            WornCostumeSlot,
            DestinationInstanceValue: Costume);

    /// <summary>A slot in each list that a costume is allowed to occupy.</summary>
    private static short SlotIn(InventoryContainerType type) => type switch
    {
        InventoryContainerType.Main => BagSlot,
        InventoryContainerType.Avatar => AvatarSlot,
        InventoryContainerType.Equipment => WornCostumeSlot,
        _ => throw new ArgumentOutOfRangeException(
            nameof(type), type, "No costume slot was chosen for this list."),
    };

    private async ValueTask<IReadOnlyList<(InventoryContainerType First, InventoryContainerType Second)>>
        ServedPairsAsync()
    {
        var served = new List<(InventoryContainerType, InventoryContainerType)>();
        foreach (var first in Enum.GetValues<InventoryContainerType>())
        {
            foreach (var second in Enum.GetValues<InventoryContainerType>())
            {
                var side = this.Build();
                var result = await side.MoveAsync(first, 0, second, 1);
                if (result.Status is not ItemMoveStatus.ListNotServed)
                    served.Add((first, second));
            }
        }

        return served;
    }

    private async ValueTask<IReadOnlyList<(InventoryContainerType First, InventoryContainerType Second)>>
        CrossListPairsAsync() =>
        [.. (await this.ServedPairsAsync()).Where(pair => pair.First != pair.Second)];

    private Side Build()
    {
        var character = new CharacterId(Character);
        var containers = new CharacterContainerDomainService();
        foreach (var type in (InventoryContainerType[])
            [InventoryContainerType.Main, InventoryContainerType.Avatar, InventoryContainerType.Equipment])
        {
            containers.Register(character, new SlotBook(type, character));
        }

        return new Side(
            containers,
            new MoveItemSpaceCommandHandler(
                containers,
                new ItemMovementDomainService(
                    new ItemPlacementRuleService(archive.Placements, archive.EquipmentSlots),
                    new ItemSlotRuleService())),
            character);
    }

    private (MoveItemSpaceHandler Handler, RecordingSender Sender, RecordingLogger Logger, Side Side)
        BuildHandler()
    {
        var side = this.Build();
        var sender = new RecordingSender();
        var logger = new RecordingLogger();
        return (
            new MoveItemSpaceHandler(
                new OneSession(), new NoPublishedRuntime(), new UnreachableInventory(),
                new DirectMoveBus(side.Handler), sender, logger),
            sender,
            logger,
            side);
    }

    private sealed record Side(
        CharacterContainerDomainService Containers,
        MoveItemSpaceCommandHandler Handler,
        CharacterId Character)
    {
        public void Seed(InventoryContainerType type, short slotIndex, int itemId) =>
            ((SlotBook)this.Containers.GetContainer(this.Character, type)).Seed(slotIndex, itemId);

        public ValueTask<ItemMoveResult> MoveAsync(
            InventoryContainerType source,
            short sourceSlot,
            InventoryContainerType destination,
            short destinationSlot,
            int sourceInstanceValue = 0) =>
            this.Handler.HandleAsync(
                new CommandExecutionContext(
                    Guid.Empty,
                    typeof(MoveItemSpaceCommand),
                    typeof(Side),
                    CommandExecutionKind.Transactional),
                new MoveItemSpaceCommand(
                    this.Character,
                    (byte)source,
                    sourceSlot,
                    1,
                    (byte)destination,
                    destinationSlot,
                    sourceInstanceValue),
                CancellationToken.None);

        public async ValueTask<ContainerSlotItem?> ReadAsync(
            InventoryContainerType type, short slotIndex) =>
            await ((SlotBook)this.Containers.GetContainer(this.Character, type))
                .ReadSlotAsync(slotIndex, CancellationToken.None);
    }

    /// <summary>
    /// A container that is a book of slots and refuses everything the served paths do not use.
    /// </summary>
    /// <remarks>
    /// What is under test is which end a move reads and what it does with the answer, so nothing
    /// here has to settle or persist. The unused members raise rather than answering emptily: a
    /// path that started using one of them fails here instead of being quietly told the container
    /// is empty, which is the shape of the defect these cases exist for.
    /// </remarks>
    private sealed class SlotBook(InventoryContainerType type, CharacterId character) : IItemContainer
    {
        private readonly Dictionary<int, ContainerSlotItem> bySlot = [];

        public ContainerId Id { get; } = ContainerId.ForCharacter(character, type);

        public InventoryContainerType Type => type;

        public ContainerShape Shape { get; } = new(type, 0);

        public bool IsRemovable => false;

        public void Seed(int slotIndex, int itemId) =>
            this.bySlot[slotIndex] = new ContainerSlotItem(
                slotIndex,
                StackableItemCore.Create(1, itemId, 1),
                ItemWorldClass.Equipment);

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
            this.bySlot[slotIndex] = new ContainerSlotItem(
                slotIndex, core, ItemWorldClass.Equipment);
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
            var moving = this.bySlot.GetValueOrDefault(fromSlotIndex)
                ?? this.bySlot.GetValueOrDefault(toSlotIndex);
            return ValueTask.FromResult<ItemMoveOutcome>(moving is null
                ? new ItemMoveOutcome.Refused(ItemMoveRefusal.BothSlotsEmpty)
                : new ItemMoveOutcome.Moved(count, moving.ItemId));
        }

        public ValueTask<IReadOnlyList<InventoryItemState>> SnapshotAsync(
            CancellationToken cancellationToken) => throw Unused();

        public ValueTask<IReadOnlyList<ContainerSlotItem>> ReadAllAsync(
            CancellationToken cancellationToken) => throw Unused();

        public ValueTask<ItemPlacementOutcome> AddAsync(
            int itemId, int count, CancellationToken cancellationToken) => throw Unused();

        public ValueTask<ItemPlacementOutcome> RefillAsync(
            ItemRefill refill, CancellationToken cancellationToken) => throw Unused();

        private static NotSupportedException Unused() =>
            new("This container answers slot reads, writes and moves and nothing else.");
    }

    /// <summary>A bus that runs the move command on the handler that owns it.</summary>
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
            throw new InvalidOperationException("The move acknowledgement is sent explicitly.");

        public ValueTask SendNotiAsync(
            NetworkConnectionId connectionId,
            IGameNotiMessage message,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The move acknowledgement is a CMD.");

        public ValueTask SendExplicitAsync(
            NetworkConnectionId requiredDirectConnectionId,
            GameExplicitMessageDelivery delivery,
            CancellationToken cancellationToken)
        {
            this.Sent.AddRange(delivery.RequiredDirectMessages);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>The log, kept as the sentences it would have written.</summary>
    private sealed class RecordingLogger : ILogger<MoveItemSpaceHandler>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            this.Entries.Add((logLevel, formatter(state, exception)));
    }

    /// <summary>A session directory that has nothing published, so no appearance frame is built.</summary>
    /// <remarks>
    /// These cases are about the acknowledgement and the log line. The frame that follows a move
    /// which changed the character's look is asserted where it belongs, and it is skipped here
    /// rather than mocked: a session that cannot be read is exactly the case the handler answers by
    /// sending the acknowledgement alone.
    /// </remarks>
    private sealed class NoPublishedRuntime : ISelectedSessionRuntimeDirectory
    {
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
