using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.GuildAttendance;

public sealed record GuildAttendanceDetailRequest(GameMessageMetadata Metadata, bool IsEmpty) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameGuildAttendanceDetail;
}

internal sealed class GuildAttendanceDetailDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new GuildAttendanceDetailRequest(metadata, body.IsEmpty);
}

internal sealed record GuildAttendanceDetailReply(IReadOnlyList<byte[]>? Names = null) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameGuildAttendanceDetail;
}

internal sealed class GuildAttendanceDetailEncoder : GameCmdMessageEncoder<GuildAttendanceDetailReply>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GuildAttendanceDetailReply message)
    {
        if (message.Names is not { } names)
            return OnePayload(message, GameCmdEnvelope.Refuse(0));

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(checked((ushort)names.Count));
        foreach (var name in names)
            ChatDString.Write(writer, name);
        return OnePayload(message, GameCmdEnvelope.Accept(stream.ToArray()));
    }
}

public sealed class GuildAttendanceDetailRequestHandler(ICommandBus commands)
    : ClientMessageHandler<GuildAttendanceDetailRequest>, IProtocolCommandMessageHandler<GameGuildAttendanceDetailCommand>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context, GuildAttendanceDetailRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameGuildAttendanceDetailCommand(context, message), ct).ConfigureAwait(false);
}

public sealed record GameGuildAttendanceDetailCommand(NetworkDispatchContext Dispatch, GuildAttendanceDetailRequest Request)
    : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Request;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Request.Metadata);
}

internal sealed class GameGuildAttendanceDetailCommandHandler(
    ISelectedSessionDomainService sessions,
    IGuildMembershipQueryDomainService guilds,
    IGuildAttendanceDomainService attendance,
    ICommandWorkUnits units,
    IGameCommandOutbox outbox) : ICommandHandler<GameGuildAttendanceDetailCommand, bool>
{
    public async ValueTask<bool> HandleAsync(
        CommandExecutionContext context, GameGuildAttendanceDetailCommand command, CancellationToken ct)
    {
        var connection = command.Dispatch.Connection;
        var reply = new GuildAttendanceDetailReply();
        if (command.Request.IsEmpty && sessions.TryGetInWorld(connection.ConnectionId, out var viewer))
        {
            var recipient = new SelectedSessionRecipient(viewer, connection, command.Request.Metadata);
            reply = await units.ReadOnlyAsync(context, new("guild-attendance-detail", [recipient]),
                async token => await guilds.FindMembershipAsync(viewer.CharacterId, token).ConfigureAwait(false) is null
                    ? new GuildAttendanceDetailReply()
                    : new GuildAttendanceDetailReply(await attendance.ReadAttendanceTodayAsync(viewer.CharacterId, token)
                        .ConfigureAwait(false)), ct).ConfigureAwait(false);
        }

        await outbox.RegisterCmdAsync(connection.ConnectionId, reply, ct).ConfigureAwait(false);
        return reply.Names is not null;
    }
}

internal static class GuildAttendanceRegistration
{
    internal static void AddGuildAttendanceSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<GuildAttendanceDetailDecoder>(ClientMessageType.GameGuildAttendanceDetail);
        services.AddGameCmdMessageEncoder<GuildAttendanceDetailReply, GuildAttendanceDetailEncoder>();
        services.AddProtocolCommandHandler<GameGuildAttendanceDetailCommand, bool, GameGuildAttendanceDetailCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameGuildAttendanceDetail, typeof(GuildAttendanceDetailRequest)));
        services.RegisterProtocolMessageHandler<GuildAttendanceDetailRequestHandler, GuildAttendanceDetailRequest, GameGuildAttendanceDetailCommand>(
            ClientMessageType.GameGuildAttendanceDetail);
    }
}
