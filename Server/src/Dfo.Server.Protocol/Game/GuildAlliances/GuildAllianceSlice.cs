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

namespace Dfo.Server.Protocol.Game.GuildAlliances;

public sealed record GuildAllianceRequest(ClientMessageType MessageType, GameMessageMetadata Metadata, bool Valid,
    GuildId Target = default, string? Name = null, string? Message = null) : IClientNetworkMessage;

internal abstract class GuildAllianceDecoder(ClientMessageType type) : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        var invalid = new GuildAllianceRequest(type, metadata, false);
        if (type is ClientMessageType.GameGuildAllianceIncoming or ClientMessageType.GameGuildAllianceOutgoing)
            return invalid with { Valid = body.IsEmpty };
        if (body.Length < sizeof(uint) || BinaryPrimitives.ReadUInt32LittleEndian(body) is not (> 0 and var id))
            return invalid;

        var target = new GuildId(id);
        body = body[sizeof(uint)..];
        if (type is ClientMessageType.GameGuildAllianceWithdraw or ClientMessageType.GameGuildAllianceReject)
            return invalid with { Valid = body.IsEmpty, Target = target };
        var name = ReadText(ref body, 72);
        if (name is null)
            return invalid;
        if (type is not ClientMessageType.GameGuildAllianceApply)
            return invalid with { Valid = body.IsEmpty, Target = target, Name = name };
        var message = ReadText(ref body, 80);
        return message is not null && body.IsEmpty
            ? new GuildAllianceRequest(type, metadata, true, target, name, message)
            : invalid;
    }

    private static string? ReadText(ref ReadOnlySpan<byte> body, int maxBytes)
    {
        if (body.Length < sizeof(int))
            return null;
        var length = BinaryPrimitives.ReadInt32LittleEndian(body);
        if (length < 0 || length > maxBytes || length > body.Length - sizeof(int))
            return null;
        var field = body[..(sizeof(int) + length)];
        body = body[field.Length..];
        return GuildCreationPayloadCodec.DecodeText(field, 0, maxBytes);
    }
}

internal sealed class GuildAllianceApplyDecoder() : GuildAllianceDecoder(ClientMessageType.GameGuildAllianceApply);
internal sealed class GuildAllianceOutgoingDecoder() : GuildAllianceDecoder(ClientMessageType.GameGuildAllianceOutgoing);
internal sealed class GuildAllianceWithdrawDecoder() : GuildAllianceDecoder(ClientMessageType.GameGuildAllianceWithdraw);
internal sealed class GuildAllianceIncomingDecoder() : GuildAllianceDecoder(ClientMessageType.GameGuildAllianceIncoming);
internal sealed class GuildAllianceApproveDecoder() : GuildAllianceDecoder(ClientMessageType.GameGuildAllianceApprove);
internal sealed class GuildAllianceRejectDecoder() : GuildAllianceDecoder(ClientMessageType.GameGuildAllianceReject);
internal sealed class GuildAllianceDissolveDecoder() : GuildAllianceDecoder(ClientMessageType.GameGuildAllianceDissolve);

internal sealed record GuildAllianceReply(ServerMessageType MessageType, byte? Error = null,
    IReadOnlyList<GuildAllianceApplication>? Applications = null) : IGameCmdMessage;

internal sealed class GuildAllianceReplyEncoder : GameCmdMessageEncoder<GuildAllianceReply>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GuildAllianceReply message)
    {
        if (message.Error is { } error)
            return OnePayload(message, GameCmdEnvelope.Refuse(error));
        if (message.Applications is not { } applications)
            return OnePayload(message, GameCmdEnvelope.Accept([]));
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)1);
        writer.Write(checked((byte)applications.Count));
        foreach (var row in applications)
        {
            writer.Write(row.Other.Id.Value);
            ChatDString.Write(writer, GuildCreationPayloadCodec.EncodeGuildText(row.Other.Name));
            ChatDString.Write(writer, GuildCreationPayloadCodec.EncodeGuildText(row.Message));
            writer.Write(checked((uint)row.AppliedAt));
        }
        return OnePayload(message, stream.ToArray());
    }
}

internal sealed record GuildAlliancePeerChanged(string Text, GuildAllianceReply? ApplicationList)
    : INotification<GuildAlliancePeerChanged>;

internal sealed class GuildAlliancePeerChangedHandler : INotificationHandler<GuildAlliancePeerChanged>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        GuildAlliancePeerChanged changed, CancellationToken ct) =>
        ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(changed.ApplicationList is { } list
            ? [list, new GameServerNoticeMessage(changed.Text)]
            : [new GameServerNoticeMessage(changed.Text)]);
}

internal sealed class GuildAllianceHandler(ICommandBus commands)
    : ClientMessageHandler<GuildAllianceRequest>, IProtocolCommandMessageHandler<GameGuildAllianceCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, GuildAllianceRequest message,
        CancellationToken ct) => _ = await commands.SendAsync(new GameGuildAllianceCommand(context, message), ct).ConfigureAwait(false);
}

internal static class GuildAllianceRegistration
{
    public static void AddGuildAllianceSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<GuildAllianceApplyDecoder>(ClientMessageType.GameGuildAllianceApply);
        services.AddGameCmdMessageDecoder<GuildAllianceOutgoingDecoder>(ClientMessageType.GameGuildAllianceOutgoing);
        services.AddGameCmdMessageDecoder<GuildAllianceWithdrawDecoder>(ClientMessageType.GameGuildAllianceWithdraw);
        services.AddGameCmdMessageDecoder<GuildAllianceIncomingDecoder>(ClientMessageType.GameGuildAllianceIncoming);
        services.AddGameCmdMessageDecoder<GuildAllianceApproveDecoder>(ClientMessageType.GameGuildAllianceApprove);
        services.AddGameCmdMessageDecoder<GuildAllianceRejectDecoder>(ClientMessageType.GameGuildAllianceReject);
        services.AddGameCmdMessageDecoder<GuildAllianceDissolveDecoder>(ClientMessageType.GameGuildAllianceDissolve);
        services.AddGameCmdMessageEncoder<GuildAllianceReply, GuildAllianceReplyEncoder>();
        services.AddNotificationHandler<GuildAlliancePeerChanged, GuildAlliancePeerChangedHandler>();
        ClientMessageType[] types =
        [
            ClientMessageType.GameGuildAllianceApply, ClientMessageType.GameGuildAllianceOutgoing,
            ClientMessageType.GameGuildAllianceWithdraw, ClientMessageType.GameGuildAllianceIncoming,
            ClientMessageType.GameGuildAllianceApprove, ClientMessageType.GameGuildAllianceReject,
            ClientMessageType.GameGuildAllianceDissolve,
        ];
        services.AddProtocolCommandHandler<GameGuildAllianceCommand, bool, GameGuildAllianceCommandHandler>(
            types.Select(type => new ProtocolCommandMessageBinding(type, typeof(GuildAllianceRequest))).ToArray());
        foreach (var type in types)
            services.RegisterProtocolMessageHandler<GuildAllianceHandler, GuildAllianceRequest, GameGuildAllianceCommand>(type);
    }
}
