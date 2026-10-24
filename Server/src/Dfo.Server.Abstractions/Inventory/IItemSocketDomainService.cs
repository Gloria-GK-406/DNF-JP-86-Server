using Dfo.Server.Abstractions.Effects;
using System.Collections.Immutable;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
namespace Dfo.Server.Abstractions.Inventory;

public enum ItemSocketRejection { None, NoSession, InvalidRequest, MissingItem, Locked, UnsupportedContent, AlreadyOpen, MissingMaterial, WrongEmblem }
public sealed record EmblemPlacement(BusinessSlot Material, int ExpectedItemId, byte SocketIndex);
public sealed record SocketEmbedding(byte SocketIndex, int ItemId);
public sealed record ItemSocketInput(bool Avatar, bool Open, BusinessSlot Target, int ExpectedItemId,
    BusinessSlot? Drill, ImmutableArray<EmblemPlacement> Emblems);
public sealed record SocketMaterialRemainder(BusinessSlot Slot, int Remaining);
public sealed record ItemSocketReceipt(SelectedSessionIdentity Session, ItemSocketInput Input,
    ImmutableArray<SocketMaterialRemainder> Materials, DateTimeOffset At);
public sealed record ItemSocketResult(ItemSocketRejection Rejection, ItemSocketReceipt? Receipt = null);
public sealed record ItemSocketOpenPlan(ItemSocketRejection Rejection, ImmutableArray<ushort> Types, int MaterialCount);
public sealed record ItemSocketEmbedPlan(ItemSocketRejection Rejection, ImmutableArray<ushort> Types, ImmutableArray<SocketEmbedding> Embeddings);
public interface IItemSocketDomainService
{
    ValueTask<ItemSocketResult> ApplyAsync(SelectedSessionIdentity session, ItemSocketInput input, IEffectFootprintRecorder footprint, CancellationToken ct);
}
public interface IItemSocketRuleService
{
    ValueTask<ItemSocketOpenPlan> OpenAsync(ItemSnapshot target, bool avatar, int drillId, DateTimeOffset now, CancellationToken ct);
    ValueTask<ItemSocketEmbedPlan> EmbedAsync(ItemSnapshot target, bool avatar, ImmutableArray<EmblemPlacement> emblems, CancellationToken ct);
}
