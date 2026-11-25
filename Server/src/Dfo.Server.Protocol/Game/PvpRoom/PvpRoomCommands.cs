using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.PvpTotalMatchTeam;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.PvpRoom;

public sealed record GameMakePvpRoomCommand(NetworkDispatchContext Dispatch, MakePvpRoomRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed record GameEnterPvpRoomCommand(NetworkDispatchContext Dispatch, EnterPvpRoomRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed record GameQuickJoinPvpRoomCommand(NetworkDispatchContext Dispatch, QuickJoinPvpRoomRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed record GameSetPvpSeatStateCommand(NetworkDispatchContext Dispatch, SetPvpSeatStateRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed record GameSetPvpReadyStateCommand(NetworkDispatchContext Dispatch, SetPvpReadyStateRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed record GameSetPvpTeamModeCommand(NetworkDispatchContext Dispatch, SetPvpTeamModeRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed record GameSetPvpMapIndexCommand(NetworkDispatchContext Dispatch, SetPvpMapIndexRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

/// <summary>The duel room commands: one room change in one write unit each, projected once the unit has returned.</summary>
/// <remarks>
/// A refusal, from the room owner or for a request it never sees (malformed, an unknown
/// seat), answers <c>00 reason</c> on the command's own number (<see cref="PvpRoomRefusalCode"/>). A success
/// answers only through the projection; making a room has no reply of its own (upstream), entering one has
/// <c>01 + 8 × ready</c> in the newcomer's own frames. Quick join first acknowledges its own request.
/// </remarks>
internal sealed class GamePvpRoomCommandHandler(
    ISelectedSessionDomainService sessions,
    IPvpRoomDomainService rooms,
    IPvpLobbyDomainService lobby,
    ISelectedCharacterCurrentViewDomainService currentViews,
    IOnlineCharacterRuntimeDomainService online,
    IOnlineUserQueryDomainService onlineUsers,
    IGameSessionEndpointDomainService endpoints,
    ITownAreaDomainService townAreas,
    IPartyService parties,
    IPartyProjectionStateDomainService projectionState,
    IWeaknessDomainService weakness,
    ICommandWorkUnits units,
    ICommandProjections projections,
    IGameCommandOutbox outbox,
    IPvpResultScreenDeferralDomainService deferrals,
    ILogger<GamePvpRoomCommandHandler> logger,
    IPvpTotalMatchCandidateQueryDomainService totalMatchCandidates,
    TimeProvider clock)
    : ICommandHandler<GameMakePvpRoomCommand, bool>,
        ICommandHandler<GameEnterPvpRoomCommand, bool>,
        ICommandHandler<GameQuickJoinPvpRoomCommand, bool>,
        ICommandHandler<GameSetPvpSeatStateCommand, bool>,
        ICommandHandler<GameSetPvpReadyStateCommand, bool>,
        ICommandHandler<GameSetPvpTeamModeCommand, bool>,
        ICommandHandler<GameSetPvpMapIndexCommand, bool>
{
    /// <summary>A seat state byte that closes a seat, or for the actor's own seat leaves it, or for another member's kicks them.</summary>
    private const byte CloseOrLeave = 0xFE;

    /// <summary>A seat state byte that opens a closed seat.</summary>
    private const byte OpenSeat = 0xFF;

    public ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameMakePvpRoomCommand command, CancellationToken ct)
    {
        var input = command.Input.Input;
        return RunAsync(execution, command.Dispatch, command.Input.Metadata, "MAKE_PVP_ROOM", PvpRoomCommandRefusal.MakeRoom,
            input is null ? "malformed body" : null,
            (actor, token) => rooms.CreateAsync(new PvpRoomCreation(
                actor, input!.NameType, input.Name, input.Password, input.MapIndex,
                input.Special == 1 ? PvpBattleMode.Practice : PvpBattleMode.Team), token), ct);
    }

    public ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameEnterPvpRoomCommand command, CancellationToken ct) =>
        RunAsync(execution, command.Dispatch, command.Input.Metadata, "ENTER_PVP_ROOM", PvpRoomCommandRefusal.EnterRoom,
            command.Input.WellFormed ? null : "malformed body",
            (actor, token) => rooms.EnterAsync(actor, command.Input.Number, command.Input.Password, token), ct);

    public ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameQuickJoinPvpRoomCommand command, CancellationToken ct) =>
        RunAsync(execution, command.Dispatch, command.Input.Metadata, "QUICK_JOIN_ROOM", PvpRoomCommandRefusal.QuickJoinRoom,
            command.Input.WellFormed ? null : "unsupported quick join body", rooms.QuickJoinAsync, ct,
            success: new QuickJoinPvpRoomAccepted());

    public ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameSetPvpSeatStateCommand command, CancellationToken ct)
    {
        var input = command.Input;
        var team = PvpRoomPayloadCodec.TeamOf(input.State);
        var rejected = !input.WellFormed ? "malformed body"
            : input.Seat >= PvpRoomSnapshot.SeatCount ? "seat out of range"
            : team is null && input.State is not ((byte)PvpObservation.Standard or (byte)PvpObservation.Alternate or CloseOrLeave or OpenSeat)
                ? $"seat state {input.State} not supported" : null;
        return RunAsync(execution, command.Dispatch, input.Metadata, "SET_PVP_SEAT_STATE", PvpRoomCommandRefusal.SeatState, rejected,
            (actor, token) =>
            {
                var (action, side) = SeatAction(actor, input.Seat, input.State, team);
                return rooms.SetSeatAsync(actor, input.Seat, action, side, token);
            }, ct, malformedReason: PvpRoomRefusalCode.NoAuthority);
    }

    public ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameSetPvpReadyStateCommand command, CancellationToken ct) =>
        RunAsync(execution, command.Dispatch, command.Input.Metadata, "SET_PVP_READY_STATE", PvpRoomCommandRefusal.ReadyState,
            command.Input.WellFormed ? null : "malformed body",
            (actor, token) => rooms.SetReadyAsync(actor, command.Input.Ready, token), ct, malformedReason: PvpRoomRefusalCode.NoAuthority);

    public ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameSetPvpTeamModeCommand command, CancellationToken ct) =>
        RunAsync(execution, command.Dispatch, command.Input.Metadata, "SET_PVP_TEAM_MODE", PvpRoomCommandRefusal.TeamMode,
            command.Input.WellFormed ? null : "body is not exactly one byte",
            // The mode byte is the room's battle mode as the client numbers it; the room owner refuses the unsupported ones.
            (actor, token) => rooms.SetModeAsync(actor, (PvpBattleMode)command.Input.Mode, token), ct,
            malformedReason: PvpRoomRefusalCode.NoAuthority);

    public ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameSetPvpMapIndexCommand command, CancellationToken ct) =>
        RunAsync(execution, command.Dispatch, command.Input.Metadata, "SET_PVP_MAP_INDEX", PvpRoomCommandRefusal.MapIndex,
            command.Input.WellFormed ? null : "malformed body",
            (actor, token) => rooms.SetMapAsync(actor, command.Input.Index, token), ct, malformedReason: PvpRoomRefusalCode.NoAuthority);

    /// <summary>
    /// Which seat action a state byte asks for. 0xFE is one byte for three things, told apart by whose seat it
    /// names (upstream <c>HandleSetSeatStateWithinTransition</c>): the actor's own is leaving, another member's is
    /// a kick, an empty one is closing. Read inside the write unit, so the room is the one the change applies to.
    /// </summary>
    private (PvpSeatAction Action, PvpTeam Team) SeatAction(SelectedSessionIdentity actor, byte seat, byte state, PvpTeam? team)
    {
        if (team is { } side) return (PvpSeatAction.ChangeTeam, side);
        if (state == (byte)PvpObservation.Standard) return (PvpSeatAction.Observe, PvpTeam.Individual);
        if (state == (byte)PvpObservation.Alternate) return (PvpSeatAction.ObserveAlternate, PvpTeam.Individual);
        var room = rooms.FindSeated(actor.CharacterId);
        if (state == OpenSeat)
            return room is { Admission: not null } && room.SeatOf(actor.CharacterId) == seat
                ? (PvpSeatAction.Leave, PvpTeam.Individual)
                : (PvpSeatAction.Open, PvpTeam.Individual);
        if (room is null || room.SeatOf(actor.CharacterId) == seat) return (PvpSeatAction.Leave, PvpTeam.Individual);
        return room.Seats[seat] is PvpSeat.Taken ? (PvpSeatAction.Kick, PvpTeam.Individual) : (PvpSeatAction.Close, PvpTeam.Individual);
    }

    private async ValueTask<bool> RunAsync(
        CommandExecutionContext execution,
        NetworkDispatchContext dispatch,
        GameMessageMetadata metadata,
        string name,
        Func<byte, PvpRoomCommandRefusal> refusal,
        string? rejected,
        Func<SelectedSessionIdentity, CancellationToken, ValueTask<PvpRoomOutcome>> act,
        CancellationToken ct,
        byte malformedReason = PvpRoomRefusalCode.NotNow,
        IGameCmdMessage? success = null)
    {
        var connection = dispatch.Connection.ConnectionId;
        if (sessions.FindByConnection(connection) is not { } actor || !sessions.IsInWorld(actor))
        {
            logger.LogInformation("{Command} connection={Connection} refused: not in the world", name, connection);
            await outbox.RegisterCmdAsync(connection, refusal(PvpRoomRefusalCode.NotNow), ct).ConfigureAwait(false);
            return false;
        }
        if (rejected is not null)
        {
            logger.LogInformation("{Command} character={Character} refused: {Reason}", name, actor.CharacterId.Value, rejected);
            await outbox.RegisterCmdAsync(connection, refusal(malformedReason), ct).ConfigureAwait(false);
            return false;
        }

        var outcome = await units.WriteAsync(execution,
            new CommandWorkUnitDeclaration(name, [new SelectedSessionRecipient(actor, dispatch.Connection, metadata)]),
            (_, token) => act(actor, token), ct).ConfigureAwait(false);
        switch (outcome)
        {
            case PvpRoomOutcome.Ignored:
                logger.LogInformation("{Command} character={Character} ignored in practice room", name, actor.CharacterId.Value);
                return true;
            case PvpRoomOutcome.Refused refused:
                logger.LogInformation("{Command} character={Character} refused: {Refusal}", name, actor.CharacterId.Value, refused.Reason);
                await outbox.RegisterCmdAsync(connection, refusal(PvpRoomRefusalCode.Of(refused.Reason)), ct).ConfigureAwait(false);
                return false;
            case PvpRoomOutcome.Changed changed:
                logger.LogInformation("{Command} character={Character} room={Room}: {Kind}", name, actor.CharacterId.Value,
                    (changed.After ?? changed.Before)?.Number, changed.Kind);
                if (success is not null)
                    await outbox.RegisterCmdAsync(connection, success, ct).ConfigureAwait(false);
                await Projector().ProjectAsync(changed, ct).ConfigureAwait(false);
                return true;
            default:
                throw new InvalidOperationException($"Unknown duel room outcome {outcome.GetType().Name}.");
        }
    }

    private PvpRoomProjector Projector() => new(sessions, lobby, currentViews, online, onlineUsers, endpoints, townAreas, parties,
        projectionState, weakness, projections, outbox, logger, deferrals,
        new PvpTotalMatchCandidateProjection(rooms, totalMatchCandidates, outbox, clock));
}

