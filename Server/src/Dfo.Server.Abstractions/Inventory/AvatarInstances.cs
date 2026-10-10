using System.Collections.Immutable;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
namespace Dfo.Server.Abstractions.Inventory;

public interface IItemSocketInstance
{
    ValueTask<ItemReference> ReadAsync(CancellationToken ct);
    ValueTask<ItemReference> OpenSocketsAsync(ImmutableArray<ushort> types, CancellationToken ct);
    ValueTask<ItemReference> EmbedAsync(ImmutableArray<ushort> types, ImmutableArray<SocketEmbedding> emblems, CancellationToken ct);
}
public interface IAvatarDomainService
{
    ValueTask<IItemSocketInstance?> FindAtAsync(CharacterId owner, BusinessSlot slot, CancellationToken ct);
}
