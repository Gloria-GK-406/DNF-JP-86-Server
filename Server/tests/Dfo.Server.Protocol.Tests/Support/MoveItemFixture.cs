using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Tests.Support;

namespace Dfo.Server.Protocol.Tests.Support;

/// <summary>One backend for every container reached by a move command.</summary>
internal sealed class MoveItemFixture
{
    private readonly VolatileItemDomainStore store = new();
    private readonly DomainTransactionScope scope = new();
    public IItemDomainStoreSession Session => store.SessionIn(scope.Current
        ?? throw new InvalidOperationException("The move requires an open transaction."));

    public async ValueTask<T> RunAsync<T>(Func<ValueTask<T>> action)
    {
        await using var transaction = scope.Begin();
        var result = await action();
        await transaction.CommitAsync(CancellationToken.None);
        return result;
    }
}

/// <summary>Exercises real custody while allowing deliberately misplaced initial test items.</summary>
internal class LifecycleTestContainer(
    InventoryContainerType type, CharacterId character, MoveItemFixture fixture) : IItemContainer
{
    public ContainerId Id { get; } = ContainerId.ForCharacter(character, type);
    public InventoryContainerType Type => type;
    public ContainerShape Shape { get; } = new(type, 0);
    public bool IsRemovable => false;

    protected void SeedItem(int slot, int itemId, byte kind) => fixture.RunAsync(async () =>
    {
        var snapshot = ItemSnapshot.FromCore(StackableItemCore.Create(kind, itemId, 1), false,
            avatar: kind == 8 ? new InventoryAvatarDetailState(0, 0, new byte[30], 0, 0, 0) : null);
        var item = await fixture.Session.CreateAsync(snapshot, default);
        return await fixture.Session.PutAsync(Id, slot, item, default);
    }).AsTask().GetAwaiter().GetResult();

    public ValueTask<ItemReference?> GetAsync(int slot, CancellationToken ct) => fixture.Session.GetAsync(Id, slot, ct);
    public ValueTask<IReadOnlyList<ItemReference>> GetAllAsync(CancellationToken ct) => fixture.Session.GetAllAsync(Id, ct);
    public ValueTask<ItemRecord> TakeAsync(ItemReference item, int? count, CancellationToken ct) => fixture.Session.TakeAsync(item, count, ct);
    public ValueTask<ItemReference> PutAsync(int slot, ItemRecord item, CancellationToken ct) => fixture.Session.PutAsync(Id, slot, item, ct);
    public ValueTask<ItemReference> UpdateAsync(ItemReference item, Func<ItemSnapshot, ItemSnapshot> change, CancellationToken ct) => fixture.Session.UpdateAsync(item, change, ct);
    public ValueTask FlushAsync(CancellationToken ct) => fixture.Session.ApplyDecidedWritesAsync(ct);
    public ValueTask<ItemSnapshot?> ReadSnapshotAsync(int slot, CancellationToken ct) => fixture.RunAsync(async () =>
        (await GetAsync(slot, ct))?.Snapshot);
    public ValueTask<IReadOnlyList<InventoryItemState>> SnapshotAsync(CancellationToken ct) => fixture.RunAsync<IReadOnlyList<InventoryItemState>>(async () =>
        [.. (await GetAllAsync(ct)).Select(item => new InventoryItemState(
            item.ItemUid ?? new ItemIdentity(0), item.SlotIndex, item.Snapshot.ToCore()))]);
}
