using System.Buffers.Binary;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.GuildMemberInfo;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.GuildRankAssignment;

public sealed record GuildRankAssignmentRequest(GameMessageMetadata Metadata, byte[]? Name, byte Rank)
    : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameGuildAssignRank;
}

internal sealed class GuildRankAssignmentDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length >= 6)
        {
            var length = BinaryPrimitives.ReadInt32LittleEndian(body);
            var rank = body[^1];
            if (length is >= 1 and <= 62 && length == body.Length - 5 &&
                rank is >= GuildRankSettings.ViceLeader and <= GuildRankSettings.LastRank)
                return new GuildRankAssignmentRequest(metadata, body.Slice(4, length).ToArray(), rank);
        }
        return new GuildRankAssignmentRequest(metadata, null, 0);
    }
}

internal sealed record GuildRankAssignmentReply(byte[]? Name = null, byte Rank = 0) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameGuildAssignRank;
}

internal sealed class GuildRankAssignmentReplyEncoder : GameCmdMessageEncoder<GuildRankAssignmentReply>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GuildRankAssignmentReply message)
    {
        if (message.Name is not { } name)
            return OnePayload(message, GameCmdEnvelope.Refuse(0x22));

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)0);
        ChatDString.Write(writer, name);
        writer.Write(message.Rank);
        return OnePayload(message, GameCmdEnvelope.Accept(stream.ToArray()));
    }
}

internal sealed record OwnGuildRankChanged(byte Rank) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameGuildMemberRankChanged;
}

internal sealed class OwnGuildRankChangedEncoder : GameNotiMessageEncoder<OwnGuildRankChanged>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(OwnGuildRankChanged message) =>
        OnePayload(message, new byte[] { message.Rank });
}

internal sealed record GuildMemberRankAssigned(CharacterId Target, byte Rank) : INotification<GuildMemberRankAssigned>;

internal sealed class GuildMemberRankAssignedHandler : INotificationHandler<GuildMemberRankAssigned>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient, GuildMemberRankAssigned changed, CancellationToken ct) =>
        ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(recipient.CharacterId == changed.Target
            ? [new GuildMemberInfoMessage((GuildRank)changed.Rank), new OwnGuildRankChanged(changed.Rank)]
            : []);
}

public sealed class GuildRankAssignmentRequestHandler(ICommandBus commands)
    : ClientMessageHandler<GuildRankAssignmentRequest>, IProtocolCommandMessageHandler<GameGuildRankAssignmentCommand>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context, GuildRankAssignmentRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameGuildRankAssignmentCommand(context, message), ct).ConfigureAwait(false);
}

public sealed record GameGuildRankAssignmentCommand(NetworkDispatchContext Dispatch, GuildRankAssignmentRequest Request)
    : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Request;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Request.Metadata);
}

internal sealed class GameGuildRankAssignmentCommandHandler(
    IGuildMembershipDomainService guilds, ISelectedSessionDomainService sessions, ICommandWorkUnits units,
    IGameCommandOutbox outbox, ICommandProjections projections) : ICommandHandler<GameGuildRankAssignmentCommand, bool>
{
    public async ValueTask<bool> HandleAsync(
        CommandExecutionContext context, GameGuildRankAssignmentCommand command, CancellationToken ct)
    {
        var connection = command.Dispatch.Connection;
        var request = command.Request;
        GuildRankAssignmentResult? assignment = null;
        if (request.Name is { } name && sessions.TryGetInWorld(connection.ConnectionId, out var actor))
        {
            var recipient = new SelectedSessionRecipient(actor, connection, request.Metadata);
            var result = await units.WriteAsync(context, new("assign-guild-rank", [recipient]), async (_, token) =>
                new AssignmentResult(await guilds.AssignRankAsync(actor.CharacterId, name, request.Rank, token)
                    .ConfigureAwait(false)), ct).ConfigureAwait(false);
            assignment = result.Assignment;
        }

        var reply = assignment is null ? new GuildRankAssignmentReply() : new GuildRankAssignmentReply(request.Name, assignment.Rank);
        await outbox.RegisterCmdAsync(connection.ConnectionId, reply, ct).ConfigureAwait(false);
        if (assignment is { Changed: true })
        {
            await projections.ProjectAsync(new NotificationAudience.TheseCharacters([assignment.Character]),
                new GuildMemberRankAssigned(assignment.Character, assignment.Rank), ct).ConfigureAwait(false);
        }
        return assignment is not null;
    }

    private sealed record AssignmentResult(GuildRankAssignmentResult? Assignment);
}

internal static class GuildRankAssignmentRegistration
{
    internal static void AddGuildRankAssignmentSlice(this IServiceCollection services)
    {
        const ClientMessageType type = ClientMessageType.GameGuildAssignRank;
        services.AddGameCmdMessageDecoder<GuildRankAssignmentDecoder>(type);
        services.AddGameCmdMessageEncoder<GuildRankAssignmentReply, GuildRankAssignmentReplyEncoder>();
        services.AddGameNotiMessageEncoder<OwnGuildRankChanged, OwnGuildRankChangedEncoder>();
        services.AddNotificationHandler<GuildMemberRankAssigned, GuildMemberRankAssignedHandler>();
        services.AddProtocolCommandHandler<GameGuildRankAssignmentCommand, bool, GameGuildRankAssignmentCommandHandler>(
            new ProtocolCommandMessageBinding(type, typeof(GuildRankAssignmentRequest)));
        services.RegisterProtocolMessageHandler<GuildRankAssignmentRequestHandler, GuildRankAssignmentRequest, GameGuildRankAssignmentCommand>(type);
    }
}
