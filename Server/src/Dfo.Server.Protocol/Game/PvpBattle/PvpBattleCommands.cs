using System.Collections.Immutable;
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
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.PvpRoom;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.PvpBattle;

public sealed record GameCompleteLoadPvpCommand(NetworkDispatchContext Dispatch, CompleteLoadPvpRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed record GameConnectP2pPvpCommand(NetworkDispatchContext Dispatch, ConnectP2pPvpRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed record GamePvpHeartbeatCommand(NetworkDispatchContext Dispatch, PvpHeartbeatRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed record GameDiePvpCharacterCommand(NetworkDispatchContext Dispatch, DiePvpCharacterRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed record GamePvpTimeOutCommand(NetworkDispatchContext Dispatch, PvpTimeOutRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed record GamePvpRankResponseCommand(NetworkDispatchContext Dispatch, PvpRankResponseRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed record GameEndPvpResultCommand(NetworkDispatchContext Dispatch, EndPvpResultRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

/// <summary>What one battle report did: the room's change, and the settlement it led to in the same unit.</summary>
internal sealed record PvpBattleStep(PvpRoomOutcome Outcome, PvpSettlementOutcome? Settled);

/// <summary>
/// The duel battle reports: one room report in one write unit each, the settlement it makes due settled in the same
/// unit, projected once the unit has returned (design-r1 §4 b).
/// </summary>
/// <remarks>
/// No report has a command reply. A report the room owner ignores (a phase it does not belong to, a late or second
/// one) and a malformed one are logged and change nothing; a malformed one never reaches the room owner.
/// <c>COMPLETE_LOAD_PVP</c> and <c>CONNECT_P2P_PVP</c> never reach it at all (spec SA-1).
/// </remarks>
internal sealed class GamePvpBattleCommandHandler(
    ISelectedSessionDomainService sessions,
    IPvpRoomDomainService rooms,
    IPvpSettlementDomainService settlements,
    IPvpRecordDomainService records,
    IPvpResultScreenDeferralDomainService deferrals,
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
    ILogger<GamePvpBattleCommandHandler> logger)
    : ICommandHandler<GameCompleteLoadPvpCommand, bool>,
        ICommandHandler<GameConnectP2pPvpCommand, bool>,
        ICommandHandler<GamePvpHeartbeatCommand, bool>,
        ICommandHandler<GameDiePvpCharacterCommand, bool>,
        ICommandHandler<GamePvpTimeOutCommand, bool>,
        ICommandHandler<GamePvpRankResponseCommand, bool>,
        ICommandHandler<GameEndPvpResultCommand, bool>
{
    /// <summary>
    /// Only logged: the fight is on from the start (spec SA-1, 70Reference <c>start_pvp</c>), and an A21 client in an
    /// ordinary room was not seen sending it.
    /// </summary>
    public ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameCompleteLoadPvpCommand command, CancellationToken ct)
    {
        var connection = command.Dispatch.Connection.ConnectionId;
        logger.LogInformation("COMPLETE_LOAD_PVP connection={Connection} character={Character} length={Length}: logged only",
            connection, sessions.FindByConnection(connection)?.CharacterId.Value, command.Input.Length);
        return ValueTask.FromResult(false);
    }

    /// <summary>Only logged, as <c>COMPLETE_LOAD_PVP</c> is.</summary>
    public ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameConnectP2pPvpCommand command, CancellationToken ct)
    {
        var input = command.Input;
        var connection = command.Dispatch.Connection.ConnectionId;
        logger.LogInformation("CONNECT_P2P_PVP connection={Connection} character={Character} peers={Peers}: logged only",
            connection, sessions.FindByConnection(connection)?.CharacterId.Value,
            input.WellFormed ? string.Join(",", input.Peers.Select(peer => $"{peer.Seat}:{peer.Status}")) : $"malformed {input.Length}-byte body");
        return ValueTask.FromResult(false);
    }

    public ValueTask<bool> HandleAsync(CommandExecutionContext execution, GamePvpHeartbeatCommand command, CancellationToken ct) =>
        RunAsync(execution, command.Dispatch, command.Input.Metadata, "PVP_HEART_BEAT",
            command.Input.Length == 0 ? null : $"body is {command.Input.Length} bytes, not empty",
            (actor, token) => rooms.HeartbeatAsync(actor, token), ct, quiet: true);

    /// <summary>
    /// The sender must be the victim it names; the killer it names counts only as the fighter of this battle holding
    /// that user id, and 0 or anyone else is nobody (the room owner then decides whether the kill counts at all).
    /// </summary>
    public ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameDiePvpCharacterCommand command, CancellationToken ct)
    {
        var input = command.Input;
        var actor = sessions.FindByConnection(command.Dispatch.Connection.ConnectionId);
        var rejected = !input.WellFormed ? $"body is {input.Length} bytes, not {PvpBattlePayloadCodec.DeathLength}"
            : actor is not null && input.Victim != actor.WireUserId ? $"victim {input.Victim} is not the sender {actor.WireUserId}"
            : null;
        return RunAsync(execution, command.Dispatch, input.Metadata, "DIE_PVP_CHARACTER", rejected, (self, token) =>
        {
            logger.LogInformation("DIE_PVP_CHARACTER character={Character} victim={Victim} killer={Killer} flag={Flag}",
                self.CharacterId.Value, input.Victim, input.Killer, input.Flag);
            CharacterId? killer = null;
            if (input.Killer != 0 && rooms.FindSeated(self.CharacterId)?.Match is { } match)
                killer = match.Fighters.FirstOrDefault(fighter => fighter.Session.WireUserId == input.Killer)?.CharacterId;
            return rooms.ReportDeathAsync(self, killer, token);
        }, ct);
    }

    public ValueTask<bool> HandleAsync(CommandExecutionContext execution, GamePvpTimeOutCommand command, CancellationToken ct) =>
        RunAsync(execution, command.Dispatch, command.Input.Metadata, "PVP_TIME_OUT",
            command.Input.WellFormed ? null : $"body is {command.Input.Values.Length} bytes, not {PvpBattlePayloadCodec.TimeOutLength}",
            (actor, token) => rooms.ReportTimeoutAsync(actor, command.Input.Values, token), ct);

    /// <summary>The ranking block is the client's own; only its arrival counts (design-r1 §5).</summary>
    public ValueTask<bool> HandleAsync(CommandExecutionContext execution, GamePvpRankResponseCommand command, CancellationToken ct) =>
        RunAsync(execution, command.Dispatch, command.Input.Metadata, "RES_PVP_RANK", null,
            (actor, token) => rooms.AcknowledgeRankAsync(actor, token), ct);

    public ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameEndPvpResultCommand command, CancellationToken ct) =>
        RunAsync(execution, command.Dispatch, command.Input.Metadata, "END_PVP_RESULT",
            command.Input.Length == 0 ? null : $"body is {command.Input.Length} bytes, not empty",
            (actor, token) => rooms.AcknowledgeResultAsync(actor, token), ct);

    private async ValueTask<bool> RunAsync(
        CommandExecutionContext execution,
        NetworkDispatchContext dispatch,
        GameMessageMetadata metadata,
        string name,
        string? rejected,
        Func<SelectedSessionIdentity, CancellationToken, ValueTask<PvpRoomOutcome>> act,
        CancellationToken ct,
        bool quiet = false)
    {
        var connection = dispatch.Connection.ConnectionId;
        if (sessions.FindByConnection(connection) is not { } actor || !sessions.IsInWorld(actor))
        {
            logger.LogInformation("{Command} connection={Connection} ignored: not in the world", name, connection);
            return false;
        }
        if (rejected is not null)
        {
            logger.LogWarning("{Command} character={Character} refused: {Reason}", name, actor.CharacterId.Value, rejected);
            return false;
        }

        var step = await units.WriteAsync(execution,
            new CommandWorkUnitDeclaration(name, [new SelectedSessionRecipient(actor, dispatch.Connection, metadata)]),
            async (_, token) =>
            {
                var outcome = await act(actor, token).ConfigureAwait(false);
                var settled = outcome is PvpRoomOutcome.Changed { SettlementDue: { } match }
                    ? await settlements.SettleAsync(match, token).ConfigureAwait(false)
                    : null;
                return new PvpBattleStep(outcome, settled);
            }, ct).ConfigureAwait(false);
        switch (step.Outcome)
        {
            case PvpRoomOutcome.Refused refused:
                // The room owner has logged why; the report changes nothing and has no reply.
                logger.LogDebug("{Command} character={Character} ignored: {Refusal}", name, actor.CharacterId.Value, refused.Reason);
                return false;
            case PvpRoomOutcome.Changed changed:
                if (!quiet || changed.After?.Phase != changed.Before?.Phase)
                    logger.LogInformation("{Command} character={Character} room={Room}: {Before} -> {After}", name,
                        actor.CharacterId.Value, (changed.After ?? changed.Before)?.Number, changed.Before?.Phase, changed.After?.Phase);
                var projector = Projector();
                // Its own close of the result screen settles what the room still owed it, before anything else it is shown.
                if (changed.Kind == PvpRoomChangeKind.ResultAcknowledged)
                    await projector.Rooms.ReleaseAsync(actor, changed.After, ct).ConfigureAwait(false);
                await projector.ChangedAsync(changed, ct).ConfigureAwait(false);
                if (step.Settled is PvpSettlementOutcome.Settled written)
                    await projector.SettledAsync(written, await PvpBattleRecords.ReadAsync(units, records, execution, written, ct)
                        .ConfigureAwait(false), ct).ConfigureAwait(false);
                return true;
            default:
                throw new InvalidOperationException($"Unknown duel room outcome {step.Outcome.GetType().Name}.");
        }
    }

    private PvpBattleProjector Projector() => new(new PvpRoomProjector(sessions, lobby, currentViews, online, onlineUsers, endpoints,
        townAreas, parties, projectionState, weakness, projections, outbox, logger, deferrals), logger);
}

/// <summary>Reads the records a settlement's <c>0x0030</c> frames show, after the settlement committed.</summary>
internal static class PvpBattleRecords
{
    public static async ValueTask<IReadOnlyDictionary<CharacterId, PvpRecordView>> ReadAsync(ICommandWorkUnits units,
        IPvpRecordDomainService records, CommandExecutionContext execution, PvpSettlementOutcome? settled, CancellationToken ct)
    {
        var characters = PvpBattleProjector.RecordsFor(settled);
        if (characters.Count == 0) return ImmutableDictionary<CharacterId, PvpRecordView>.Empty;
        return await units.ReadOnlyAsync<IReadOnlyDictionary<CharacterId, PvpRecordView>>(execution,
            new CommandWorkUnitDeclaration("pvp.records", []), async token =>
            {
                var views = new Dictionary<CharacterId, PvpRecordView>();
                foreach (var character in characters)
                    views[character] = await records.ViewAsync(character, token).ConfigureAwait(false);
                return views;
            }, ct).ConfigureAwait(false);
    }
}

public sealed class CompleteLoadPvpHandler(ICommandBus commands) : ClientMessageHandler<CompleteLoadPvpRequest>,
    IProtocolCommandMessageHandler<GameCompleteLoadPvpCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, CompleteLoadPvpRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameCompleteLoadPvpCommand(context, message), ct).ConfigureAwait(false);
}

public sealed class ConnectP2pPvpHandler(ICommandBus commands) : ClientMessageHandler<ConnectP2pPvpRequest>,
    IProtocolCommandMessageHandler<GameConnectP2pPvpCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, ConnectP2pPvpRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameConnectP2pPvpCommand(context, message), ct).ConfigureAwait(false);
}

