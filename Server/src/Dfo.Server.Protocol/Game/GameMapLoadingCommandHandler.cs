using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Application.Execution;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Parties;
using Dfo.Server.Protocol.Game.CharacterDeath;
using Dfo.Server.Protocol.Game.DeleteItem;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.GroundPickup;
using Dfo.Server.Protocol.Game.IllusionTowerContinue;
using Dfo.Server.Protocol.Game.IllusionTowerInfo;
using Dfo.Server.Protocol.Game.IllusionTowerRanking;
using Dfo.Server.Protocol.Game.IllusionTowerReward;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Protocol.Game.Weakness;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.FinishLoading;
using Dfo.Server.Protocol.Game.IllusionTowerStage;
using Dfo.Server.Protocol.Game.BossTowerMap;
using Dfo.Server.Protocol.Game.IllusionTowerMap;
using Dfo.Server.Protocol.Game.Dungeons;
using Microsoft.Extensions.Logging;
namespace Dfo.Server.Protocol.Game;
internal sealed partial class GameMapLoadingCommandHandler :
    ICommandHandler<GameFinishLoadingCommand, bool>, ICommandHandler<GameMoveMapCommand, bool>,
    ICommandHandler<GameIllusionTowerStageCommand, bool>
{
    private readonly ISelectedSessionDomainService sessions;
    private readonly ICommandProjections projections;
    private readonly ILogger logger;
    private readonly IBossTowerDomainService bossTowers;
    private readonly IIllusionTowerDomainService illusionTowers;
    private readonly IDungeonParticipationDomainService participation;
    private readonly IGameCommandOutbox outbox;
    private readonly ICommandWorkUnits workUnits;
    private readonly IOnlineUserQueryDomainService onlineUsers;
    private readonly IPartyService parties;

    private readonly IDungeonRunDomainService runs;
    private readonly IDungeonVisitDomainService visits;
    private readonly IDungeonRunProjectionDomainService registry;
    private readonly ICharacterFatigueDomainService fatigue;
    private readonly IWeaknessDomainService weaknessState;
    private readonly ICommandConnectionClosures closures;
    private readonly IGameCommandOutbox mapOutbox;
    public GameMapLoadingCommandHandler(ISelectedSessionDomainService sessions, IDungeonRunDomainService runs,
        IDungeonVisitDomainService visits, IDungeonRunProjectionDomainService registry, IPartyService parties,
        ICharacterFatigueDomainService fatigue, IWeaknessDomainService weaknessState,
        IBossTowerDomainService bossTowers, IIllusionTowerDomainService illusionTowers,
        IDungeonParticipationDomainService participation, ICommandProjections projections, IGameCommandOutbox outbox, ICommandWorkUnits units,
        ICommandConnectionClosures closures, IOnlineUserQueryDomainService onlineUsers,
        ILogger<GameMapLoadingCommandHandler> logger)

    {
        this.sessions = sessions; this.projections = projections; this.logger = logger; this.bossTowers = bossTowers; this.illusionTowers = illusionTowers; this.participation = participation; this.outbox = outbox; this.workUnits = units; this.onlineUsers = onlineUsers; this.parties = parties;
        this.runs = runs; this.visits = visits; this.registry = registry;
        this.fatigue = fatigue; this.weaknessState = weaknessState; this.closures = closures; this.mapOutbox = outbox;
    }
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameFinishLoadingCommand command, CancellationToken token)
    {
        var id = command.Dispatch.Connection.ConnectionId;
        if (GameRequestFreshness.IsStale(command.Input.Metadata)) return true;
        // No session, town and ordinary runs all fall to the ordinary flow, which also answers town loads.
        var session = sessions.FindByConnection(id);
        switch (session is null ? null : DungeonLocator.Locate(session, participation, bossTowers, illusionTowers))
        {
            case DungeonPlace.InBossTower boss:
                await CompleteBossLoadingAsync(execution, boss, token).ConfigureAwait(false);
                return true;
            case DungeonPlace.InIllusionTower illusion:
                await CompleteIllusionLoadingAsync(execution, illusion, token).ConfigureAwait(false);
                return true;
        }
        var completed = await CompleteOrdinaryLoadingAsync(id, token).ConfigureAwait(false);
        if (!completed)
            logger.LogWarning("Finish loading {ConnectionId}: stale or missing selected-session generation", id);
        else
            logger.LogInformation("Finish loading {ConnectionId}: releasing the current map ({First},{Second})",
                id, command.Input.First, command.Input.Second);
        return completed;
    }
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameMoveMapCommand command, CancellationToken token)
    {
        var context = command.Dispatch; var message = command.Input;
        if (GameRequestFreshness.IsStale(message.Metadata))
        {
            logger.LogWarning("Move-map ignored connection={ConnectionId} sequence={Sequence} ordinal={Ordinal} target=({X},{Y}) reason=StaleSequence",
                context.Connection.ConnectionId, message.Metadata.Sequence, message.Metadata.InboundOrdinal, message.NextX, message.NextY);
            return true;
        }
        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("Move map received connection={ConnectionId} sequence={Sequence} ordinal={Ordinal} target=({X},{Y}) path=({PathX},{PathY})",
                context.Connection.ConnectionId, message.Metadata.Sequence, message.Metadata.InboundOrdinal,
                message.NextX, message.NextY, message.PathPositionX, message.PathPositionY);
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null)
        {
            logger.LogDebug(
                "Move map {ConnectionId}: no published session, ignoring ({X},{Y})",
                context.Connection.ConnectionId, message.NextX, message.NextY);
            return true;
        }
        switch (DungeonLocator.Locate(session, participation, bossTowers, illusionTowers))
        {
            case DungeonPlace.InBossTower boss:
                await AdvanceBossAsync(execution, context.Connection.ConnectionId, boss, message.Metadata, message.NextX, message.NextY, token).ConfigureAwait(false);
                logger.LogDebug("Move map routed connection={ConnectionId} sequence={Sequence} route=BossTower", context.Connection.ConnectionId, message.Metadata.Sequence);
                return true;
            case DungeonPlace.InIllusionTower illusion:
                await AdvanceIllusionAsync(execution, illusion, token).ConfigureAwait(false);
                logger.LogDebug("Move map routed connection={ConnectionId} sequence={Sequence} route=IllusionTower", context.Connection.ConnectionId, message.Metadata.Sequence);
                return true;
            default:
                await MoveOrdinaryAsync(execution, context, message, session, token).ConfigureAwait(false);
                return true;
        }
    }
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameIllusionTowerStageCommand command, CancellationToken token)
    {
        // Mode 2 reports clear before dialogue; actor deaths own clear, MOVE_MAP alone advances.
        if (command.Input.Mode == 1)
            _ = await StartIllusionAsync(execution, command.Dispatch.Connection.ConnectionId, command.Input.Metadata, token).ConfigureAwait(false);
        return true;
    }
    private ValueTask LoadingReadyAsync(SelectedSessionIdentity session, DungeonRunState expected, CancellationToken token)
    { _ = runs.TryMarkParticipantReady(session.CharacterId, expected.RunId, expected.RoomVersion); return ValueTask.CompletedTask; }
    private bool IsCurrentSession(SelectedSessionIdentity session, NetworkConnectionId id) => session.ConnectionId == id && sessions.IsInWorld(session);
    private IReadOnlyList<SelectedSessionIdentity> FindMoveRecipients(SelectedSessionIdentity actor, DungeonRunId id) =>
        DungeonPartyRecipients.FindCurrent(parties, sessions, runs, registry, actor, id);
    private ValueTask<DungeonMoveAttempt> MoveRoomAsync(CommandExecutionContext execution,
        SelectedSessionIdentity session, DungeonRunState current, MazeCell target, CancellationToken token) =>
        workUnits.WriteAsync(execution, new("move-dungeon-room", []),
            (_, ct) => visits.TryMoveAsync(session.CharacterId, current, target, ct), token);
    private async ValueTask MoveFatigueAsync(SelectedSessionIdentity session, CancellationToken token)
    {
        var balance = await fatigue.BalanceOfAsync(session.CharacterId, token).ConfigureAwait(false);
        if (sessions.IsInWorld(session))
            await RegisterNotiAsync(session.ConnectionId, new Fatigue.FatigueNotification(balance), token).ConfigureAwait(false);
    }
    private ValueTask RegisterNotiAsync(NetworkConnectionId id, IGameNotiMessage message, CancellationToken token) => mapOutbox.RegisterNotiAsync(id, message, token);
    private ValueTask LoadingWeaknessAsync(SelectedSessionIdentity session, CancellationToken token) => sessions.IsInWorld(session)
        ? RegisterNotiAsync(session.ConnectionId, new Weakness.WeaknessProjectionNotification(weaknessState.For(session.CharacterId).Value), token)
        : ValueTask.CompletedTask;
    private ValueTask<NotificationDelivery> LoadingQuotaAsync(SelectedSessionIdentity session, UsageQuotaFamily family, CancellationToken token) =>
        projections.ProjectAsync(new NotificationAudience.TheseSessions([session]), new UsageQuotaViewChanged(session.CharacterId, family), token);
    private async ValueTask LoadingMechanismsAsync(SelectedSessionIdentity session, DungeonRunState current, CancellationToken token)
    {
        var states = current.CurrentCellState.Mechanisms;
        if (states.Count == 0 || states.Any(state => state.StartedAt is null)) return;
        var audience = sessions.FindByCharacter(session.CharacterId) is { } recipient ? new[] { recipient } : [];
        var result = await projections.ProjectAsync(new NotificationAudience.TheseSessions(audience),
            new DungeonRoomMechanismsNotification(new(current.RunId, current.CurrentCell, current.RoomVersion, states, [session.CharacterId])), token).ConfigureAwait(false);
        if (result.Attempts.Any(attempt => attempt.Outcome == NotificationOutcome.Failed))
            throw new IOException("Room mechanism projection failed; the current snapshot will be retried.");
    }
}
