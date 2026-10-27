using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Application.Execution;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Protocol.Game.CharacterDeath;
using Dfo.Server.Protocol.Game.DeleteItem;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.FinishLoading;
using Dfo.Server.Protocol.Game.GroundPickup;
using Dfo.Server.Protocol.Game.IllusionTowerContinue;
using Dfo.Server.Protocol.Game.IllusionTowerInfo;
using Dfo.Server.Protocol.Game.IllusionTowerMap;
using Dfo.Server.Protocol.Game.IllusionTowerRanking;
using Dfo.Server.Protocol.Game.IllusionTowerReward;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using System.Collections.Immutable;
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
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Protocol.Game.Weakness;
using Microsoft.Extensions.Logging;
namespace Dfo.Server.Protocol.Game;
internal sealed partial class GamePartyTransitionCommandHandler :
    ICommandHandler<GameEplpCommand, bool>, ICommandHandler<GameGiveUpGameCommand, bool>,
    ICommandHandler<GameLeavePartyCommand, bool>, ICommandHandler<GameRemovePartyMemberCommand, bool>,
    ICommandHandler<GameChangePartyLeaderCommand, bool>
{
    private readonly ISelectedSessionDomainService sessions;
    private readonly ITownAreaDomainService townAreas;
    private readonly ICommandProjections projections;
    private readonly IPartyProjectionStateDomainService projectionState;
    private readonly ILogger logger;
    private readonly ISelectedCharacterCurrentViewDomainService currentViews;
    private readonly ICharacterStateProjectionDomainService currentState;
    private readonly ICharacterDomainService characters;
    private readonly IOnlineCharacterRuntimeDomainService onlineCharacters;
    private readonly ITownMovementDomainService towns;
    private readonly IMarriageHousingQueryDomainService housing;
    private readonly IBossTowerDomainService bossTowers;
    private readonly IIllusionTowerDomainService illusionTowers;
    private readonly IDungeonParticipationDomainService participation;
    private readonly TimeProvider clock;
    private readonly IGameCommandOutbox outbox;
    private readonly ICommandConnectionClosures closures;
    private readonly IWeaknessDomainService weaknessState;
    private readonly ICommandWorkUnits workUnits;
    private readonly IOnlineUserQueryDomainService onlineUsers;
    private readonly IPartyService parties;
    private const bool projectWeakness = true;

    private readonly IDungeonRunDomainService runs;
    private readonly IDungeonRunProjectionDomainService registry;
    private readonly IDungeonCardPresentationControl cardPresentation;
    private readonly IDungeonEntryIntentDomainService entryIntents;
    private readonly IAltarDungeonDomainService altars;
    private readonly IAltarSchedulerWakeSignal wake;
    private readonly IOutboundOrdering ordering;
    private readonly IGuardianAltarDomainService guardian;
    public GamePartyTransitionCommandHandler(ISelectedSessionDomainService sessions,
        ISelectedCharacterCurrentViewDomainService currentViews, IOnlineCharacterRuntimeDomainService onlineCharacters,
        IDungeonRunDomainService runs, IDungeonRunProjectionDomainService registry, IPartyService parties,
        ITownAreaDomainService townAreas, ICommandProjections projections, IPartyProjectionStateDomainService projectionState,
        ICharacterStateProjectionDomainService currentState, ICharacterDomainService characters,
        ITownMovementDomainService towns, IMarriageHousingQueryDomainService housing,
        IBossTowerDomainService bossTowers, IIllusionTowerDomainService illusionTowers,
        IDungeonParticipationDomainService participation,
        IOnlineUserQueryDomainService onlineUsers, ICommandWorkUnits units, IGameCommandOutbox outbox,
        ICommandConnectionClosures closures, IDungeonCardPresentationControl cardPresentation,
        IDungeonEntryIntentDomainService entryIntents, IAltarDungeonDomainService altars,
        IAltarSchedulerWakeSignal wake, IWeaknessDomainService weakness, TimeProvider clock,
        ILogger<GamePartyTransitionCommandHandler> logger, IOutboundOrdering ordering, IGuardianAltarDomainService guardian)

    {
        this.sessions = sessions; this.townAreas = townAreas; this.projections = projections; this.projectionState = projectionState; this.logger = logger; this.currentViews = currentViews; this.currentState = currentState; this.characters = characters; this.onlineCharacters = onlineCharacters; this.towns = towns; this.housing = housing; this.bossTowers = bossTowers; this.illusionTowers = illusionTowers; this.participation = participation; this.clock = clock; this.outbox = outbox; this.closures = closures; this.weaknessState = weakness; this.workUnits = units; this.onlineUsers = onlineUsers; this.parties = parties;
        this.runs = runs; this.registry = registry; this.cardPresentation = cardPresentation;
        this.entryIntents = entryIntents; this.altars = altars; this.wake = wake; this.ordering = ordering;
        this.guardian = guardian;
    }
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameEplpCommand command, CancellationToken ct)
    { await EplpAsync(execution, command.Dispatch, command.Input, ct).ConfigureAwait(false); return true; }
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameGiveUpGameCommand command, CancellationToken ct)
    { await GiveUpAsync(execution, command.Dispatch, command.Input, ct).ConfigureAwait(false); return true; }
    public ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameLeavePartyCommand command, CancellationToken token) =>
        HandleMutationAsync(execution, command.Dispatch, command.Input.Metadata, PartyChange.Leave, null, token);
    public ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameRemovePartyMemberCommand command, CancellationToken token) =>
        HandleMutationAsync(execution, command.Dispatch, command.Input.Metadata, PartyChange.Remove, command.Input.TargetSlot, token);
    public ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameChangePartyLeaderCommand command, CancellationToken token) =>
        HandleMutationAsync(execution, command.Dispatch, command.Input.Metadata, PartyChange.Transfer, command.Input.TargetSlot, token);

    private async ValueTask EplpAsync(
        CommandExecutionContext execution, NetworkDispatchContext context,
        EplpCommandRequest message,
        CancellationToken cancellationToken)
    {
        if (GameRequestFreshness.IsStale(message.Metadata)) return;
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        switch (session is null ? null : DungeonLocator.Locate(session, participation, bossTowers, illusionTowers))
        {
            case DungeonPlace.InBossTower boss:
                await SelectBossReturnAsync(execution, boss, message.State, message.Option, cancellationToken).ConfigureAwait(false);
                return;
            case DungeonPlace.InIllusionTower illusion:
                await SelectIllusionReturnAsync(execution, illusion, message.State, message.Option, cancellationToken).ConfigureAwait(false);
                return;
            default:
                await EplpOrdinaryAsync(execution, context, message, session, cancellationToken).ConfigureAwait(false);
                return;
        }
    }

    private async ValueTask EplpOrdinaryAsync(
        CommandExecutionContext execution, NetworkDispatchContext context,
        EplpCommandRequest message, SelectedSessionIdentity? session,
        CancellationToken cancellationToken)
    {
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
        var isGuardian = currentRun?.Layout.Guardian is not null;
        var leaving = message.State == EplpCommandHandler.LeaveState && (isAltar || isGuardian || message.Option <= 2);
        if (message.State == EplpCommandHandler.LeaveState && currentRun is null && message.Option != 2)
        {
            logger.LogDebug("Eplp ignored after exit {ConnectionId}: state={State} option={Option} sequence={Sequence} ordinal={Ordinal}",
                context.Connection.ConnectionId, message.State, message.Option, message.Metadata.Sequence, message.Metadata.InboundOrdinal);
            return;
        }
        var roster = currentRun is not null
            ? parties.FindParty(new PartyCharacterSnapshot(
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
                || (!(isAltar || isGuardian) && returning.Any(x => entryIntents.Find(x.Session) is not null)))
                return;
            if (roster is not null && returning.Count != roster.Members.Length)
                return;
            if (currentRun is null
                && (townAreas.Find(session.CharacterId) is not { } member || !session.Owns(member)))
                return;
        }

        var runEnded = false;
        HashSet<CharacterId> endedCharacters = [];
        if (leaving)
        {
            if (isGuardian)
                _ = await workUnits.WriteAsync(execution, new("guardian-altar.exit", [CaptureParticipant(session)]),
                    async (_, token) => new GuardianAltar.GuardianAltarMutation(Action: await guardian.AbandonAsync(session, token).ConfigureAwait(false)), cancellationToken).ConfigureAwait(false);
            if (isAltar)
                await SettleAltarExitAsync(execution, session, currentRun!.RunId, false, cancellationToken).ConfigureAwait(false);

            if (currentRun is not null)
            {
                foreach (var member in returning)
                    cardPresentation.Cancel(member.Session.CharacterId, currentRun.RunId);
                var ended = await EndOrdinaryRunAsync(currentRun.RunId, cancellationToken).ConfigureAwait(false);
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
                if (message.Option == 0 && !(isAltar || isGuardian)
                    && currentRun.Layout is { Purpose.Kind: DungeonRunPurposeKind.Ordinary,
                        EntryKind: DungeonEntryKind.Normal, Mirror: null, Sao: null, Despair: null,
                        Tournament: null })
                    foreach (var member in returning)
                        entryIntents.RestoreIfAbsent(member.Session,
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
        using (ordering.Begin("XO-2 peer decisions before the leader's ACK", OutboundDelivery.BestEffort))
        {
            foreach (var member in returning.Where(value => value.Session != session))
            {
                if (!sessions.IsInWorld(member.Session))
                    continue;
                try
                {
                    await outbox.RegisterExplicitAsync(
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
            await outbox.RegisterExplicitAsync(
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
    private async ValueTask GiveUpAsync(
        CommandExecutionContext execution, NetworkDispatchContext context,
        GiveUpGameRequest message,
        CancellationToken cancellationToken)
    {
        if (GameRequestFreshness.IsStale(message.Metadata))
            return;
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null)
        {
            logger.LogWarning(
                "Give up game {ConnectionId}: no published session, ignoring",
                context.Connection.ConnectionId);
            return;
        }

        var (candidates, place) = LocatePartyOf(session, null);
        switch (place)
        {
            case DungeonPartyPlace.PartyInBossTower boss:
                _ = await CoordinateBossTowerPartyAsync(execution, PartyChange.Leave, session, candidates, boss, true, new GiveUpGameResponse(), cancellationToken).ConfigureAwait(false);
                return;
            case DungeonPartyPlace.PartyInIllusionTower illusion:
                _ = await CoordinateIllusionTowerPartyAsync(execution, PartyChange.Leave, session, candidates, illusion, true, new GiveUpGameResponse(), cancellationToken).ConfigureAwait(false);
                return;
            default:
                await GiveUpOrdinaryAsync(execution, context, session, cancellationToken).ConfigureAwait(false);
                return;
        }
    }

    private async ValueTask GiveUpOrdinaryAsync(
        CommandExecutionContext execution, NetworkDispatchContext context,
        SelectedSessionIdentity session, CancellationToken cancellationToken)
    {
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
        if (currentRun?.Layout.Guardian is not null)
            _ = await workUnits.WriteAsync(execution, new("guardian-altar.give-up", [CaptureParticipant(session)]),
                async (_, token) => new GuardianAltar.GuardianAltarMutation(Action: await guardian.AbandonAsync(session, token).ConfigureAwait(false)), cancellationToken).ConfigureAwait(false);
        if (currentRun?.Layout.Altar is not null)
            await SettleAltarExitAsync(execution, session, currentRun.RunId, true, cancellationToken).ConfigureAwait(false);
        if (currentRun is not null)
        {
            cardPresentation.Cancel(session.CharacterId, currentRun.RunId);
            // The run's members are its party's, so giving up leaves the party first; the party
            // mutation then ends this character's part of the run. Outside a real party the leave
            // is refused and the run, which is theirs alone, ends below.
            var partyResult = await MutatePartyAsync(PartyChange.Leave, session, null, cancellationToken).ConfigureAwait(false);
            partyExecution = new PartyCommandExecution(
                partyResult,
                PartyNotificationRecipientCapture.Capture(
                    partyResult.Notifications, sessions, townAreas));
            ended = partyResult.Succeeded && runs.Find(session.CharacterId)?.RunId != currentRun.RunId;
            if (!ended)
                ended = await runs.EndAsync(session.CharacterId, currentRun.RunId).ConfigureAwait(false);
            if (ended)
                registry.ParticipantDeparted(currentRun.RunId, session.CharacterId);
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

        await outbox.RegisterExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery([new GiveUpGameResponse()], []),
            cancellationToken).ConfigureAwait(false);
        await ReturnTownAsync(
            [new TownAreaEntry(session, result, position, ProjectWeaknessBeforeTown: true)],
            cancellationToken).ConfigureAwait(false);
    }
}
