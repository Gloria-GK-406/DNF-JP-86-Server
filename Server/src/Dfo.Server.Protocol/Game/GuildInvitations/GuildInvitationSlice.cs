using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.GuildCreation;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.GuildInvitations;

public sealed record GuildInvitationRequest(ClientMessageType MessageType, GameMessageMetadata Metadata,
    string? TargetName = null, bool? Accept = null) : IClientNetworkMessage;

internal sealed class GuildInviteDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new GuildInvitationRequest(ClientMessageType.GameGuildInvite, metadata,
            GuildCreationPayloadCodec.DecodeText(body, 1, 62));
}

internal sealed class GuildInviteReplyDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new GuildInvitationRequest(ClientMessageType.GameGuildInviteReply, metadata, Accept: body switch
        {
            [0] => false,
            [1] => true,
            _ => null,
        });
}

internal sealed record GuildInvited(string GuildName, byte[] InviterName, string Promotion, ushort MemberCount,
    uint WeeklyVisitors, IReadOnlyList<GuildContentKind> ActiveContents)
    : IGameNotiMessage, INotification<GuildInvited>
{
    public ServerMessageType MessageType => ServerMessageType.GameGuildInvited;
}

internal sealed record GuildInviteResolved(bool Accepted, byte[] InviteeName)
    : IGameNotiMessage, INotification<GuildInviteResolved>
{
    public ServerMessageType MessageType => ServerMessageType.GameGuildInviteResolved;
}

internal sealed class GuildInvitedEncoder : GameNotiMessageEncoder<GuildInvited>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GuildInvited message)
    {
        using var body = new MemoryStream();
        using var writer = new BinaryWriter(body);
        ChatDString.Write(writer, GuildCreationPayloadCodec.EncodeGuildText(message.GuildName));
        ChatDString.Write(writer, message.InviterName);
        ChatDString.Write(writer, GuildCreationPayloadCodec.EncodeGuildText(message.Promotion));
        writer.Write(message.WeeklyVisitors);
        writer.Write(message.MemberCount);
        writer.Write(checked((byte)message.ActiveContents.Count));
        foreach (var content in message.ActiveContents)
            writer.Write((byte)content);
        return OnePayload(message, body.ToArray());
    }
}

internal sealed class GuildInviteResolvedEncoder : GameNotiMessageEncoder<GuildInviteResolved>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GuildInviteResolved message)
    {
        using var body = new MemoryStream();
        using var writer = new BinaryWriter(body);
        writer.Write(message.Accepted);
        ChatDString.Write(writer, message.InviteeName);
        return OnePayload(message, body.ToArray());
    }
}

internal sealed class GuildInvitationProjection :
    INotificationHandler<GuildInvited>, INotificationHandler<GuildInviteResolved>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient, GuildInvited message, CancellationToken ct) =>
        ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([message]);

    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient, GuildInviteResolved message, CancellationToken ct) =>
        ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([message]);
}

public sealed class GuildInvitationHandler(ICommandBus commands)
    : ClientMessageHandler<GuildInvitationRequest>, IProtocolCommandMessageHandler<GameGuildInvitationCommand>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context, GuildInvitationRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameGuildInvitationCommand(context, message), ct).ConfigureAwait(false);
}

internal static class GuildInvitationRegistration
{
    internal static void AddGuildInvitationSlice(IServiceCollection services)
    {
        const ClientMessageType invite = ClientMessageType.GameGuildInvite;
        const ClientMessageType reply = ClientMessageType.GameGuildInviteReply;
        services.AddGameCmdMessageDecoder<GuildInviteDecoder>(invite);
        services.AddGameCmdMessageDecoder<GuildInviteReplyDecoder>(reply);
        services.AddGameNotiMessageEncoder<GuildInvited, GuildInvitedEncoder>();
        services.AddGameNotiMessageEncoder<GuildInviteResolved, GuildInviteResolvedEncoder>();
        services.AddNotificationHandler<GuildInvited, GuildInvitationProjection>();
        services.AddNotificationHandler<GuildInviteResolved, GuildInvitationProjection>();
        services.AddProtocolCommandHandler<GameGuildInvitationCommand, bool, GameGuildInvitationCommandHandler>(
            new ProtocolCommandMessageBinding(invite, typeof(GuildInvitationRequest)),
            new ProtocolCommandMessageBinding(reply, typeof(GuildInvitationRequest)));
        services.RegisterProtocolMessageHandler<GuildInvitationHandler, GuildInvitationRequest, GameGuildInvitationCommand>(invite);
        services.RegisterProtocolMessageHandler<GuildInvitationHandler, GuildInvitationRequest, GameGuildInvitationCommand>(reply);
    }
}
