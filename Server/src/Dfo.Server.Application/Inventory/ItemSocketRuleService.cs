using System.Buffers.Binary;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
namespace Dfo.Server.Application.Inventory;

internal sealed class ItemSocketRuleService(IItemSocketDefinitionSource source) : IItemSocketRuleService
{
    public async ValueTask<ItemSocketOpenPlan> OpenAsync(ItemSnapshot target, bool avatar, int drillId, DateTimeOffset now, CancellationToken ct)
    {
        var definition = await source.TargetAsync(target.ItemId, ct).ConfigureAwait(false);
        if (definition is null || definition.Avatar != avatar) return new(ItemSocketRejection.UnsupportedContent, [], 0);
        if (avatar ? HasAvatarSockets(target) : target.EmblemSocketCount != 0)
            return new(ItemSocketRejection.AlreadyOpen, [], 0);
        var types = definition.DefaultTypes.IsDefaultOrEmpty ? definition.OpenTypes : definition.DefaultTypes;
        if (types.Length < 1 || types.Length > (avatar ? 3 : 2)) return new(ItemSocketRejection.UnsupportedContent, [], 0);
        if (avatar && !definition.DefaultTypes.IsDefaultOrEmpty) return new(ItemSocketRejection.None, types, 0);
        var drill = await source.DrillAsync(drillId, ct).ConfigureAwait(false);
        return drill?.Avatar == avatar && (drill.ExpiresAt == 0 || drill.ExpiresAt > now.ToUnixTimeSeconds()) ? new(ItemSocketRejection.None, types, 1) : new(ItemSocketRejection.MissingMaterial, [], 0);
    }
    public async ValueTask<ItemSocketEmbedPlan> EmbedAsync(ItemSnapshot target, bool avatar, ImmutableArray<EmblemPlacement> emblems, CancellationToken ct)
    {
        var definition = await source.TargetAsync(target.ItemId, ct).ConfigureAwait(false);
        if (definition is null || definition.Avatar != avatar) return new(ItemSocketRejection.UnsupportedContent, [], []);
        var types = avatar ? AvatarTypes(target, definition) : definition.OpenTypes;
        if (types.Length < 1 || types.Length > (avatar ? 3 : 2) || !avatar && target.EmblemSocketCount != types.Length)
            return new(ItemSocketRejection.UnsupportedContent, [], []);
        if (emblems.IsDefaultOrEmpty || emblems.Length > types.Length) return new(ItemSocketRejection.InvalidRequest, [], []);
        var result = ImmutableArray.CreateBuilder<SocketEmbedding>();
        foreach (var emblem in emblems)
        {
            // Single-slot auxiliary equipment uses the sole socket; older UI sends either 0 or 1.
            var index = !avatar && types.Length == 1 && emblem.SocketIndex <= 1 ? (byte)0 : emblem.SocketIndex;
            if (index >= types.Length || result.Any(x => x.SocketIndex == index)) return new(ItemSocketRejection.InvalidRequest, [], []);
            var allowed = await source.EmblemTypesAsync(emblem.ExpectedItemId, ct).ConfigureAwait(false);
            if (allowed == 0 || (allowed & types[index]) == 0) return new(ItemSocketRejection.WrongEmblem, [], []);
            result.Add(new(index, emblem.ExpectedItemId));
        }
        return new(ItemSocketRejection.None, types, result.ToImmutable());
    }
    private static bool HasAvatarSockets(ItemSnapshot target) => target.AvatarDetail?.JewelSocket.Any(x => x != 0) == true;
    private static ImmutableArray<ushort> AvatarTypes(ItemSnapshot target, ItemSocketDefinition definition)
    {
        var bytes = target.AvatarDetail?.JewelSocket;
        if (bytes is null || bytes.Length != 30) return [];
        if (!HasAvatarSockets(target)) return definition.DefaultTypes;
        var result = ImmutableArray.CreateBuilder<ushort>();
        for (var i = 0; i < 5; i++)
        {
            var type = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(i * 6));
            if (type == 0)
            {
                if (bytes.AsSpan(i * 6).ContainsAnyExcept((byte)0)) return [];
                break;
            }
            result.Add(type);
        }
        var types = result.ToImmutable();
        return types.SequenceEqual(definition.OpenTypes) || types.SequenceEqual(definition.DefaultTypes) ? types : [];
    }
}
