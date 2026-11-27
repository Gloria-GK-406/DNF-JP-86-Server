using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.GuildCreation;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.GuildGifts;

public sealed record GuildGiftRequest(ClientMessageType MessageType, GameMessageMetadata Metadata, bool Valid,
    GuildId Recipient = default, ushort ProductId = 0, GuildGiftKey Key = default) : IClientNetworkMessage;

internal abstract class GuildGiftDecoder(ClientMessageType type) : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        switch (type)
        {
            case ClientMessageType.GameGuildSendGift when body.Length == 6:
                var product = BinaryPrimitives.ReadUInt16LittleEndian(body);
                var target = BinaryPrimitives.ReadUInt32LittleEndian(body[2..]);
                return new GuildGiftRequest(type, metadata, product != 0 && target != 0, new(target), product);
            case ClientMessageType.GameGuildClaimGift when body.Length == 11:
                var sender = BinaryPrimitives.ReadUInt32LittleEndian(body);
                var content = BinaryPrimitives.ReadUInt16LittleEndian(body[4..]);
                var sent = BinaryPrimitives.ReadUInt32LittleEndian(body[7..]);
                return new GuildGiftRequest(type, metadata, sender != 0 && content != 0 && sent != 0,
                    Key: new(new(sender), content, body[6], sent));
            case ClientMessageType.GameGuildGiftList or ClientMessageType.GameGuildGiftHistory:
                return new GuildGiftRequest(type, metadata, body.IsEmpty);
            default:
                return new GuildGiftRequest(type, metadata, false);
        }
    }
}

internal sealed class GuildSendGiftDecoder() : GuildGiftDecoder(ClientMessageType.GameGuildSendGift);
internal sealed class GuildGiftListDecoder() : GuildGiftDecoder(ClientMessageType.GameGuildGiftList);
internal sealed class GuildGiftHistoryDecoder() : GuildGiftDecoder(ClientMessageType.GameGuildGiftHistory);
internal sealed class GuildClaimGiftDecoder() : GuildGiftDecoder(ClientMessageType.GameGuildClaimGift);

internal sealed record GuildGiftReply(ServerMessageType MessageType, byte? Error = null, GuildGiftView? View = null)
    : IGameCmdMessage;

internal sealed class GuildGiftReplyEncoder : GameCmdMessageEncoder<GuildGiftReply>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GuildGiftReply message)
    {
        if (message.Error is { } error)
            return OnePayload(message, GameCmdEnvelope.Refuse(error));
        if (message.View is not { } view)
            return OnePayload(message, GameCmdEnvelope.Accept([]));

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)1);
        writer.Write(checked((byte)view.Rows.Count));
        foreach (var gift in view.Rows)
        {
            writer.Write(gift.Sender.Value);
            ChatDString.Write(writer, GuildCreationPayloadCodec.EncodeGuildText(gift.SenderName));
            writer.Write(checked((ushort)gift.Content.ProductId));
            writer.Write(checked((uint)gift.SentAt));
        }
        return OnePayload(message, stream.ToArray());
    }
}

internal sealed record GuildGiftListsChanged(GuildGiftView Pending, GuildGiftView History, string? Notice = null)
    : INotification<GuildGiftListsChanged>;

internal sealed class GuildGiftListsChangedHandler : INotificationHandler<GuildGiftListsChanged>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        GuildGiftListsChanged changed, CancellationToken ct)
    {
        List<IServerNetworkMessage> messages =
        [
            new GuildGiftReply(ServerMessageType.GameGuildGiftList, View: changed.Pending),
            new GuildGiftReply(ServerMessageType.GameGuildGiftHistory, View: changed.History),
        ];
        if (changed.Notice is { } notice)
            messages.Add(new GameServerNoticeMessage(notice));
        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(messages);
    }
}

internal sealed class GuildGiftHandler(ICommandBus commands)
    : ClientMessageHandler<GuildGiftRequest>, IProtocolCommandMessageHandler<GameGuildGiftCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, GuildGiftRequest message,
        CancellationToken ct) => _ = await commands.SendAsync(new GameGuildGiftCommand(context, message), ct).ConfigureAwait(false);
}

internal static class GuildGiftRegistration
{
    public static void AddGuildGiftSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<GuildSendGiftDecoder>(ClientMessageType.GameGuildSendGift);
        services.AddGameCmdMessageDecoder<GuildGiftListDecoder>(ClientMessageType.GameGuildGiftList);
        services.AddGameCmdMessageDecoder<GuildGiftHistoryDecoder>(ClientMessageType.GameGuildGiftHistory);
        services.AddGameCmdMessageDecoder<GuildClaimGiftDecoder>(ClientMessageType.GameGuildClaimGift);
        services.AddGameCmdMessageEncoder<GuildGiftReply, GuildGiftReplyEncoder>();
        services.AddNotificationHandler<GuildGiftListsChanged, GuildGiftListsChangedHandler>();
        ClientMessageType[] types =
        [
            ClientMessageType.GameGuildSendGift, ClientMessageType.GameGuildGiftList,
            ClientMessageType.GameGuildGiftHistory, ClientMessageType.GameGuildClaimGift,
        ];
        services.AddProtocolCommandHandler<GameGuildGiftCommand, bool, GameGuildGiftCommandHandler>(
            types.Select(type => new ProtocolCommandMessageBinding(type, typeof(GuildGiftRequest))).ToArray());
        foreach (var type in types)
            services.RegisterProtocolMessageHandler<GuildGiftHandler, GuildGiftRequest, GameGuildGiftCommand>(type);
    }
}
