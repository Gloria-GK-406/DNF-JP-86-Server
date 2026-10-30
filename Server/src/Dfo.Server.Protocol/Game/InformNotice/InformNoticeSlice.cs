using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Notices;

namespace Dfo.Server.Protocol.Game.InformNotice;

public interface INoticeSeenReport : IClientNetworkMessage
{
    NoticeGroup Group { get; }
    uint Value { get; }
}

public sealed record NoticeSeenRequest(GameMessageMetadata Metadata, uint Value) : INoticeSeenReport
{
    public NoticeGroup Group => NoticeGroup.Primary;
    public ClientMessageType MessageType => ClientMessageType.GameNoticeSeen;
}

public sealed record InformNoticeSecondReport(GameMessageMetadata Metadata, uint Value) : INoticeSeenReport
{
    public NoticeGroup Group => NoticeGroup.Secondary;
    public ClientMessageType MessageType => ClientMessageType.GameInformNoticeSecond;
}

internal static class NoticeStateCodec
{
    public static uint DecodeId(ReadOnlySpan<byte> body)
    {
        if (body.Length != 4 || BinaryPrimitives.ReadUInt32LittleEndian(body) > 127)
            throw new InvalidDataException("Seen notice requires one u32 ID in 0..127.");
        return BinaryPrimitives.ReadUInt32LittleEndian(body);
    }

    public static byte[] Encode(IReadOnlyList<byte> ids)
    {
        if (ids.Any(id => id > 127)) throw new InvalidDataException("Invalid seen notice ID.");
        var distinct = ids.Distinct().Order().ToArray();
        var body = new byte[distinct.Length + 1];
        body[0] = checked((byte)distinct.Length);
        distinct.CopyTo(body, 1);
        return body;
    }
}

internal sealed class NoticeSeenDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new NoticeSeenRequest(metadata, NoticeStateCodec.DecodeId(body));
}

internal sealed class InformNoticeSecondDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new InformNoticeSecondReport(metadata, NoticeStateCodec.DecodeId(body));
}

public sealed class NoticeSeenHandler(ICommandBus commands) : ClientMessageHandler<INoticeSeenReport>,
    Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameNoticeSeenCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, INoticeSeenReport message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameNoticeSeenCommand(context, message), ct).ConfigureAwait(false);
}

/// <summary>Full replacement of the primary seen-notice set, delivered to the login connection.</summary>
public sealed record InformNoticeNotice(IReadOnlyList<byte> NoticeIds) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameInformNotice;
}

internal sealed class InformNoticeNoticeEncoder : GameNotiMessageEncoder<InformNoticeNotice>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(InformNoticeNotice message) =>
        OnePayload(message, NoticeStateCodec.Encode(message.NoticeIds));
}
