using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Effects;
using System.Buffers.Binary;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging;

using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Dispatching;
namespace Dfo.Server.Protocol.Game.LotteryOpening;

public sealed record LotteryOpeningRequest(GameMessageMetadata Metadata, ushort Mode, short Slot) : IEffectSettlementRequest
{
    public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.Inventory | (Mode == 1 ? ReplayProjectionNeeds.Premium : ReplayProjectionNeeds.None);
    public ClientMessageType MessageType => ClientMessageType.GameLotteryOpening;
}
internal sealed class LotteryOpeningDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length != 4 || BinaryPrimitives.ReadUInt16LittleEndian(body) > 1 || BinaryPrimitives.ReadInt16LittleEndian(body[2..]) < 3)
            throw new InvalidDataException("Invalid lottery opening request.");
        return new LotteryOpeningRequest(metadata, BinaryPrimitives.ReadUInt16LittleEndian(body), BinaryPrimitives.ReadInt16LittleEndian(body[2..]));
    }
}
public sealed record LotteryOpeningResponse(LotteryOpeningResult? Result, byte Error = 0) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameLotteryOpening;
}
internal sealed class LotteryOpeningEncoder : GameCmdMessageEncoder<LotteryOpeningResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(LotteryOpeningResponse message) => OnePayload(message, Encode(message));
    internal static byte[] Encode(LotteryOpeningResponse message)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        var result = message.Result;
        writer.Write(result is null ? (byte)0 : (byte)1);
        if (result is null) { writer.Write(message.Error); return stream.ToArray(); }
        var receipt = result.Receipt;
        var reward = receipt.Rewards[0];
        var core = reward.Snapshot ?? receipt.Updates.Single(x => x.Slot == reward.Slot).Core;
        writer.Write(checked((short)OpeningWireSlots.RewardIndex(result.SourceSlot))); writer.Write(checked((short)OpeningWireSlots.RewardIndex(reward.Slot)));
        writer.Write(reward.ItemId); writer.Write(receipt.Rewards.Sum(x => x.Count)); writer.Write(core.Durability);
        writer.Write(core.Attribute); writer.Write(core.AmplifyType); writer.Write(core.AmplifyValue);
        // A21 0x0113F780 reads these fields unconditionally, including two length-prefixed values.
        // Empty optional properties; absolute inventory projection follows this native result.
        writer.Write(0); // unknown item scalar
        writer.Write((byte)0); // extra u32 array count (0x01637A80)
        writer.Write((ushort)0); // nested reader 0x012612F0
        writer.Write((byte)0); // nested list count (0x01088BC0)
        writer.Write(new byte[10]); // u8 x3, u16, u8 x5
        writer.Write(4); writer.Write(0); // destination constructor initializes a 4-byte scalar
        writer.Write(8); writer.Write(0L); // second destination initializes an 8-byte scalar
        writer.Write((byte)0); writer.Write(0);
        return stream.ToArray();
    }
}
public sealed class LotteryOpeningHandler(ICommandBus commands) : ClientMessageHandler<LotteryOpeningRequest>, IProtocolCommandMessageHandler<GameLotteryOpeningCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, LotteryOpeningRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameLotteryOpeningCommand(context, message), ct).ConfigureAwait(false);
}