public sealed class PvpHeartbeatHandler(ICommandBus commands) : ClientMessageHandler<PvpHeartbeatRequest>,
    IProtocolCommandMessageHandler<GamePvpHeartbeatCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, PvpHeartbeatRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GamePvpHeartbeatCommand(context, message), ct).ConfigureAwait(false);
}

public sealed class DiePvpCharacterHandler(ICommandBus commands) : ClientMessageHandler<DiePvpCharacterRequest>,
    IProtocolCommandMessageHandler<GameDiePvpCharacterCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, DiePvpCharacterRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameDiePvpCharacterCommand(context, message), ct).ConfigureAwait(false);
}

public sealed class PvpTimeOutHandler(ICommandBus commands) : ClientMessageHandler<PvpTimeOutRequest>,
    IProtocolCommandMessageHandler<GamePvpTimeOutCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, PvpTimeOutRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GamePvpTimeOutCommand(context, message), ct).ConfigureAwait(false);
}

public sealed class PvpRankResponseHandler(ICommandBus commands) : ClientMessageHandler<PvpRankResponseRequest>,
    IProtocolCommandMessageHandler<GamePvpRankResponseCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, PvpRankResponseRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GamePvpRankResponseCommand(context, message), ct).ConfigureAwait(false);
}

public sealed class EndPvpResultHandler(ICommandBus commands) : ClientMessageHandler<EndPvpResultRequest>,
    IProtocolCommandMessageHandler<GameEndPvpResultCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, EndPvpResultRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameEndPvpResultCommand(context, message), ct).ConfigureAwait(false);
}
