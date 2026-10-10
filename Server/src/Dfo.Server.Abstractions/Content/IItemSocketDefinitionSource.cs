using System.Collections.Immutable;
namespace Dfo.Server.Abstractions.Content;

public sealed record ItemSocketDefinition(bool Avatar, ImmutableArray<ushort> OpenTypes, ImmutableArray<ushort> DefaultTypes);
public sealed record SocketDrillDefinition(bool Avatar, long ExpiresAt = 0);
public interface IItemSocketDefinitionSource
{
    ValueTask<ItemSocketDefinition?> TargetAsync(int itemId, CancellationToken ct);
    ValueTask<SocketDrillDefinition?> DrillAsync(int itemId, CancellationToken ct);
    ValueTask<ushort> EmblemTypesAsync(int itemId, CancellationToken ct);
}
