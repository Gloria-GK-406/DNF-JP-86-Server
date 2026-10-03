using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Tests.Support;
namespace Dfo.Server.Application.Tests.Creatures;

internal sealed class TestCreatureContainer : IBusinessItemContainer
{
    private readonly IItemDomainStoreSession session;
    public IItemLifecycleDomainService Lifecycle { get; }
    public TestCreatureContainer(BusinessInventorySpace space, ItemSnapshot item)
    {
        Space = space;
        var backend = new VolatileItemDomainStore();
        backend.Seed(StorageId, Physical(0), item);
        var scope = new DomainTransactionScope();
        _ = scope.Begin();
        session = backend.SessionIn(scope.Current!);
        Lifecycle = new Dfo.Server.Application.Inventory.ItemLifecycleDomainService(backend, scope, null!);
    }
    private int Physical(int index) => Space == BusinessInventorySpace.WornCreature ? 25 : index;
    public ItemReference? Item => GetAsync(0, default).GetAwaiter().GetResult();
    public BusinessContainerId Id => new(Space, StorageId);
    public ContainerId StorageId => ContainerId.ForCharacter(new(2), Space == BusinessInventorySpace.WornCreature ? InventoryContainerType.Equipment : InventoryContainerType.Pet);
    public BusinessInventorySpace Space { get; }
    public ValueTask<IEquipmentInstance?> GetEquipmentAsync(int index, CancellationToken ct) => ValueTask.FromResult<IEquipmentInstance?>(null);
    public async ValueTask<ItemReference?> GetAsync(int index, CancellationToken ct) => (await session.GetAsync(StorageId, Physical(index), ct))?.WithBusinessSlot(new(Space, index));
    public async ValueTask<IReadOnlyList<ItemReference>> GetAllAsync(CancellationToken ct) => (await session.GetAllAsync(StorageId, ct)).Select(x => x.WithBusinessSlot(new(Space, Space == BusinessInventorySpace.WornCreature ? 0 : x.StorageSlotIndex))).ToArray();
    public ValueTask<ItemRecord> TakeAsync(ItemReference reference, int? count, CancellationToken ct) => session.TakeAsync(reference, count, ct);
    public async ValueTask<ItemReference> PutAsync(int index, ItemRecord record, CancellationToken ct) => (await session.PutAsync(StorageId, Physical(index), record, ct)).WithBusinessSlot(new(Space, index));
    public async ValueTask<ItemReference> UpdateAsync(ItemReference reference, Func<ItemSnapshot, ItemSnapshot> change, CancellationToken ct) => (await session.UpdateAsync(reference, change, ct)).WithBusinessSlot(new(Space, reference.SlotIndex));
    public ValueTask FlushAsync(CancellationToken ct) => ValueTask.CompletedTask;
    public ValueTask<BusinessItemAdditionResult> AddAsync(int itemId, int count, CancellationToken ct) => throw new NotSupportedException();
}
