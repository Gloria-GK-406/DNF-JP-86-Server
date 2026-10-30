using Dfo.Server.Application.Commands;
using Dfo.Server.Data.World;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Networking;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace Dfo.Server.Protocol.Game.Fatigue;

internal sealed record FatigueNotification(CharacterFatigueBalance Balance) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameFatigue;
}
internal sealed class FatigueEncoder : GameNotiMessageEncoder<FatigueNotification>
{
    internal static byte[] Body(CharacterFatigueBalance balance)
    {
        var body = new byte[10];
        BinaryPrimitives.WriteUInt16LittleEndian(body, balance.Used);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(2), balance.Total);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(4), checked((ushort)Math.Min(ushort.MaxValue, balance.ConsumedToday)));
        // The two A21-specific trailing fields remain zero until their semantics are verified.
        return body;
    }
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(FatigueNotification message) =>
        OnePayload(message, Body(message.Balance));
}
