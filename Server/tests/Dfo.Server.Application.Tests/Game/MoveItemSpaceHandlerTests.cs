using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
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
            MoveItemSpaceResponse.InvalidOperation,
            ((MoveItemSpaceResponse)sender.Sent.Single()).ErrorCode);
    }

    /// <remarks>
    /// Cargo, equipment, pets and the rest each carry their own rules about what may enter them.
    /// Answering for them without those rules would open a door that should have stayed shut.
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
            MoveItemSpaceResponse.InvalidOperation,
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

    [Fact]
    public async Task Two_empty_slots_are_refused()
    {
        var (handler, sender) = Build(new FakeInventory());

        await handler.HandleAsync(Context, Request(65, 66, 0, 0), CancellationToken.None);

        Assert.Equal(
            MoveItemSpaceResponse.Rejected,
            ((MoveItemSpaceResponse)sender.Sent.Single()).ErrorCode);
    }

    private static readonly NetworkDispatchContext Context = new(
        new NetworkConnectionContext(new NetworkConnectionId("game-move"), NetworkEndpointKind.Game, 10011));

    private static MoveItemSpaceRequest Request(
        short sourceSlot, short destinationSlot, int sourceInstance, int destinationInstance) =>
        new(GameMessageMetadata.Empty, 0, sourceSlot, sourceInstance, 0, 0, destinationSlot,
            destinationInstance);

    private static (MoveItemSpaceHandler Handler, RecordingSender Sender) Build(FakeInventory inventory)
    {
        var sender = new RecordingSender();
        return (
            new MoveItemSpaceHandler(
                new OneSession(),
                Containers(inventory),
                new ConsumablePlacements(),
                new ItemSlotRuleService(),
                sender,
                NullLogger<MoveItemSpaceHandler>.Instance),
            sender);
    }


    /// <summary>Binds the fixture's storage to the session's character as its main container.</summary>
    private static ICharacterContainerDomainService Containers(
        IEntryInventoryMutationRepository storage)
    {
        var characterId = new CharacterId(60);
        var containers = new CharacterContainerDomainService();
        containers.Register(characterId, new PersistentItemContainer(
            new ContainerId("character:60:Main"),
            characterId,
            new ContainerShape(InventoryContainerType.Main, 0),
            storage));
        return containers;
    }

    /// <summary>Every item here is an ordinary consumable: kind 2, band 65 to 120.</summary>
    private sealed class ConsumablePlacements : IItemPlacementDefinitionSource
    {
        public ValueTask<ItemPlacement?> GetAsync(int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ItemPlacement?>(new ItemPlacement(2, 100, 65, 120));
    }

    private sealed class FakeInventory : IEntryInventoryMutationRepository
    {

        public ValueTask<IReadOnlyList<InventoryItemState>> ReadMainItemsAsync(
            CharacterId characterId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("This fixture covers moves only.");

        /// <summary>Not exercised here: this fixture does not consume items.</summary>
        public ValueTask<MainInventoryOccupant?> ReadMainSlotAsync(
            CharacterId characterId, int slotIndex, CancellationToken cancellationToken) =>
            throw new NotSupportedException("This fixture covers moves only.");

        /// <summary>Not exercised here: this fixture does not consume items.</summary>
        public ValueTask<ConsumedInventoryItem?> ConsumeMainItemAsync(
            CharacterId characterId, long storageEntryId, int count,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("This fixture covers moves only.");
        private readonly Dictionary<int, (int ItemId, int Count)> slots = [];
        private readonly Dictionary<long, int> entries = [];
        private long nextEntryId = 1;

        public (int ItemId, int Count) this[int slotIndex]
        {
            set
            {
                slots[slotIndex] = value;
                entries[nextEntryId++] = slotIndex;
            }
        }

        public List<(int Slot, int ItemId, int Count)> Rows() =>
            [.. slots.OrderBy(pair => pair.Key)
                .Select(pair => (pair.Key, pair.Value.ItemId, pair.Value.Count))];

        public ValueTask<MainInventorySlotPair> ReadMainSlotsAsync(
            CharacterId characterId, int sourceSlotIndex, int destinationSlotIndex,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new MainInventorySlotPair(
                Occupant(sourceSlotIndex), Occupant(destinationSlotIndex)));

        public ValueTask<bool> ApplyMainMoveAsync(
            CharacterId characterId, MainInventoryMovePlan plan, CancellationToken cancellationToken)
        {
            if (!entries.TryGetValue(plan.MovingEntryId, out var fromSlot))
                return ValueTask.FromResult(false);

            var moving = slots[fromSlot];
            var displaced = slots.TryGetValue(plan.TargetSlotIndex, out var value)
                ? value
                : ((int ItemId, int Count)?)null;

            switch (plan.Kind)
            {
                case MainInventoryMoveKind.Relocate:
                    slots.Remove(fromSlot);
                    slots[plan.TargetSlotIndex] = moving;
                    entries[plan.MovingEntryId] = plan.TargetSlotIndex;
                    break;
                case MainInventoryMoveKind.Split:
                    slots[fromSlot] = (moving.ItemId, moving.Count - plan.MoveCount);
                    slots[plan.TargetSlotIndex] = (moving.ItemId, plan.MoveCount);
                    entries[nextEntryId++] = plan.TargetSlotIndex;
                    break;
                case MainInventoryMoveKind.Swap:
                    slots[fromSlot] = displaced!.Value;
                    slots[plan.TargetSlotIndex] = moving;
                    entries[plan.MovingEntryId] = plan.TargetSlotIndex;
                    entries[plan.DisplacedEntryId!.Value] = fromSlot;
                    break;
                case MainInventoryMoveKind.Merge:
                    slots[plan.TargetSlotIndex] =
                        (displaced!.Value.ItemId, displaced.Value.Count + plan.MoveCount);
                    if (plan.MoveCount == moving.Count)
                    {
                        slots.Remove(fromSlot);
                        entries.Remove(plan.MovingEntryId);
                    }
                    else
                    {
                        slots[fromSlot] = (moving.ItemId, moving.Count - plan.MoveCount);
                    }
                    break;
                default:
                    return ValueTask.FromResult(false);
            }

            return ValueTask.FromResult(true);
        }

        private MainInventoryOccupant? Occupant(int slotIndex) =>
            slots.TryGetValue(slotIndex, out var item)
                ? new MainInventoryOccupant(
                    entries.First(pair => pair.Value == slotIndex).Key, slotIndex, 2,
                    item.ItemId, item.Count)
                : null;

        public ValueTask<GrantedInventoryItem?> GrantItemAsync(
            CharacterId characterId, int itemId, int count, ItemPlacement placement,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("This fixture covers moves only.");

        public ValueTask<DailyRefillMutationResult> ApplyDailyRefillAsync(
            AccountId accountId, CharacterId characterId, IReadOnlyList<DailyRefillRule> rules,
            DateTimeOffset preparedAt, CancellationToken cancellationToken) =>
            throw new NotSupportedException("This fixture covers moves only.");
    }

    private sealed class OneSession : ISelectedSessionDirectory
    {
        private static readonly SelectedSessionIdentity Session = SelectedSessionIdentity.Create(
            new AccountId(6), new CharacterId(60), new NetworkConnectionId("game-move"), 1, 10011);

        public SelectedSessionIdentity? RegisterReplacing(SelectedSessionIdentity session) => null;
        public bool TryRemoveExact(SelectedSessionIdentity session) => false;
        public SelectedSessionIdentity? Find(AccountId accountId) => Session;
        public SelectedSessionIdentity? FindByCharacter(CharacterId characterId) => Session;
        public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId) => Session;
        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId) => Session;
        public IReadOnlyList<SelectedSessionIdentity> Current() => [Session];
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
}
