using System.Buffers.Binary;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.PvpDailyMission;

internal sealed record PvpDailyWinPoseReport(GameMessageMetadata Metadata, ushort? MissionId, byte Ordinal)
    : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GamePvpMissionWinPose;
}

internal sealed class PvpDailyWinPoseReportDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        body.Length == 3
            ? new PvpDailyWinPoseReport(metadata, BinaryPrimitives.ReadUInt16LittleEndian(body), body[2])
            : new PvpDailyWinPoseReport(metadata, null, 0);
}

internal sealed record GamePvpDailyWinPoseCommand(NetworkDispatchContext Dispatch, PvpDailyWinPoseReport Input)
    : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

internal sealed class PvpDailyWinPoseReportHandler(ICommandBus commands) : ClientMessageHandler<PvpDailyWinPoseReport>,
    IProtocolCommandMessageHandler<GamePvpDailyWinPoseCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, PvpDailyWinPoseReport message,
        CancellationToken cancellationToken) =>
        _ = await commands.SendAsync(new GamePvpDailyWinPoseCommand(context, message), cancellationToken).ConfigureAwait(false);
}

internal sealed class PvpDailyWinPoseCommandHandler(
    ISelectedSessionDomainService sessions,
    IPvpRoomDomainService rooms,
    ICommandWorkUnits units) : ICommandHandler<GamePvpDailyWinPoseCommand, bool>
{
    public ValueTask<bool> HandleAsync(CommandExecutionContext execution, GamePvpDailyWinPoseCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Input.MissionId is not { } missionId
            || sessions.FindByConnection(command.Dispatch.Connection.ConnectionId) is not { } session
            || !sessions.IsInWorld(session))
            return ValueTask.FromResult(false);
        return units.WriteAsync(execution, new CommandWorkUnitDeclaration("pvp.mission.pose", []),
            (_, _) => ValueTask.FromResult(rooms.RecordDailyWinPose(session, missionId, command.Input.Ordinal)), cancellationToken);
    }
}
