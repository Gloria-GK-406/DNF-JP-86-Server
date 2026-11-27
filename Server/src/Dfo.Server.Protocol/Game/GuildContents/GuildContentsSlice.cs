using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Guilds;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.CharacterBuffs;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.GuildContents;

public sealed record GuildBuyContentRequest(GameMessageMetadata Metadata, uint ProductId) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameGuildBuyContent;
}

internal sealed class GuildBuyContentDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new GuildBuyContentRequest(metadata, body.Length == sizeof(uint) ? BinaryPrimitives.ReadUInt32LittleEndian(body) : 0);
}

internal sealed record GuildBuyContentReply(byte? Error) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameGuildBuyContent;
}

internal sealed class GuildBuyContentReplyEncoder : GameCmdMessageEncoder<GuildBuyContentReply>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GuildBuyContentReply message) =>
        OnePayload(message, message.Error is { } error ? GameCmdEnvelope.Refuse(error) : GameCmdEnvelope.Accept([]));
}

internal sealed record GuildAttributeBenefitGranted(int BuffId) : INotification<GuildAttributeBenefitGranted>;

internal sealed class GuildAttributeBenefitGrantedHandler : INotificationHandler<GuildAttributeBenefitGranted>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient, GuildAttributeBenefitGranted granted, CancellationToken ct) =>
        ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(
            [new CharacterBuffRemoved(granted.BuffId), new CharacterBuffAdded(granted.BuffId, State1: 1)]);
}

internal sealed class GuildAttributeBenefitExpiredHandler : INotificationHandler<GuildAttributeBenefitExpired>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient, GuildAttributeBenefitExpired expired, CancellationToken ct) =>
        ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([new CharacterBuffRemoved(expired.BuffId)]);
}

public sealed class GuildBuyContentRequestHandler(ICommandBus commands)
    : ClientMessageHandler<GuildBuyContentRequest>, IProtocolCommandMessageHandler<GameGuildBuyContentCommand>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context, GuildBuyContentRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameGuildBuyContentCommand(context, message), ct).ConfigureAwait(false);
}

internal static class GuildContentsRegistration
{
    internal static void AddGuildContentsSlice(this IServiceCollection services)
    {
        const ClientMessageType buy = ClientMessageType.GameGuildBuyContent;
        const ClientMessageType teleport = ClientMessageType.GameGuildPartyTeleport;
        services.AddGameCmdMessageDecoder<GuildBuyContentDecoder>(buy);
        services.AddGameCmdMessageEncoder<GuildBuyContentReply, GuildBuyContentReplyEncoder>();
        services.AddNotificationHandler<GuildAttributeBenefitGranted, GuildAttributeBenefitGrantedHandler>();
        services.AddNotificationHandler<GuildAttributeBenefitExpired, GuildAttributeBenefitExpiredHandler>();
        services.AddProtocolCommandHandler<GameGuildBuyContentCommand, bool, GameGuildBuyContentCommandHandler>(
            new ProtocolCommandMessageBinding(buy, typeof(GuildBuyContentRequest)));
        services.RegisterProtocolMessageHandler<GuildBuyContentRequestHandler, GuildBuyContentRequest, GameGuildBuyContentCommand>(buy);

        services.AddGameCmdMessageDecoder<GuildPartyTeleportDecoder>(teleport);
        services.AddGameCmdMessageEncoder<GuildPartyTeleportReply, GuildPartyTeleportReplyEncoder>();
        services.AddProtocolCommandHandler<GameGuildPartyTeleportCommand, bool, GameGuildPartyTeleportCommandHandler>(
            new ProtocolCommandMessageBinding(teleport, typeof(GuildPartyTeleportRequest)));
        services.RegisterProtocolMessageHandler<GuildPartyTeleportRequestHandler, GuildPartyTeleportRequest, GameGuildPartyTeleportCommand>(teleport);
    }
}
