using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Altar;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Protocol.Game.Weakness;
using Microsoft.Extensions.Logging;
namespace Dfo.Server.Protocol.Game.DungeonClear;
public sealed record GameEplpCommand(NetworkDispatchContext Dispatch, EplpCommandRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed record GameGiveUpGameCommand(NetworkDispatchContext Dispatch, GiveUpGameRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
internal partial class GameDungeonExitCommandHandler : PartyMutationProtocolCommandHandler,
    ICommandHandler<GameEplpCommand, bool>, ICommandHandler<GameGiveUpGameCommand, bool>
{
    private readonly IDungeonRunDomainService runs;
    private readonly IDungeonRunProjectionDomainService registry;
    private readonly IDungeonCardPresentationControl cardPresentation;
    private readonly IDungeonEntryIntentDomainService entryIntents;
    private readonly IAltarDungeonDomainService altars;
    private readonly IAltarSchedulerWakeSignal wake;
    private readonly IOutboundOrdering? ordering;
    public GameDungeonExitCommandHandler(ISelectedSessionDomainService sessions,
        ISelectedCharacterCurrentViewDomainService currentViews, IOnlineCharacterRuntimeDomainService onlineCharacters,
        IDungeonRunDomainService runs, IDungeonRunProjectionDomainService registry, IPartyService parties,
        ITownAreaDomainService townAreas, ICommandProjections projections, IPartyProjectionStateDomainService projectionState,
        ICharacterStateProjectionDomainService currentState, ICharacterDomainService characters,
        ITownMovementDomainService towns, IMarriageHousingQueryDomainService housing,
        IBossTowerDomainService bossTowers, IIllusionTowerDomainService illusionTowers,
        IOnlineUserQueryDomainService onlineUsers, ICommandWorkUnits units, IGameCommandOutbox outbox,
        ICommandConnectionClosures closures, IDungeonCardPresentationControl cardPresentation,
        IDungeonEntryIntentDomainService entryIntents, IAltarDungeonDomainService altars,
        IAltarSchedulerWakeSignal wake, IWeaknessDomainService weakness, TimeProvider clock,
        ILogger<GameDungeonExitCommandHandler> logger, IOutboundOrdering? ordering = null)
        : base(sessions, parties, townAreas, projections, projectionState is null ? null! : projectionState.StreamOf, ordering, logger,
            currentViews, currentState, characters, onlineCharacters, towns, housing, bossTowers, illusionTowers, clock,
            outbox.RegisterExplicitAsync,
            session => onlineUsers.TryGetByConnection(session.ConnectionId, out var user) && user is not null
                && user.AccountId == session.AccountId && user.CurrentCharacterId == session.CharacterId
                ? closures.RequestCloseAsync(user.Connection, CancellationToken.None) : ValueTask.CompletedTask,
            (session, token) => sessions.IsInWorld(session)
                ? outbox.RegisterNotiAsync(session.ConnectionId, new WeaknessProjectionNotification(weakness.For(session.CharacterId).Value), token)
                : ValueTask.CompletedTask,
            units, onlineUsers, runs, registry)
    {
        this.runs = runs; this.registry = registry; this.cardPresentation = cardPresentation;
        this.entryIntents = entryIntents; this.altars = altars; this.wake = wake; this.ordering = ordering;
    }
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameEplpCommand command, CancellationToken ct)
    { await EplpAsync(execution, command.Dispatch, command.Input, ct).ConfigureAwait(false); return true; }
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameGiveUpGameCommand command, CancellationToken ct)
    { await GiveUpAsync(execution, command.Dispatch, command.Input, ct).ConfigureAwait(false); return true; }
    protected async ValueTask EplpAsync(
        CommandExecutionContext execution, NetworkDispatchContext context,
        EplpCommandRequest message,
        CancellationToken cancellationToken)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        if (session is not null && await SelectBossReturnAsync(execution, session, message.Metadata, message.State, message.Option, cancellationToken).ConfigureAwait(false)) return;
        if (session is not null && await SelectIllusionReturnAsync(execution, session, message.Metadata, message.State, message.Option, cancellationToken).ConfigureAwait(false)) return;
        // Capture the source before loading any asynchronous character view.
        var currentRun = session is null ? null : runs.Find(session.CharacterId);
        // Where they are standing is asked of the service that owns it. It is recorded and dropped
        // with the published session, so this conjunct admits exactly the connections the two
        // before it do.
        var result = session is null ? null
            : await currentViews.TryLoadAsync(session, cancellationToken).ConfigureAwait(false);
        if (session is null || result is null
            || onlineCharacters.PositionOf(session) is not { } position)
        {
            logger.LogWarning(
                "Eplp command {ConnectionId}: no published session, ignoring",
                context.Connection.ConnectionId);
            return;
        }

        var isAltar = currentRun?.Layout.Altar is not null;
        var leaving = message.State == EplpCommandHandler.LeaveState && (isAltar || message.Option <= 2);
        if (message.State == EplpCommandHandler.LeaveState && currentRun is null && message.Option != 2)
        {
            logger.LogDebug("Eplp ignored after exit {ConnectionId}: state={State} option={Option} sequence={Sequence} ordinal={Ordinal}",
                context.Connection.ConnectionId, message.State, message.Option, message.Metadata.Sequence, message.Metadata.InboundOrdinal);
            return;
        }
        var roster = currentRun is not null && PartyService is not null
            ? PartyService.FindParty(new PartyCharacterSnapshot(
                session.AccountId, session.CharacterId, session.WireUserId, null))
            : null;
        var leaderWireUserId = roster?.LeaderWireUserId ?? session.WireUserId;
        var currentRunId = currentRun?.RunId;
        var participants = roster is not null
            ? roster.Members.OrderBy(value => value.Slot)
                .Select(value => new DungeonRunParticipant(
                    value.Character.AccountId, value.CharacterId, value.Slot,
                    value.WireUserId == leaderWireUserId))
                .Where(value => runs.Find(value.CharacterId)?.RunId == currentRunId)
                .ToArray()
            : currentRun is { Participants.Count: > 0 }
                ? currentRun.Participants.OrderBy(value => value.Slot).ToArray()
                : [new DungeonRunParticipant(session.AccountId, session.CharacterId, 0, true)];
        var leader = participants.SingleOrDefault(value => value.IsLeader);
        if (leader is null || leader.CharacterId != session.CharacterId)
        {
            logger.LogWarning(
                "Eplp command {ConnectionId}: non-leader character={CharacterId} ignored",
                context.Connection.ConnectionId, session.CharacterId.Value);
            return;
        }

        var returning = new List<ReturnMember>();
        foreach (var participant in participants)
        {
            var memberSession = sessions.TryGetInWorld(participant.CharacterId, out var inWorld) ? inWorld : null;
            var memberRuntime = memberSession is null ? null
                : await currentViews.TryLoadAsync(memberSession, cancellationToken).ConfigureAwait(false);
            if (memberSession is null
                || memberSession.AccountId != participant.AccountId
                || memberRuntime is null
                || onlineCharacters.PositionOf(memberSession) is not { } memberPosition
                || (currentRun is not null
                    && runs.Find(participant.CharacterId)?.RunId != currentRun.RunId))
            {
                continue;
            }
            returning.Add(new ReturnMember(memberSession, memberRuntime, memberPosition));
        }
        if (!returning.Any(value => value.Session == session))
            return;

        if (message.State == EplpCommandHandler.LeaveState)
        {
            if (!sessions.IsInWorld(session)
                || returning.Any(x => !sessions.IsInWorld(x.Session))
                || (!isAltar && returning.Any(x => entryIntents?.Find(x.Session) is not null)))
                return;
            if (roster is not null && PartyService is not null && returning.Count != roster.Members.Length)
                return;
            if (currentRun is null && townAreas is not null
                && (townAreas.Find(session.CharacterId) is not { } member || !session.Owns(member)))
                return;
        }

        var runEnded = false;
        HashSet<CharacterId> endedCharacters = [];
        if (leaving)
        {
            if (isAltar)
                await SettleAltarExitAsync(execution, session, currentRun!.RunId, false, cancellationToken).ConfigureAwait(false);

            if (currentRun is not null)
            {
                foreach (var member in returning)
                    cardPresentation?.Cancel(member.Session.CharacterId, currentRun.RunId);
                var ended = await EndOrdinaryRunAsync(returning.Select(x => x.Session).ToArray(), currentRun.RunId, cancellationToken).ConfigureAwait(false);
                foreach (var character in ended)
                {
                    registry.ParticipantDeparted(currentRun.RunId, character);
                    endedCharacters.Add(character);
                }
                runEnded = endedCharacters.Count > 0;
            }
            if (currentRun is not null && !runEnded)
                return;
            if (currentRun is not null)
            {
                returning.RemoveAll(value => !endedCharacters.Contains(value.Session.CharacterId));
                if (!returning.Any(value => value.Session == session))
                    return;
                // Native retry follows the exit with 000F(dungeonId). Retain the ordinary
                // maze partition rather than treating that nonzero target as a story request.
                if (message.Option == 0 && !isAltar
                    && currentRun.Layout is { Purpose.Kind: DungeonRunPurposeKind.Ordinary,
                        EntryKind: DungeonEntryKind.Normal, Mirror: null, Sao: null, Despair: null,
                        Tournament: null })
                    foreach (var member in returning)
                        entryIntents?.RestoreIfAbsent(member.Session,
                            DungeonEntryIntent.OrdinaryRechallenge(currentRun.Layout.DungeonId));
            }
        }

        logger.LogInformation(
            "Eplp command {ConnectionId} character={CharacterId}: state={State} option={Option}; "
                + "leaving={Leaving} runEnded={RunEnded} sourceRun={RunId} sequence={Sequence} ordinal={Ordinal}",
            context.Connection.ConnectionId,
            session.CharacterId.Value,
            message.State,
            message.Option,
            leaving,
            runEnded, currentRun?.RunId, message.Metadata.Sequence, message.Metadata.InboundOrdinal);

        // Peers must see the decision before the leader's ACK can open another selection (XO-2:
        // delivered before it, each BestEffort).
        using (ordering?.Begin("XO-2 peer decisions before the leader's ACK", OutboundDelivery.BestEffort))
        {
            foreach (var member in returning.Where(value => value.Session != session))
            {
                if (!sessions.IsInWorld(member.Session))
                    continue;
                try
                {
                    await register(
                        member.Session.ConnectionId,
                        new GameExplicitMessageDelivery(
                            [new EplpCommandResponse(message.State, message.Option)], []),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception error) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogWarning(error,
                        "Eplp command {ConnectionId}: peer decision delivery failed for character={CharacterId}",
                        context.Connection.ConnectionId, member.Session.CharacterId.Value);
                }
            }
            if (!sessions.IsInWorld(session))
                return;
            await register(
                context.Connection.ConnectionId,
                new GameExplicitMessageDelivery(
                    [new EplpCommandResponse(message.State, message.Option)], []),
                cancellationToken).ConfigureAwait(false);
        }
        if (leaving)
        {
            await ReturnTownAsync(
                returning.Where(value => sessions.IsInWorld(value.Session))
                    .Select(value => new TownAreaEntry(
                        value.Session, value.Runtime, value.Position))
                    .ToArray(),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed record ReturnMember(
        SelectedSessionIdentity Session,
        SelectCharacterResult Runtime,
        CharacterPositionState Position);
    protected async ValueTask GiveUpAsync(
        CommandExecutionContext execution, NetworkDispatchContext context,
        GiveUpGameRequest message,
        CancellationToken cancellationToken)
    {
        if (TowerProtocol.IsStale(message.Metadata))
            return;
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null)
        {
            logger.LogWarning(
                "Give up game {ConnectionId}: no published session, ignoring",
                context.Connection.ConnectionId);
            return;
        }

        if (await TryGiveUpTowersAsync(execution, session, cancellationToken).ConfigureAwait(false)) return;

        var result = await currentViews.TryLoadAsync(session, cancellationToken)
            .ConfigureAwait(false);
        if (result is null || onlineCharacters.PositionOf(session) is not { } position)
        {
            logger.LogWarning(
                "Give up game {ConnectionId}: no published session, ignoring",
                context.Connection.ConnectionId);
            return;
        }

        var ended = false;
        PartyCommandExecution? partyExecution = null;
        if (!sessions.IsInWorld(session))
            return;
        var currentRun = runs.Find(session.CharacterId);
        if (currentRun?.Layout.Altar is not null)
            await SettleAltarExitAsync(execution, session, currentRun.RunId, true, cancellationToken).ConfigureAwait(false);
        if (currentRun is not null)
            cardPresentation?.Cancel(session.CharacterId, currentRun.RunId);
        ended = currentRun is not null
            && await runs.EndAsync(session.CharacterId, currentRun.RunId)
                .ConfigureAwait(false);
        if (ended && currentRun is not null)
            registry.ParticipantDeparted(currentRun.RunId, session.CharacterId);
        if (ended && townAreas is not null && PartyService is not null)
        {
            var partyResult = await MutatePartyAsync(PartyChange.Leave, session, null, cancellationToken).ConfigureAwait(false);
            partyExecution = new PartyCommandExecution(
                partyResult,
                PartyNotificationRecipientCapture.Capture(
                    partyResult.Notifications, sessions, townAreas));
        }

        if (partyExecution is { Result.Succeeded: true })
        {
            await ProjectPartyAsync(
                partyExecution.Result.Notifications,
                partyExecution.Recipients,
                cancellationToken).ConfigureAwait(false);
        }
        logger.LogInformation(
            "Give up game {ConnectionId} character={CharacterId}: run ended={Ended}",
            context.Connection.ConnectionId,
            session.CharacterId.Value,
            ended);

        await register(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery([new GiveUpGameResponse()], []),
            cancellationToken).ConfigureAwait(false);
        await ReturnTownAsync(
            [new TownAreaEntry(session, result, position, ProjectWeaknessBeforeTown: true)],
            cancellationToken).ConfigureAwait(false);
    }
}
