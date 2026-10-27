using System.Buffers.Binary;
using System.Text;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.MarriageProposal;

internal sealed record WeddingAvatarNotification(MarriageChurchSnapshot Church, bool Restore = false) : IGameNotiMessage
{ public ServerMessageType MessageType => ServerMessageType.GameWeddingAvatar; }
internal sealed class WeddingAvatarEncoder : GameNotiMessageEncoder<WeddingAvatarNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(WeddingAvatarNotification m)
    {
        var b = new byte[67]; b[0] = m.Restore ? (byte)1 : (byte)0;
        MarriageChurchPayload.CopyName(m.Church.ApplicantName, b.AsSpan(2, 30));
        MarriageChurchPayload.CopyName(m.Church.PartnerName, b.AsSpan(32, 30));
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(62), m.Church.Room.AreaId);
        return OnePayload(0x03CF, b);
    }
}
internal sealed record WeddingBeginNotification(MarriageChurchSnapshot Church) : IGameNotiMessage
{ public ServerMessageType MessageType => ServerMessageType.GameWeddingBegin; }
internal sealed record WeddingLetterNotification(MarriageChurchSnapshot Church, uint Key, bool Paid = false) : IGameNotiMessage
{ public ServerMessageType MessageType => ServerMessageType.GameWeddingLetter; }
internal sealed class WeddingBeginEncoder : GameNotiMessageEncoder<WeddingBeginNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(WeddingBeginNotification m)
    {
        var b = new byte[68];
        BinaryPrimitives.WriteUInt32LittleEndian(b, checked((uint)m.Church.Peer.Value));
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4), m.Church.Room.AreaId);
        MarriageChurchPayload.CopyName(m.Church.ApplicantName, b.AsSpan(8, 30));
        MarriageChurchPayload.CopyName(m.Church.PartnerName, b.AsSpan(38, 30));
        return OnePayload(0x03D7, b);
    }
}
internal sealed class WeddingLetterEncoder : GameNotiMessageEncoder<WeddingLetterNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(WeddingLetterNotification m)
    {
        var b = new byte[78];
        MarriageChurchPayload.Preparation(m.Church).AsSpan(0, 77).CopyTo(b);
        BinaryPrimitives.WriteUInt32LittleEndian(b, m.Key); b[77] = m.Paid ? (byte)1 : (byte)0;
        return OnePayload(0x03D5, b);
    }
}
internal sealed record WeddingInvitedGuestsNotification(IReadOnlyList<uint> Guests) : IGameNotiMessage
{ public ServerMessageType MessageType => ServerMessageType.GameWeddingInvitedGuests; }
internal sealed class WeddingInvitedGuestsEncoder : GameNotiMessageEncoder<WeddingInvitedGuestsNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(WeddingInvitedGuestsNotification m)
    {
        var b = new byte[1 + 4 * m.Guests.Count]; b[0] = checked((byte)m.Guests.Count);
        for (var i = 0; i < m.Guests.Count; i++) BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(1 + 4 * i), m.Guests[i]);
        return OnePayload(0x03D6, b);
    }
}
public sealed record WeddingLetterRequest(GameMessageMetadata Metadata, byte Mode, uint Target, ushort OnlineId, byte Kind) : IClientNetworkMessage
{ public ClientMessageType MessageType => ClientMessageType.GameWeddingSendLetter; }
public sealed record WeddingGiftRequest(GameMessageMetadata Metadata, uint Key, int Gold, string Text) : IClientNetworkMessage
{ public ClientMessageType MessageType => ClientMessageType.GameWeddingMoneyGift; }
internal sealed class WeddingLetterDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata m, ReadOnlySpan<byte> b)
    {
        if (b.Length != 22) throw new InvalidDataException("Wedding letter requires 22 body bytes.");
        return new WeddingLetterRequest(m, b[14], BinaryPrimitives.ReadUInt32LittleEndian(b[15..]), BinaryPrimitives.ReadUInt16LittleEndian(b[19..]), b[21]);
    }
}
internal sealed class WeddingGiftDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata m, ReadOnlySpan<byte> b)
    {
        if (b.Length < 12) throw new InvalidDataException("Wedding gift is truncated.");
        var length = BinaryPrimitives.ReadInt32LittleEndian(b[8..]);
        if (length < 0 || length > 1024 || b.Length != 12 + length) throw new InvalidDataException("Invalid blessing length.");
        return new WeddingGiftRequest(m, BinaryPrimitives.ReadUInt32LittleEndian(b), BinaryPrimitives.ReadInt32LittleEndian(b[4..]), new UTF8Encoding(false, true).GetString(b[12..]));
    }
}
internal sealed record WeddingLetterAck(int Status, ushort Remaining) : IGameCmdMessage
{ public ServerMessageType MessageType => ServerMessageType.GameWeddingSendLetter; }
internal sealed record WeddingGiftAck(int Status, uint Key) : IGameCmdMessage
{ public ServerMessageType MessageType => ServerMessageType.GameWeddingMoneyGift; }
internal sealed class WeddingLetterAckEncoder : GameCmdMessageEncoder<WeddingLetterAck>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(WeddingLetterAck m)
    {
        var b = new byte[m.Status == 0 ? 7 : 5]; b[0] = 1; BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(1), m.Status);
        if (m.Status == 0) BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(5), m.Remaining);
        return OnePayload(0x03F6, b);
    }
}
internal sealed class WeddingGiftAckEncoder : GameCmdMessageEncoder<WeddingGiftAck>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(WeddingGiftAck m)
    {
        var b = new byte[m.Status == 0 ? 9 : 5]; b[0] = 1; BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(1), m.Status);
        if (m.Status == 0) BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(5), m.Key);
        return OnePayload(0x03F2, b);
    }
}
internal sealed class WeddingLetterHandler(ICommandBus commands) : ClientMessageHandler<WeddingLetterRequest>, Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameWeddingLetterCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, WeddingLetterRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameWeddingLetterCommand(context, message), ct).ConfigureAwait(false);
}
internal sealed class WeddingGiftHandler(ICommandBus commands) : ClientMessageHandler<WeddingGiftRequest>, Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameWeddingGiftCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, WeddingGiftRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameWeddingGiftCommand(context, message), ct).ConfigureAwait(false);
}

internal sealed class MarriageWeddingScheduler(ICommandBus commands, TimeProvider clock, ILogger<MarriageWeddingScheduler> logger)
    : TypedGameplayTimerSource(commands, logger)
{
    protected override string SourceName => "timer:MarriageWeddingScheduler";
    protected override async Task RunRoundAsync(GameplayCallChain root, CancellationToken token) =>
        _ = await SubmitAsync(new AdvanceWeddingTickCommand(), root, SourceName, token).ConfigureAwait(false);
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), clock);
        while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false)) await HostedAsync(token).ConfigureAwait(false);
    }
}
