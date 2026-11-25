using System.Buffers.Binary;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.UnansweredReports;

namespace Dfo.Server.Protocol.Game.PvpDailyMission;

internal sealed record PvpDailyHealthReport(GameMessageMetadata Metadata, IReadOnlyList<ushort> MissionIds)
    : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GamePvpMissionHpPercent;
}

internal sealed class PvpDailyHealthReportDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.IsEmpty || body.Length != 1 + body[0] * sizeof(ushort))
            return new PvpDailyHealthReport(metadata, []);
        var ids = new ushort[body[0]];
        for (var index = 0; index < ids.Length; index++)
            ids[index] = BinaryPrimitives.ReadUInt16LittleEndian(body[(1 + index * sizeof(ushort))..]);
        return new PvpDailyHealthReport(metadata, ids);
    }
}

internal sealed record GamePvpDailyHealthCommand(NetworkDispatchContext Dispatch, PvpDailyHealthReport Input)
    : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

internal sealed class PvpDailyHealthReportHandler(ICommandBus commands) : ClientMessageHandler<PvpDailyHealthReport>,
    IProtocolCommandMessageHandler<GamePvpDailyHealthCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, PvpDailyHealthReport message,
        CancellationToken cancellationToken) =>
        _ = await commands.SendAsync(new GamePvpDailyHealthCommand(context, message), cancellationToken).ConfigureAwait(false);
}

internal sealed class PvpDailyHealthCommandHandler(
    ISelectedSessionDomainService sessions,
    IPvpRoomDomainService rooms,
    ICommandWorkUnits units) : ICommandHandler<GamePvpDailyHealthCommand, bool>
{
    public ValueTask<bool> HandleAsync(CommandExecutionContext execution, GamePvpDailyHealthCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Input.MissionIds.Count == 0 || !command.Input.Metadata.SequenceIsFresh
            || sessions.FindByConnection(command.Dispatch.Connection.ConnectionId) is not { } session
            || !sessions.IsInWorld(session))
            return ValueTask.FromResult(false);
        return units.WriteAsync(execution, new CommandWorkUnitDeclaration("pvp.mission.hp", []),
            (_, _) => ValueTask.FromResult(rooms.RecordDailyHealth(session, command.Input.MissionIds)), cancellationToken);
    }
}

internal sealed record GamePvpDailySkillCommand(NetworkDispatchContext Dispatch, UseSkillReport Input)
    : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

internal sealed class PvpDailySkillReportHandler(ICommandBus commands) : ClientMessageHandler<UseSkillReport>,
    IProtocolCommandMessageHandler<GamePvpDailySkillCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, UseSkillReport message,
        CancellationToken cancellationToken) =>
        _ = await commands.SendAsync(new GamePvpDailySkillCommand(context, message), cancellationToken).ConfigureAwait(false);
}

internal sealed class PvpDailySkillCommandHandler(
    ISelectedSessionDomainService sessions,
    IPvpRoomQueryDomainService roomQuery,
    IPvpRoomDomainService rooms,
    ICommandWorkUnits units) : ICommandHandler<GamePvpDailySkillCommand, bool>
{
    public ValueTask<bool> HandleAsync(CommandExecutionContext execution, GamePvpDailySkillCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Input.Body.Length < 9 || !command.Input.Metadata.SequenceIsFresh
            || sessions.FindByConnection(command.Dispatch.Connection.ConnectionId) is not { } session
            || !sessions.IsInWorld(session)
            || roomQuery.FindSeated(session.CharacterId) is not { IsMatchmaking: true, Phase: PvpRoomPhase.Fighting })
            return ValueTask.FromResult(false);
        var skill = BinaryPrimitives.ReadUInt16LittleEndian(command.Input.Body.AsSpan(1));
        return units.WriteAsync(execution, new CommandWorkUnitDeclaration("pvp.mission.skill", []),
            (_, ct) => rooms.RecordDailySkillAsync(session, skill, ct), cancellationToken);
    }
}
