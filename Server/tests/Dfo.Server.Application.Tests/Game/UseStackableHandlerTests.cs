using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.UseStackable;
using Microsoft.Extensions.Logging.Abstractions;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Tests.Game;

/// <summary>
/// What the server does when the player drinks a potion.
/// </summary>
/// <remarks>
/// Observed live on 2026-08-18: the health came back but the count did not move, because nothing
/// answered the request. The client applies the recovery itself and decrements its own pile by
/// one; the acknowledgement is what lets that decrement stand.
/// </remarks>
public sealed class UseStackableHandlerTests
{
    [Fact]
    public async Task One_item_leaves_the_pile()
    {
        var inventory = new FakeInventory(slot: 3, itemId: 8474, count: 2, itemKind: 2);
        var (handler, sender) = Build(inventory);

        await handler.HandleAsync(Context, Request(3, 8474), CancellationToken.None);

        Assert.Equal(1, inventory.Count);
        Assert.Null(((UseStackableResponse)sender.Sent.Single()).ErrorCode);
    }

    /// <remarks>
    /// Exactly one, whatever the pile holds. Consuming a whole stack on one key press is the shape
    /// the client would never recover from: it decremented by one and has no way to learn better.
    /// </remarks>
    [Fact]
    public async Task A_large_pile_loses_only_one()
    {
        var inventory = new FakeInventory(slot: 3, itemId: 8474, count: 99, itemKind: 2);
        var (handler, _) = Build(inventory);

        await handler.HandleAsync(Context, Request(3, 8474), CancellationToken.None);

        Assert.Equal(98, inventory.Count);
    }

    [Fact]
    public async Task The_last_of_a_pile_empties_the_slot()
    {
        var inventory = new FakeInventory(slot: 3, itemId: 8474, count: 1, itemKind: 2);
        var (handler, sender) = Build(inventory);

        await handler.HandleAsync(Context, Request(3, 8474), CancellationToken.None);

        Assert.True(inventory.IsEmpty);
        Assert.Null(((UseStackableResponse)sender.Sent.Single()).ErrorCode);
    }

    /// <remarks>
    /// The client names what it believes it is using. A slot holding something else means the two
    /// disagree, and deleting anyway would settle that in favour of whichever one is wrong.
    /// </remarks>
    [Fact]
    public async Task A_slot_holding_a_different_item_is_refused()
    {
        var inventory = new FakeInventory(slot: 3, itemId: 9000, count: 1, itemKind: 2);
        var (handler, sender) = Build(inventory);

        await handler.HandleAsync(Context, Request(3, 8474), CancellationToken.None);

        Assert.Equal(1, inventory.Count);
        Assert.Equal(
            UseStackableResponse.InvalidItem,
            ((UseStackableResponse)sender.Sent.Single()).ErrorCode);
    }

    [Fact]
    public async Task An_empty_slot_is_refused()
    {
        var (handler, sender) = Build(new FakeInventory(slot: 3, itemId: 8474, count: 0, itemKind: 2));

        await handler.HandleAsync(Context, Request(3, 8474), CancellationToken.None);

        Assert.Equal(
            UseStackableResponse.InvalidItem,
            ((UseStackableResponse)sender.Sent.Single()).ErrorCode);
    }

    /// <remarks>
    /// Gear can be dragged onto a quick slot like anything else. Without this check a key press
    /// there would destroy it one press at a time.
    /// </remarks>
    [Fact]
    public async Task Equipment_is_refused()
    {
        var inventory = new FakeInventory(slot: 3, itemId: 1000, count: 1, itemKind: 1);
        var (handler, sender) = Build(inventory);

        await handler.HandleAsync(Context, Request(3, 1000), CancellationToken.None);

        Assert.Equal(1, inventory.Count);
        Assert.Equal(
            UseStackableResponse.InvalidItem,
            ((UseStackableResponse)sender.Sent.Single()).ErrorCode);
    }

    [Fact]
    public async Task A_list_other_than_the_main_one_is_refused()
    {
        var inventory = new FakeInventory(slot: 3, itemId: 8474, count: 1, itemKind: 2);
        var (handler, sender) = Build(inventory);

        await handler.HandleAsync(
            Context, Request(3, 8474) with { ListType = 7 }, CancellationToken.None);

        Assert.Equal(1, inventory.Count);
        Assert.Equal(
            UseStackableResponse.InvalidItem,
            ((UseStackableResponse)sender.Sent.Single()).ErrorCode);
    }

