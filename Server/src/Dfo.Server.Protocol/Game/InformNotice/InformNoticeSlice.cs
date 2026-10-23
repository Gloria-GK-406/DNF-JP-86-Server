using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notices;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Notices;
using Dfo.Server.Data.OnlineUsers;
using Microsoft.Extensions.Logging;

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

public sealed record MarkNoticeSeenCommand(GameSessionIdentity Session, AccountId AccountId,
    NoticeOwner Owner, NoticeGroup Group, byte NoticeId) : ICommand<bool>, IAccountCommand;

public sealed class MarkNoticeSeenCommandHandler(INoticeStateDomainService notices)
    : ICommandHandler<MarkNoticeSeenCommand, bool>
{
    public ValueTask<bool> HandleAsync(CommandExecutionContext context, MarkNoticeSeenCommand command, CancellationToken ct) =>
        notices.MarkSeenAsync(command.Session, command.Owner, command.Group, command.NoticeId, ct);
}

public sealed class NoticeSeenHandler(IOnlineUserQueryDomainService users, ICommandBus commands,
    ILogger<NoticeSeenHandler> logger) : ClientMessageHandler<INoticeSeenReport>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        INoticeSeenReport message, CancellationToken ct)
    {
        if (!users.TryGetByConnection(context.Connection.ConnectionId, out var user)
            || user?.AccountId is not { } accountId) return;
        if (message.Group == NoticeGroup.Secondary && user.CurrentCharacterId is null) return;
        // Server policy, not a claim about the original game's persistence scope.
        var owner = message.Group == NoticeGroup.Primary
            ? NoticeOwner.ForAccount(accountId)
            : NoticeOwner.ForCharacter(user.CurrentCharacterId!.Value);
        var saved = await commands.SendAsync(new MarkNoticeSeenCommand(user.SessionIdentity, accountId,
            owner, message.Group, checked((byte)message.Value)), ct);
        logger.LogDebug("Seen notice owner={Owner} group={Group} id={NoticeId} saved={Saved}",
            owner, message.Group, message.Value, saved);
        // Client already inserted locally. No CMD ACK and no redundant snapshot.
    }
}

/// <summary>Full replacement of the primary seen-notice set, delivered to the login connection.</summary>
public sealed record InformNoticeNotice(IReadOnlyList<byte> NoticeIds) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameInformNotice;
}

internal sealed class InformNoticeNoticeEncoder : GameNotiMessageEncoder<InformNoticeNotice>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(InformNoticeNotice message) =>
        OnePayload(GameProtocol.InformNoticeNotificationType, NoticeStateCodec.Encode(message.NoticeIds));
}
