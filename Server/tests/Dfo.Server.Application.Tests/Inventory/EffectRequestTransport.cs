using System.Runtime.CompilerServices;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Runtime;
using Dfo.Server.Protocol.Game.ReinforceItem;
using Dfo.Server.Protocol.Game.DisjointItem;
using Dfo.Server.Protocol.Game.EnchantByBead;
using Dfo.Server.Protocol.Game.Outworld;
using Dfo.Server.Protocol.Game.Forging;
using Dfo.Server.Protocol.Game.MagicSeal;
using Dfo.Server.Protocol.Game.Teleport;
using Dfo.Server.Protocol.Game.CompoundItem;
using Dfo.Server.Protocol.Game.LotteryOpening;
using Dfo.Server.Protocol.Game.VendingMachine;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

/// <summary>Serialize real wire bodies, use the production all-frame counter, then the registered decoder and dispatcher.</summary>
internal static class EffectRequestTransport
{
    private static readonly ConditionalWeakTable<NetworkConnectionContext, CounterState> Counters = new();
    public static ValueTask Send(ShopFixture fixture, IEffectSettlementRequest request)
    {
        var body = Body(request);
        var (ordinal, fresh) = Counters.GetValue(fixture.Connection, _ => new()).Observe(request.Metadata.Sequence);
        var metadata = (request.Metadata with { InboundOrdinal = ordinal, SequenceIsFresh = fresh }).WithRawBody(body);
        var decoderRegistration = fixture.Services.GetServices<GameMessageDecoderRegistration>()
            .Single(x => x.Selector == metadata.Selector && x.Type == metadata.Type);
        var decoded = ((IGameMessageDecoder)fixture.Services.GetRequiredService(decoderRegistration.DecoderType)).Decode(metadata, body)
            ?? throw new InvalidDataException("Fixture body was not decoded.");
        return fixture.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(fixture.Connection), decoded, default);
    }
    public static void ObserveOtherFrames(ShopFixture fixture, int count) =>
        Counters.GetValue(fixture.Connection, _ => new()).Other(count);
    public static byte[] MainSnapshot(ShopFixture fixture) => fixture.Sender.Batches.Last().Frames
        .Single(frame => frame[0] == 0 && BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)) == 0x000D && frame[15] == 0);
    public static int MainValue(ShopFixture fixture, short slot)
    {
        var frame = MainSnapshot(fixture); var count = BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(18));
        return Enumerable.Range(0, count).Where(n => BinaryPrimitives.ReadInt16LittleEndian(frame.AsSpan(20 + n * 101)) == slot)
            .Select(n => BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(26 + n * 101))).Single();
    }
    private static byte[] Body(IEffectSettlementRequest request)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        switch (request)
        {
            case Dfo.Server.Protocol.Game.AvatarCompound.AvatarCompoundRequest x:
                writer.Write(x.MaterialSlot); writer.Write(x.FirstSlot); writer.Write(x.FirstItemId);
                writer.Write(x.SecondSlot); writer.Write(x.SecondItemId); writer.Write(x.TargetItemId);
                writer.Write(x.Ability); writer.Write(x.RandomTarget); writer.Write(x.Category); break;
            case Dfo.Server.Protocol.Game.ResetRandomOption.ResetRandomOptionRequest x:
                writer.Write(x.TargetSlot); writer.Write(x.MaterialSlot); writer.Write(x.OptionIndex); break;
            case Dfo.Server.Protocol.Game.ResetItemQuality.ResetItemQualityRequest x:
                writer.Write(x.TargetSlot); writer.Write(x.ExpectedItemId); writer.Write(x.MaterialSlot); break;
            case Dfo.Server.Protocol.Game.EpicCraft.EpicCraftRequest x: writer.Write(x.EquipmentId); break;
            case Dfo.Server.Protocol.Game.Warehouse.WarehouseRequest x:
                if (x.Metadata.Type is 0x0133 or 0x0134) writer.Write(x.Value);
                else if (x.Metadata.Type is 0x0198 or 0x01AF) writer.Write(checked((ushort)x.Value));
                break;
            case ReinforceItemRequest x:
                writer.Write(checked((byte)x.Method)); writer.Write((byte)x.TargetList); writer.Write(x.TargetSlot); writer.Write(x.ExpectedItemId);
                writer.Write(x.MaterialSlot); writer.Write(x.OptionalTicketSlot); writer.Write(x.ReportedItemNameBytes.Length); writer.Write(x.ReportedItemNameBytes); break;
            case DisjointItemRequest x:
                writer.Write(checked((short)x.TargetSlot.Index)); writer.Write((byte)x.TargetSlot.Space);
                writer.Write(x.DisjointToolSlotIndex); writer.Write(x.DisjointProviderId); break;
            case EnchantByBeadRequest x:
                writer.Write((byte)x.BeadList); writer.Write(x.BeadSlot); writer.Write((byte)x.TargetList); writer.Write(x.TargetSlot); break;
            case ForgingRequest x:
                writer.Write(x.TargetListType); writer.Write(x.TargetSlot); writer.Write(x.TargetItemId); writer.Write(x.MaterialSlot);
                writer.Write(x.ReportedNameBytes.Length); writer.Write(x.ReportedNameBytes); writer.Write(x.Variant); break;
            case OutworldRequest x:
                if (!x.Purify) writer.Write(x.WireAction);
                writer.Write(x.TargetSlot); writer.Write(x.TargetItemId); writer.Write(x.MaterialSlot); writer.Write(x.MaterialItemId);
                if (!x.Purify) writer.Write(x.SelectedOption); break;
            case TeleportRequest x:
                writer.Write(x.SourceSlot); writer.Write(x.ItemId); writer.Write(x.Reserved); writer.Write(x.TownId);
                writer.Write(x.AreaId); writer.Write(x.X); writer.Write(x.Y); break;
            case MagicSealRequest x:
                writer.Write(x.TargetSlot); writer.Write(x.MaterialSlot); if (x.Change) writer.Write(x.OptionIndex); break;
            case CompoundItemRequest x:
                writer.Write(x.Source); writer.Write(x.SourceMode); writer.Write(x.Count);
                if (!x.Valid) { writer.Write((byte)1); writer.Write((byte)0); } break;
            case LotteryOpeningRequest x: writer.Write(x.Mode); writer.Write(x.Slot); break;
            case VendingMachineRequest x: writer.Write(x.MachineId); writer.Write(x.GroupId); writer.Write(x.Slot); break;
            default: throw new NotSupportedException($"Fixture has no raw encoder for {request.GetType().Name}.");
        }
        return stream.ToArray();
    }
    private sealed class CounterState
    {
        private readonly GameInboundFrameCounter counter = new();
        private ushort? lastFresh;
        public (long Ordinal, bool Fresh) Observe(ushort sequence)
        {
            var result = counter.Observe(sequence); if (result.Fresh) lastFresh = sequence; return result;
        }
        public void Other(int count)
        { for (var n = 0; n < count; n++) Observe(unchecked((ushort)((lastFresh ?? 0) + 1))); }
    }
}
