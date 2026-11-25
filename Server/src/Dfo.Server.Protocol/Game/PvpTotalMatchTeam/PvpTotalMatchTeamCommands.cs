using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.PvpTotalMatchTeam;

internal sealed record GameCheckPvpTotalMatchTeamNameCommand(NetworkDispatchContext Dispatch, CheckPvpTotalMatchTeamNameRequest Input)
    : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

internal sealed record GameSetPvpTotalMatchTeamCommand(NetworkDispatchContext Dispatch, SetPvpTotalMatchTeamRequest Input)
    : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

internal sealed class PvpTotalMatchTeamCommandHandler(
    ISelectedSessionDomainService sessions,
    IPvpTotalMatchTeamDomainService teams,
    IPvpTotalMatchRepresentativeSwitchDomainService representativeSwitch,
    ICommandWorkUnits units,
    IGameCommandOutbox outbox,
    IPvpRoomQueryDomainService rooms,
    IPvpTotalMatchCandidateQueryDomainService candidates,
    IChannelEntryDomainService channels,
    TimeProvider clock) :
    ICommandHandler<GameCheckPvpTotalMatchTeamNameCommand, bool>,
    ICommandHandler<GameSetPvpTotalMatchTeamCommand, bool>
{
    public async ValueTask<bool> HandleAsync(
        CommandExecutionContext execution, GameCheckPvpTotalMatchTeamNameCommand command, CancellationToken ct)
    {
        var connection = command.Dispatch.Connection;
        if (sessions.FindByConnection(connection.ConnectionId) is not { } session || !sessions.IsInWorld(session)) return false;
        var refusal = await units.ReadOnlyAsync(execution, new(nameof(GameCheckPvpTotalMatchTeamNameCommand),
            [new SelectedSessionRecipient(session, connection, command.Input.Metadata)]),
            token => teams.CheckNameAsync(session, command.Input.Name, token), ct).ConfigureAwait(false);
        if (refusal == PvpTotalMatchTeamRefusal.NotAvailable || !sessions.IsInWorld(session)) return false;

        var available = refusal == PvpTotalMatchTeamRefusal.None;
        var reply = new PvpTotalMatchTeamReply(ServerMessageType.GameCheckPvpTotalMatchTeamName,
            available ? [1] : [0, refusal == PvpTotalMatchTeamRefusal.NameTaken ? (byte)3 : (byte)0x9F]);
        await outbox.RegisterCmdAsync(session.ConnectionId, reply, ct).ConfigureAwait(false);
        return available;
    }

    public async ValueTask<bool> HandleAsync(
        CommandExecutionContext execution, GameSetPvpTotalMatchTeamCommand command, CancellationToken ct)
    {
        var connection = command.Dispatch.Connection;
        if (sessions.FindByConnection(connection.ConnectionId) is not { } session || !sessions.IsInWorld(session)) return false;
        var (team, refusal) = await units.WriteAsync(execution, new(nameof(GameSetPvpTotalMatchTeamCommand),
            [new SelectedSessionRecipient(session, connection, command.Input.Metadata)]),
            (_, token) => teams.SaveAsync(session, command.Input.Name, command.Input.Positions, token), ct).ConfigureAwait(false);
        if (refusal == PvpTotalMatchTeamRefusal.NotAvailable || !sessions.IsInWorld(session)) return false;

        if (team is not null)
        {
            await outbox.RegisterNotiAsync(session.ConnectionId, new PvpTotalMatchTeamInfo(team), ct).ConfigureAwait(false);
            if (team.Representative != session.CharacterId)
                representativeSwitch.Expect(connection, session.AccountId, team.Positions[0]);
        }
        var reply = new PvpTotalMatchTeamReply(ServerMessageType.GameSetPvpTotalMatchTeam, team is not null
            ? [1, .. team.Positions]
            : [0, refusal switch
            {
                PvpTotalMatchTeamRefusal.NameTaken => (byte)1,
                PvpTotalMatchTeamRefusal.InvalidComposition => (byte)2,
                _ => (byte)0x9F,
            }]);
        await outbox.RegisterCmdAsync(session.ConnectionId, reply, ct).ConfigureAwait(false);
        // A21 closes the team editor on the successful CMD before it consumes the refreshed candidates.
        if (team?.Representative == session.CharacterId)
            await new PvpTotalMatchCandidateProjection(rooms, candidates, outbox, clock)
                .ProjectAfterTeamSavedAsync(session, channels, ct).ConfigureAwait(false);
        return team is not null;
    }
}

internal sealed class CheckPvpTotalMatchTeamNameHandler(ICommandBus commands)
    : ClientMessageHandler<CheckPvpTotalMatchTeamNameRequest>, IProtocolCommandMessageHandler<GameCheckPvpTotalMatchTeamNameCommand>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext dispatch, CheckPvpTotalMatchTeamNameRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameCheckPvpTotalMatchTeamNameCommand(dispatch, message), ct).ConfigureAwait(false);
}

internal sealed class SetPvpTotalMatchTeamHandler(ICommandBus commands)
    : ClientMessageHandler<SetPvpTotalMatchTeamRequest>, IProtocolCommandMessageHandler<GameSetPvpTotalMatchTeamCommand>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext dispatch, SetPvpTotalMatchTeamRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameSetPvpTotalMatchTeamCommand(dispatch, message), ct).ConfigureAwait(false);
}
