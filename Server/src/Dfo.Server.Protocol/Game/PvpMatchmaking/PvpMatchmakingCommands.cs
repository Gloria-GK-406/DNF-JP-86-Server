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
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.PvpRoom;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.PvpMatchmaking;

public sealed record GameJoinPvpMatchmakingCommand(NetworkDispatchContext Dispatch, JoinPvpMatchmakingRequest Input)
    : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed record GameCancelPvpMatchmakingCommand(NetworkDispatchContext Dispatch, CancelPvpMatchmakingRequest Input)
    : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed record GameConfirmPvpMatchmakingCommand(NetworkDispatchContext Dispatch, ConfirmPvpMatchmakingRequest Input)
    : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed record GamePvpMatchmakingCountCommand(NetworkDispatchContext Dispatch, PvpMatchmakingCountRequest Input)
    : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

internal sealed class GamePvpMatchmakingCommandHandler(
    ISelectedSessionDomainService sessions,
    IPvpMatchmakingDomainService matchmaking,
    IPvpRecordDomainService records,
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
    ILogger<GamePvpMatchmakingCommandHandler> logger)
    : ICommandHandler<GameJoinPvpMatchmakingCommand, bool>,
      ICommandHandler<GameCancelPvpMatchmakingCommand, bool>,
      ICommandHandler<GameConfirmPvpMatchmakingCommand, bool>,
      ICommandHandler<GamePvpMatchmakingCountCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameJoinPvpMatchmakingCommand command, CancellationToken ct)
    {
        var connection = command.Dispatch.Connection;
        var actor = sessions.FindByConnection(connection.ConnectionId);
        PvpMatchmakingQueueResult result;
        if (GameRequestFreshness.IsStale(command.Input.Metadata))
            result = new(PvpMatchmakingDecision.StaleRequest);
        else if (command.Input.Mode is not { } mode)
            result = new(PvpMatchmakingDecision.InvalidRequest);
        else if (actor is null)
            result = new(PvpMatchmakingDecision.NoSelectedCharacter);
        else
            result = await units.WriteAsync(execution,
                new CommandWorkUnitDeclaration("pvp.matchmaking.join", [new(actor, connection, command.Input.Metadata)]),
                (_, token) => matchmaking.EnqueueAsync(actor, mode, command.Input.ExcludedCareers, token, command.Input.Colosseum), ct).ConfigureAwait(false);

        logger.LogInformation(
            "Duel matchmaking join connection={Connection} listener={Listener} character={Character} seq={Sequence} mode={Mode} colosseum={Colosseum} excludedCareers={ExcludedCareerCount} decision={Decision}",
            connection.ConnectionId, actor?.ListenerPort, actor?.CharacterId.Value, command.Input.Metadata.Sequence,
            command.Input.Mode, command.Input.Colosseum, command.Input.ExcludedCareers.Length, result.Decision);
        if (result.Decision == PvpMatchmakingDecision.StaleRequest) return false;

        await outbox.RegisterCmdAsync(connection.ConnectionId,
            new JoinPvpMatchmakingResponse(result.Accepted ? command.Input.Mode : null, command.Input.Colosseum), ct).ConfigureAwait(false);
        if (result.Pair is { After: { } room } pair)
        {
            var views = await units.ReadOnlyAsync(execution, new CommandWorkUnitDeclaration("pvp.matchmaking.records", []),
                async token =>
                {
                    var values = new Dictionary<CharacterId, PvpRecordView>();
                    foreach (var (_, member) in room.Members)
                        values.Add(member.CharacterId, await records.ViewAsync(member.CharacterId, token).ConfigureAwait(false));
                    return values;
                }, ct).ConfigureAwait(false);
            await Projector().ProjectMatchedPairAsync(pair, views, ct).ConfigureAwait(false);
            logger.LogInformation("Duel matchmaking paired room={Room} listener={Listener} characters={Characters}",
                room.Number, room.ListenerPort, string.Join(",", room.Members.Select(entry => entry.Member.CharacterId.Value)));
        }
        return result.Accepted;
    }

    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameCancelPvpMatchmakingCommand command, CancellationToken ct)
    {
        if (GameRequestFreshness.IsStale(command.Input.Metadata)) return false;
        var connection = command.Dispatch.Connection;
        var accepted = command.Input.Mode is { } mode && sessions.FindByConnection(connection.ConnectionId) is { } actor
            && await units.WriteAsync(execution,
                new CommandWorkUnitDeclaration("pvp.matchmaking.cancel", [new(actor, connection, command.Input.Metadata)]),
                (_, token) => matchmaking.CancelAsync(actor, mode, token), ct).ConfigureAwait(false);
        await outbox.RegisterCmdAsync(connection.ConnectionId, new CancelPvpMatchmakingResponse(accepted), ct).ConfigureAwait(false);
        return accepted;
    }

    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameConfirmPvpMatchmakingCommand command, CancellationToken ct)
    {
        var connection = command.Dispatch.Connection;
        if (GameRequestFreshness.IsStale(command.Input.Metadata) || command.Input.RoomNumber is not { } number
            || sessions.FindByConnection(connection.ConnectionId) is not { } actor)
            return false;
        var outcome = await units.WriteAsync(execution,
            new CommandWorkUnitDeclaration("pvp.matchmaking.confirm", [new(actor, connection, command.Input.Metadata)]),
            (_, token) => matchmaking.ConfirmOpponentAsync(actor, number, token), ct).ConfigureAwait(false);
        if (outcome is not PvpRoomOutcome.Changed change) return false;
        await Projector().ProjectAsync(change, ct).ConfigureAwait(false);
        return true;
    }

    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GamePvpMatchmakingCountCommand command, CancellationToken ct)
    {
        var connection = command.Dispatch.Connection;
        if (!command.Input.WellFormed || sessions.FindByConnection(connection.ConnectionId) is not { } actor
            || await matchmaking.WaitingCountsAsync(actor, ct).ConfigureAwait(false) is not { } count)
            return false;
        await outbox.RegisterExplicitAsync(connection.ConnectionId,
            new GameExplicitMessageDelivery([new PvpMatchmakingCountNotice(count)], []), ct).ConfigureAwait(false);
        return true;
    }

    private PvpRoomProjector Projector() => new(sessions, lobby, currentViews, online, onlineUsers, endpoints, townAreas,
        parties, projectionState, weakness, projections, outbox, logger);
}

