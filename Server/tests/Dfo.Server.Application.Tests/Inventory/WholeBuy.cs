using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Runtime;
using Dfo.Server.Protocol.Game.BuyItem;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

internal static class WholeBuy
{
    private sealed class State { internal readonly GameInboundFrameCounter Counter = new(); internal ushort Next; }
    private static readonly ConditionalWeakTable<NetworkConnectionContext, State> States = new();
    internal static BuyItemRequest Input(ShopFixture fixture, int item, int count, int shop = 15, int npc = 2, ushort? sequence = null)
    {
        var state = States.GetValue(fixture.Connection, _ => new());
        var seq = sequence ?? ++state.Next;
        var (ordinal, fresh) = state.Counter.Observe(seq);
        var body = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(body, item);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4), count);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(8), shop);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(12), npc);
        var metadata = (new GameMessageMetadata(1, 0x0015, 0, seq) { InboundOrdinal = ordinal, SequenceIsFresh = fresh }).WithRawBody(body);
        var row = fixture.Services.GetServices<GameMessageDecoderRegistration>().Single(row => row.Selector == 1 && row.Type == 0x0015);
        return (BuyItemRequest)((IGameMessageDecoder)fixture.Services.GetRequiredService(row.DecoderType)).Decode(metadata, body)!;
    }
    internal static ValueTask<BuyItemOutcome> Command(ShopFixture fixture, int item, int count, int shop = 15, int npc = 2, ushort? sequence = null) =>
        fixture.Services.GetRequiredService<ICommandBus>().SendAsync(new GameBuyItemCommand(new(fixture.Connection), Input(fixture, item, count, shop, npc, sequence)));
    internal static ValueTask Send(ShopFixture fixture, int item, int count, int shop = 15, int npc = 2, ushort? sequence = null) =>
        fixture.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(fixture.Connection), Input(fixture, item, count, shop, npc, sequence), default);
}
