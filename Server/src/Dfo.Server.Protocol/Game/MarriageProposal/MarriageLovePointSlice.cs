using System.Buffers.Binary;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
namespace Dfo.Server.Protocol.Game.MarriageProposal;
public sealed record WeddingLovePointRequest(GameMessageMetadata Metadata, uint Slot) : IEffectSettlementRequest
{
    public ClientMessageType MessageType => ClientMessageType.GameWeddingLovePoint;
    public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.Inventory | ReplayProjectionNeeds.MarriageLovePoints;
}
internal sealed class WeddingLovePointDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata m, ReadOnlySpan<byte> b)
    {
        if (b.Length != 18) throw new InvalidDataException("Love coupon requires 18 body bytes.");
        return new WeddingLovePointRequest(m, BinaryPrimitives.ReadUInt32LittleEndian(b[14..]));
    }
}
internal sealed record WeddingLovePointAck(int Status) : IGameCmdMessage
{ public ServerMessageType MessageType => ServerMessageType.GameWeddingLovePoint; }
internal sealed class WeddingLovePointAckEncoder : GameCmdMessageEncoder<WeddingLovePointAck>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(WeddingLovePointAck m)
    {
        var b = new byte[5]; b[0] = 1; BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(1), m.Status);
        return OnePayload(m, b);
    }
}
internal sealed record WeddingLovePointNotification(int Character, int Points) : IGameNotiMessage
{ public ServerMessageType MessageType => ServerMessageType.GameWeddingLovePointNotification; }
internal sealed class WeddingLovePointNotificationEncoder : GameNotiMessageEncoder<WeddingLovePointNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(WeddingLovePointNotification m)
    {
        var b = new byte[8]; BinaryPrimitives.WriteInt32LittleEndian(b, m.Character); BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(4), m.Points);
        return OnePayload(m, b);
    }
}

internal sealed class WeddingLovePointHandler(ICommandBus commands) : ClientMessageHandler<WeddingLovePointRequest>, Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameWeddingLovePointCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, WeddingLovePointRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameWeddingLovePointCommand(context, message), ct).ConfigureAwait(false);
}
