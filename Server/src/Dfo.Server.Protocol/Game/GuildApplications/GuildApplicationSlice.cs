using System.Buffers.Binary;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Framing;
using Dfo.Server.Protocol.Game.GuildCreation;
using Dfo.Server.Protocol.Game.GuildMemberInfo;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.GuildApplications;

public sealed record GuildApplicationRequest(ClientMessageType MessageType, GameMessageMetadata Metadata, bool Valid,
    string? GuildName = null, string? Message = null, uint Id = 0) : IClientNetworkMessage;

internal sealed class GuildApplyDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        const ClientMessageType type = ClientMessageType.GameGuildApply;
        if (body.Length >= 8 && BinaryPrimitives.ReadInt32LittleEndian(body) is >= 2 and <= 24 and var length
            && length <= body.Length - 8)
        {
            var guild = GuildCreationPayloadCodec.DecodeText(body[..(4 + length)], 2, 24);
            var message = GuildCreationPayloadCodec.DecodeText(body[(4 + length)..], 0, 80);
            if (guild is not null && message is not null)
                return new GuildApplicationRequest(type, metadata, true, guild, message);
        }
        return new GuildApplicationRequest(type, metadata, false);
    }
}

internal abstract class GuildApplicationRequestDecoder(ClientMessageType type, bool hasId) : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        hasId && body.Length == 4
            ? new GuildApplicationRequest(type, metadata, true, Id: BinaryPrimitives.ReadUInt32LittleEndian(body))
            : new GuildApplicationRequest(type, metadata, !hasId && body.IsEmpty);
}

internal sealed class GuildApplicationCancelDecoder() : GuildApplicationRequestDecoder(ClientMessageType.GameGuildCancelApplication, true);
internal sealed class GuildApplicationApproveDecoder() : GuildApplicationRequestDecoder(ClientMessageType.GameGuildApproveApplication, true);
internal sealed class GuildApplicationRejectDecoder() : GuildApplicationRequestDecoder(ClientMessageType.GameGuildRejectApplication, true);
internal sealed class GuildApplicationListDecoder() : GuildApplicationRequestDecoder(ClientMessageType.GameGuildApplicationList, false);
internal sealed class GuildApplicationStatusDecoder() : GuildApplicationRequestDecoder(ClientMessageType.GameGuildApplicationStatus, false);

internal sealed record GuildApplyReply(GuildPendingApplication? Pending = null) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameGuildApply;
}

internal sealed record GuildApproveReply(uint Applicant, bool Accepted) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameGuildApproveApplication;
}

internal sealed record GuildApplicationListReply(IReadOnlyList<GuildApplicationEntry>? Entries = null) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameGuildApplicationList;
}

internal sealed record GuildApplicationStatusMessage(GuildPendingApplication? Pending) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameGuildApplicationStatus;
}

internal static class GuildApplicationPayload
{
    public static void WriteDetails(BinaryWriter writer, GuildPendingApplication pending)
    {
        writer.Write(pending.Application.Guild.Value);
        WriteText(writer, pending.GuildName);
        WriteText(writer, pending.Application.Message);
    }

    public static void WriteText(BinaryWriter writer, string text) =>
        ChatDString.Write(writer, GuildCreationPayloadCodec.EncodeGuildText(text));
}

internal sealed class GuildApplyReplyEncoder : GameCmdMessageEncoder<GuildApplyReply>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GuildApplyReply message)
    {
        if (message.Pending is not { } pending)
            return OnePayload(message, GameCmdEnvelope.Refuse(0x9F));
        using var body = new MemoryStream();
        using var writer = new BinaryWriter(body);
        GuildApplicationPayload.WriteDetails(writer, pending);
        writer.Write(0u);
        return OnePayload(message, GameCmdEnvelope.Accept(body.ToArray()));
    }
}

internal sealed class GuildApproveReplyEncoder : GameCmdMessageEncoder<GuildApproveReply>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GuildApproveReply message)
    {
        var body = new byte[message.Accepted ? 5 : 6];
        body[0] = message.Accepted ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(body.Length - 4), message.Applicant);
        return OnePayload(message, body);
    }
}