public sealed class JoinPvpMatchmakingHandler(ICommandBus commands) : ClientMessageHandler<JoinPvpMatchmakingRequest>,
    IProtocolCommandMessageHandler<GameJoinPvpMatchmakingCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, JoinPvpMatchmakingRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameJoinPvpMatchmakingCommand(context, message), ct).ConfigureAwait(false);
}

public sealed class CancelPvpMatchmakingHandler(ICommandBus commands) : ClientMessageHandler<CancelPvpMatchmakingRequest>,
    IProtocolCommandMessageHandler<GameCancelPvpMatchmakingCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, CancelPvpMatchmakingRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameCancelPvpMatchmakingCommand(context, message), ct).ConfigureAwait(false);
}

public sealed class ConfirmPvpMatchmakingHandler(ICommandBus commands) : ClientMessageHandler<ConfirmPvpMatchmakingRequest>,
    IProtocolCommandMessageHandler<GameConfirmPvpMatchmakingCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, ConfirmPvpMatchmakingRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameConfirmPvpMatchmakingCommand(context, message), ct).ConfigureAwait(false);
}

public sealed class PvpMatchmakingCountHandler(ICommandBus commands) : ClientMessageHandler<PvpMatchmakingCountRequest>,
    IProtocolCommandMessageHandler<GamePvpMatchmakingCountCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, PvpMatchmakingCountRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GamePvpMatchmakingCountCommand(context, message), ct).ConfigureAwait(false);
}
