using System.Buffers.Binary;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
namespace Dfo.Server.Application.Inventory;

internal sealed class AvatarDomainService(IBusinessInventorySlotDomainService slots, IDomainTransactionAccessor transactions) : IAvatarDomainService
{
    public async ValueTask<IItemSocketInstance?> FindAtAsync(CharacterId owner, BusinessSlot slot, CancellationToken ct)
    {
        _ = transactions.Current ?? throw new InvalidOperationException("Avatar instances require a transaction.");
        if (slot.Space is not (BusinessInventorySpace.Avatar or BusinessInventorySpace.WornAvatar)) return null;
        var bound = slots.Find(owner, slot);
        var item = bound is null ? null : await bound.Container.GetAsync(slot.Index, ct).ConfigureAwait(false);
        return item is { IsPile: false, Snapshot.AvatarDetail: not null } && item.Type.HasFlag(ItemTraits.Avatar)
            ? new AvatarInstance(bound!.Container, item) : null;
    }
}
internal sealed class AvatarInstance(IBusinessItemContainer container, ItemReference reference) : IItemSocketInstance
{
    public async ValueTask<ItemReference> ReadAsync(CancellationToken ct)
    {
        var current = await container.GetAsync(reference.SlotIndex, ct).ConfigureAwait(false);
        if (current is not { IsPile: false, Snapshot.AvatarDetail: not null }
            || !reference.SameOccupancyAs(current) || !current.Type.HasFlag(ItemTraits.Avatar))
            throw new InvalidOperationException("Avatar no longer occupies its original slot.");
        return current;
    }
    public ValueTask<ItemReference> OpenSocketsAsync(ImmutableArray<ushort> types, CancellationToken ct) =>
        ChangeAsync(core =>
        {
            var stored = core.AvatarDetail!.JewelSocket;
            if (stored.Any(x => x != 0)) throw new InvalidOperationException("Avatar sockets cannot be reopened.");
            return Set(core, types, []);
        }, ct);
    public ValueTask<ItemReference> EmbedAsync(ImmutableArray<ushort> types, ImmutableArray<SocketEmbedding> emblems, CancellationToken ct) =>
        ChangeAsync(core => Set(core, types, emblems), ct);
    private static ItemSnapshot Set(ItemSnapshot core, ImmutableArray<ushort> types, ImmutableArray<SocketEmbedding> emblems)
    {
        // A21 general avatar projection has three visible sockets. Never mutate unprojectable slots.
        if (types.Length is < 1 or > 3 || types.Any(x => x == 0)
            || emblems.Any(x => x.SocketIndex >= types.Length || x.ItemId <= 0)
            || emblems.Select(x => x.SocketIndex).Distinct().Count() != emblems.Length)
            throw new InvalidOperationException("Invalid avatar socket mutation.");
        var detail = core.AvatarDetail!;
        var bytes = new byte[30]; detail.JewelSocket.AsSpan(0, Math.Min(detail.JewelSocket.Length, 30)).CopyTo(bytes);
        for (var i = 0; i < types.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 6), types[i]);
        foreach (var emblem in emblems) BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(emblem.SocketIndex * 6 + 2), emblem.ItemId);
        return core with { AvatarDetail = detail with { JewelSocket = bytes } };
    }
    private async ValueTask<ItemReference> ChangeAsync(Func<ItemSnapshot, ItemSnapshot> change, CancellationToken ct)
    {
        _ = await ReadAsync(ct).ConfigureAwait(false);
        return await container.UpdateAsync(reference, change, ct).ConfigureAwait(false);
    }
}