internal sealed class GuildApplicationListReplyEncoder(TimeProvider clock) : GameCmdMessageEncoder<GuildApplicationListReply>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GuildApplicationListReply message)
    {
        if (message.Entries is not { } entries)
            return OnePayload(message, GameCmdEnvelope.Refuse(0));

        using var body = new MemoryStream();
        using var writer = new BinaryWriter(body);
        writer.Write((byte)1);
        writer.Write(0);
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var count = 0;
        foreach (var row in entries)
        {
            var start = body.Position;
            writer.Write(row.Application.Character.Value);
            ChatDString.Write(writer, row.Name);
            writer.Write(checked((byte)row.Job));
            writer.Write(checked((byte)row.GrowType));
            writer.Write(checked((byte)row.Level));
            writer.Write((byte)0);
            GuildApplicationPayload.WriteText(writer, row.Application.Message);
            writer.Write((uint)Math.Clamp(now - row.Application.AppliedAt.ToUnixTimeSeconds(), 0L, int.MaxValue));
            if (body.Length + GameServerFrameEncoder.HeaderLength > DnfFrameProtocol.MaximumFrameLength)
            {
                body.SetLength(start);
                break;
            }
            count++;
        }
        body.Position = 1;
        writer.Write(count);
        return OnePayload(message, body.ToArray());
    }
}

internal sealed class GuildApplicationStatusEncoder : GameNotiMessageEncoder<GuildApplicationStatusMessage>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GuildApplicationStatusMessage message)
    {
        using var body = new MemoryStream();
        using var writer = new BinaryWriter(body);
        writer.Write(message.Pending is not null);
        if (message.Pending is { } pending)
        {
            GuildApplicationPayload.WriteDetails(writer, pending);
            writer.Write(0u);
            writer.Write(0u);
        }
        return OnePayload(message, body.ToArray());
    }
}

internal sealed record GuildApplicationResolved(CharacterId Applicant, GuildMember? JoinedMember)
    : INotification<GuildApplicationResolved>;

internal sealed class GuildApplicationResolvedHandler(IGuildQueryDomainService query)
    : INotificationHandler<GuildApplicationResolved>
{
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient, GuildApplicationResolved result, CancellationToken ct)
    {
        if (recipient.CharacterId != result.Applicant)
            return [];

        var pending = new GuildApplicationStatusMessage(
            await query.FindPendingApplicationAsync(result.Applicant, ct).ConfigureAwait(false));
        return result.JoinedMember is { } member ? [GuildMemberInfoMessage.From(member), pending] : [pending];
    }
}

public sealed class GuildApplicationRequestHandler(ICommandBus commands)
    : ClientMessageHandler<GuildApplicationRequest>, IProtocolCommandMessageHandler<GameGuildApplicationCommand>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context, GuildApplicationRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameGuildApplicationCommand(context, message), ct).ConfigureAwait(false);
}

internal static class GuildApplicationRegistration
{
    internal static void AddGuildApplicationSlice(this IServiceCollection services)
    {
        ClientMessageType[] commands =
        [
            ClientMessageType.GameGuildApply,
            ClientMessageType.GameGuildCancelApplication,
            ClientMessageType.GameGuildApproveApplication,
            ClientMessageType.GameGuildRejectApplication,
            ClientMessageType.GameGuildApplicationList,
            ClientMessageType.GameGuildApplicationStatus,
        ];
        services.AddGameCmdMessageDecoder<GuildApplyDecoder>(ClientMessageType.GameGuildApply);
        services.AddGameCmdMessageDecoder<GuildApplicationCancelDecoder>(ClientMessageType.GameGuildCancelApplication);
        services.AddGameCmdMessageDecoder<GuildApplicationApproveDecoder>(ClientMessageType.GameGuildApproveApplication);
        services.AddGameCmdMessageDecoder<GuildApplicationRejectDecoder>(ClientMessageType.GameGuildRejectApplication);
        services.AddGameCmdMessageDecoder<GuildApplicationListDecoder>(ClientMessageType.GameGuildApplicationList);
        services.AddGameCmdMessageDecoder<GuildApplicationStatusDecoder>(ClientMessageType.GameGuildApplicationStatus);
        services.AddGameCmdMessageEncoder<GuildApplyReply, GuildApplyReplyEncoder>();
        services.AddGameCmdMessageEncoder<GuildApproveReply, GuildApproveReplyEncoder>();
        services.AddGameCmdMessageEncoder<GuildApplicationListReply, GuildApplicationListReplyEncoder>();
        services.AddGameNotiMessageEncoder<GuildApplicationStatusMessage, GuildApplicationStatusEncoder>();
        services.AddNotificationHandler<GuildApplicationResolved, GuildApplicationResolvedHandler>();
        services.AddProtocolCommandHandler<GameGuildApplicationCommand, bool, GameGuildApplicationCommandHandler>(
            commands.Select(type => new ProtocolCommandMessageBinding(type, typeof(GuildApplicationRequest))).ToArray());
        foreach (var type in commands)
            services.RegisterProtocolMessageHandler<GuildApplicationRequestHandler, GuildApplicationRequest, GameGuildApplicationCommand>(type);
    }
}
