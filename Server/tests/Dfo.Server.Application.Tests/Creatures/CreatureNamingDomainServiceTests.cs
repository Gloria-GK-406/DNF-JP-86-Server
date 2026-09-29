using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Creatures;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Tests.Creatures;

public sealed class CreatureNamingDomainServiceTests
{
    private static readonly SelectedSessionIdentity Session = SelectedSessionIdentity.Create(
        new AccountId(1), new CharacterId(2), new NetworkConnectionId("rename"), 1, 10011);
    private static readonly BusinessSlot CardSlot = new(BusinessInventorySpace.CreatureConsumable, 0);

    [Fact]
    public async Task Valid_card_renames_equipped_uid_and_consumes_one_card()
    {
        var store = new FakeCreatureStore();
        var service = new CreatureNamingDomainService(new FakeCreatureDomain(store), new FakeSessions(), TimeProvider.System);
        var receipt = await service.RenameAsync(Session, CardSlot, [0x82, 0xA0], default);

        Assert.NotNull(receipt);
        Assert.Equal(new byte[] { 0x82, 0xA0 }, receipt.Creature.NameBytes);
        Assert.Equal(1, receipt.RemainingSourceCore!.Value);
        Assert.Equal(1, store.Card.Item!.Core.Value);
        Assert.Equal(1, store.Saves);
    }

    [Theory]
    [InlineData(24, 500)]
    [InlineData(25, 499)]
    public async Task Wrong_card_or_equipped_uid_cannot_rename_for_free(int cardId, int wornUid)
    {
        var store = new FakeCreatureStore(cardId, wornUid);
        var service = new CreatureNamingDomainService(new FakeCreatureDomain(store), new FakeSessions(), TimeProvider.System);
        var receipt = await service.RenameAsync(Session, CardSlot, [0x41], default);

        Assert.Null(receipt);
        Assert.Equal(0, store.Saves);
        Assert.Equal(2, store.Card.Item!.Core.Value);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Expired_card_or_worn_creature_cannot_rename_or_consume(bool expiredCard)
    {
        var store = new FakeCreatureStore(cardExpiry: expiredCard ? 1 : 0,
            wornExpiry: expiredCard ? 0 : 1);
        var service = new CreatureNamingDomainService(new FakeCreatureDomain(store),
            new FakeSessions(), TimeProvider.System);

        var receipt = await service.RenameAsync(Session, CardSlot, [0x41], default);

        Assert.Null(receipt);
        Assert.Equal(0, store.Saves);
        Assert.Equal(2, store.Card.Item!.Core.Value);
    }

    private sealed class FakeCreatureDomain(FakeCreatureStore store) : ICreatureDomainService
    {
        public ICreatureStore GetStore(CharacterId characterId) => store;
    }

    private sealed class FakeCreatureStore(int cardId = 25, int wornUid = 500,
        int cardExpiry = 0, int wornExpiry = 0) : ICreatureStore
    {
        public FakeContainer Card { get; } = new(BusinessInventorySpace.CreatureConsumable,
            new ContainerSlotItem(0, StackableItemCore.Create(7, cardId, 2) with
                { ExpireTime = cardExpiry }, ItemWorldClass.Interchangeable));
        private FakeContainer Worn { get; } = new(BusinessInventorySpace.WornCreature,
            new ContainerSlotItem(0, StackableItemCore.Create(5, 63023, wornUid) with
                { ExpireTime = wornExpiry }, ItemWorldClass.Creature));
        private CreatureState Current { get; set; } = new(500, 63023, 100, 0, 1, [], 0, 0, 0, 0);
        public int Saves { get; private set; }
        public BusinessInventoryStoreKind Kind => BusinessInventoryStoreKind.Creature;
        public IBusinessItemContainer? FindContainer(BusinessInventorySpace space) => space switch
        {
            BusinessInventorySpace.CreatureConsumable => Card,
            BusinessInventorySpace.WornCreature => Worn,
            _ => null,
        };
        public IBusinessItemContainer GetContainer(BusinessInventorySpace space) =>
            FindContainer(space) ?? throw new InvalidOperationException();
        public ValueTask<CreatureState?> FindAsync(int itemUid, CancellationToken ct) =>
            ValueTask.FromResult<CreatureState?>(itemUid == Current.ItemUid ? Current : null);
        public ValueTask<IReadOnlyList<CreatureState>> ReadAllAsync(CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<CreatureState>>([Current]);
        public ValueTask<CreatureState?> CurrentAsync(CancellationToken ct) =>
            ValueTask.FromResult<CreatureState?>(Current);
        public ValueTask SaveAsync(CreatureState creature, CancellationToken ct)
        {
            Current = creature;
            Saves++;
            return ValueTask.CompletedTask;
        }
        public ValueTask ReviveIfEmptyAsync(int itemUid, CancellationToken ct) => ValueTask.CompletedTask;
    }

    private sealed class FakeContainer(BusinessInventorySpace space, ContainerSlotItem? item)
        : IBusinessItemContainer
    {
        public ContainerSlotItem? Item { get; private set; } = item;
        public BusinessContainerId Id => new(space, StorageId);
        public ContainerId StorageId => new("rename-card");
        public BusinessInventorySpace Space => space;
        public ValueTask<ContainerSlotItem?> ReadSlotAsync(int index, CancellationToken ct) =>
            ValueTask.FromResult(index == 0 ? Item : null);
        public ValueTask<IReadOnlyList<ContainerSlotItem>> ReadAllAsync(CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<ContainerSlotItem>>(Item is null ? [] : [Item]);
        public ValueTask PutAsync(int index, InventoryItemCoreState core, CancellationToken ct)
        {
            Item = new ContainerSlotItem(index, core, ItemWorldClass.Interchangeable);
            return ValueTask.CompletedTask;
        }
        public ValueTask PutTransferredAsync(int index, InventoryItemCoreState core,
            IBusinessItemContainer source, int sourceIndex, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask PutNewAsync(int index, InventoryItemCoreState core, InventoryItemCreationFacts facts,
            CancellationToken ct) => throw new NotSupportedException();
        public ValueTask ClearAsync(int index, CancellationToken ct)
        {
            Item = null;
            return ValueTask.CompletedTask;
        }
        public ValueTask FlushAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask<BusinessItemAdditionResult> AddAsync(int itemId, int count,
            CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeSessions : ISelectedSessionDomainService
    {
        public SelectedSessionIdentity? Select(SelectedSessionIdentity session) => session;
        public bool ReleaseExact(SelectedSessionIdentity session) => true;
        public SelectedSessionIdentity? Find(AccountId accountId) => Session;
        public SelectedSessionIdentity? FindByCharacter(CharacterId characterId) => Session;
        public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId) => Session;
        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId) => Session;
    }
}