public sealed class MakePvpRoomHandler(ICommandBus commands) : ClientMessageHandler<MakePvpRoomRequest>,
    IProtocolCommandMessageHandler<GameMakePvpRoomCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, MakePvpRoomRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameMakePvpRoomCommand(context, message), ct).ConfigureAwait(false);
}

public sealed class EnterPvpRoomHandler(ICommandBus commands) : ClientMessageHandler<EnterPvpRoomRequest>,
    IProtocolCommandMessageHandler<GameEnterPvpRoomCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, EnterPvpRoomRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameEnterPvpRoomCommand(context, message), ct).ConfigureAwait(false);
}

public sealed class QuickJoinPvpRoomHandler(ICommandBus commands) : ClientMessageHandler<QuickJoinPvpRoomRequest>,
    IProtocolCommandMessageHandler<GameQuickJoinPvpRoomCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, QuickJoinPvpRoomRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameQuickJoinPvpRoomCommand(context, message), ct).ConfigureAwait(false);
}

public sealed class SetPvpSeatStateHandler(ICommandBus commands) : ClientMessageHandler<SetPvpSeatStateRequest>,
    IProtocolCommandMessageHandler<GameSetPvpSeatStateCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, SetPvpSeatStateRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameSetPvpSeatStateCommand(context, message), ct).ConfigureAwait(false);
}

public sealed class SetPvpReadyStateHandler(ICommandBus commands) : ClientMessageHandler<SetPvpReadyStateRequest>,
    IProtocolCommandMessageHandler<GameSetPvpReadyStateCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, SetPvpReadyStateRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameSetPvpReadyStateCommand(context, message), ct).ConfigureAwait(false);
}

public sealed class SetPvpTeamModeHandler(ICommandBus commands) : ClientMessageHandler<SetPvpTeamModeRequest>,
    IProtocolCommandMessageHandler<GameSetPvpTeamModeCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, SetPvpTeamModeRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameSetPvpTeamModeCommand(context, message), ct).ConfigureAwait(false);
}

public sealed class SetPvpMapIndexHandler(ICommandBus commands) : ClientMessageHandler<SetPvpMapIndexRequest>,
    IProtocolCommandMessageHandler<GameSetPvpMapIndexCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, SetPvpMapIndexRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameSetPvpMapIndexCommand(context, message), ct).ConfigureAwait(false);
}