    /// <remarks>
    /// The reference server echoes it back untouched. Inventing a value here would answer a
    /// question the client did not ask.
    /// </remarks>
    [Fact]
    public async Task The_instance_value_comes_back_unchanged()
    {
        var (handler, sender) = Build(new FakeInventory(slot: 3, itemId: 8474, count: 1, itemKind: 2));

        await handler.HandleAsync(Context, Request(3, 8474), CancellationToken.None);

        Assert.Equal(25333, ((UseStackableResponse)sender.Sent.Single()).InstanceValue);
    }

    private static readonly NetworkDispatchContext Context = new(
        new NetworkConnectionContext(new NetworkConnectionId("game-use"), NetworkEndpointKind.Game, 10011));

    private static UseStackableRequest Request(short slot, int itemCode) =>
        new(GameMessageMetadata.Empty, slot, 0, 25333, itemCode);

    private static (UseStackableHandler Handler, RecordingSender Sender) Build(FakeInventory inventory)
    {
        var sender = new RecordingSender();
        return (
            new UseStackableHandler(
                new OneSession(),
                Containers(inventory),
                new KindFromInventory(inventory),
                sender,
                NullLogger<UseStackableHandler>.Instance),
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

    /// <summary>Reports the placement of whatever the fixture holds, so the two cannot disagree.</summary>
    private sealed class KindFromInventory(FakeInventory inventory) : IItemPlacementDefinitionSource
    {
        public ValueTask<ItemPlacement?> GetAsync(int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ItemPlacement?>(
                inventory.ItemKind == 1
                    ? new ItemPlacement(1, 1, 9, 64)
                    : new ItemPlacement(2, 100, 65, 120));
    }

    private sealed class FakeInventory(int slot, int itemId, int count, byte itemKind)
        : IEntryInventoryMutationRepository
    {
        public int Count { get; private set; } = count;

        public byte ItemKind => itemKind;

        public bool IsEmpty => Count == 0;

        public ValueTask<IReadOnlyList<InventoryItemState>> ReadMainItemsAsync(
            CharacterId characterId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("This fixture covers use only.");

        public ValueTask<MainInventoryOccupant?> ReadMainSlotAsync(
            CharacterId characterId, int slotIndex, CancellationToken cancellationToken) =>
            ValueTask.FromResult(
                slotIndex == slot && Count > 0
                    ? new MainInventoryOccupant(1, slot, itemKind, itemId, Count)
                    : null);

        public ValueTask<ConsumedInventoryItem?> ConsumeMainItemAsync(
            CharacterId characterId, long storageEntryId, int consumed,
            CancellationToken cancellationToken)
        {
            if (storageEntryId != 1 || consumed > Count)
                return ValueTask.FromResult<ConsumedInventoryItem?>(null);
            Count -= consumed;
            return ValueTask.FromResult<ConsumedInventoryItem?>(
                new ConsumedInventoryItem(slot, itemId, Count));
        }

        public ValueTask<MainInventorySlotPair> ReadMainSlotsAsync(
            CharacterId characterId, int sourceSlotIndex, int destinationSlotIndex,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("This fixture covers use only.");

        public ValueTask<bool> ApplyMainMoveAsync(
            CharacterId characterId, MainInventoryMovePlan plan, CancellationToken cancellationToken) =>
            throw new NotSupportedException("This fixture covers use only.");

        public ValueTask<GrantedInventoryItem?> GrantItemAsync(
            CharacterId characterId, int grantedItemId, int grantedCount, ItemPlacement placement,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("This fixture covers use only.");

        public ValueTask<DailyRefillMutationResult> ApplyDailyRefillAsync(
            AccountId accountId, CharacterId characterId, IReadOnlyList<DailyRefillRule> rules,
            DateTimeOffset preparedAt, CancellationToken cancellationToken) =>
            throw new NotSupportedException("This fixture covers use only.");
    }

    private sealed class OneSession : ISelectedSessionDirectory
    {
        private static readonly SelectedSessionIdentity Session = SelectedSessionIdentity.Create(
            new AccountId(6), new CharacterId(60), new NetworkConnectionId("game-use"), 1, 10011);

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
            throw new InvalidOperationException("The use acknowledgement is sent explicitly.");

        public ValueTask SendNotiAsync(
            NetworkConnectionId connectionId, IGameNotiMessage message,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The use acknowledgement is a CMD.");

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
