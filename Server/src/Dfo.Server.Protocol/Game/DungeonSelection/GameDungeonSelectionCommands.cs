using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.Weakness;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.DungeonSelection;

public sealed record GameEnterSelectDungeonCommand(NetworkDispatchContext Dispatch, EnterSelectDungeonRequest Input)
    : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed record GameBackToVillageCommand(NetworkDispatchContext Dispatch, BackToVillageRequest Input)
    : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

internal partial class GameDungeonSelectionCommandHandler : PartyMutationProtocolCommandHandler,
    ICommandHandler<GameEnterSelectDungeonCommand, bool>, ICommandHandler<GameBackToVillageCommand, bool>
{
    private readonly IDungeonVisitDomainService visits;
    private readonly IDungeonEntryIntentDomainService? entryIntents;
    private readonly IDespairTowerDomainService? despair;
    private readonly ISaoTowerDomainService? sao;

    public GameDungeonSelectionCommandHandler(
        ISelectedSessionDomainService sessions, ISelectedCharacterCurrentViewDomainService currentViews,
        IOnlineCharacterRuntimeDomainService onlineCharacters, ITownAreaDomainService townAreas, IPartyService parties,
        IPartyProjectionStateDomainService projectionState, ICharacterStateProjectionDomainService currentState,
        ICharacterDomainService characters, ITownMovementDomainService towns, IDungeonVisitDomainService visits,
        IDungeonRunDomainService runs, IDungeonRunProjectionDomainService departureSink, ICommandProjections projections,
        IGameCommandOutbox outbox, ICommandWorkUnits units, ICommandConnectionClosures closures, IOnlineUserQueryDomainService onlineUsers,
        ILogger<GameDungeonSelectionCommandHandler> logger, IDungeonEntryIntentDomainService? entryIntents = null,
        IMarriageHousingQueryDomainService? housing = null, IBossTowerDomainService? bossTowers = null, IIllusionTowerDomainService? illusionTowers = null,
        IDespairTowerDomainService? despair = null, ISaoTowerDomainService? sao = null, TimeProvider? clock = null, IWeaknessDomainService? weakness = null)
        : base(sessions, parties, townAreas, projections, projectionState.StreamOf, null, logger, currentViews, currentState, characters,
            onlineCharacters, towns, housing, bossTowers, illusionTowers, clock, outbox.RegisterExplicitAsync,
            session => onlineUsers.TryGetByConnection(session.ConnectionId, out var user) && user is not null
                && user.AccountId == session.AccountId && user.CurrentCharacterId == session.CharacterId
                    ? closures.RequestCloseAsync(user.Connection, CancellationToken.None) : ValueTask.CompletedTask,
            weakness is null ? null : (session, token) => sessions.IsInWorld(session)
                ? outbox.RegisterNotiAsync(session.ConnectionId, new WeaknessProjectionNotification(weakness.For(session.CharacterId).Value), token)
                : ValueTask.CompletedTask,
            workUnits: units, onlineUsers: onlineUsers, dungeonRuns: runs, departureSink: departureSink)
    {
        this.visits = visits;
        this.entryIntents = entryIntents;
        this.despair = despair;
        this.sao = sao;
    }

    public ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameEnterSelectDungeonCommand command, CancellationToken token) =>
        EnterSelectDungeonAsync(execution, command.Dispatch, command.Input, token);

    public ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameBackToVillageCommand command, CancellationToken token) =>
        BackToVillageAsync(execution, command.Dispatch, command.Input, token);

    protected virtual async ValueTask SelectionQuotaAsync(SelectedSessionIdentity session, CancellationToken token) =>
        _ = await projections.ProjectAsync(new Dfo.Server.Abstractions.Notifications.NotificationAudience.TheseSessions([session]),
            new Dfo.Server.Abstractions.UsageQuotas.UsageQuotaViewChanged(session.CharacterId,
                Dfo.Server.Abstractions.UsageQuotas.UsageQuotaFamily.MirrorDungeon), token).ConfigureAwait(false);

    protected virtual async ValueTask<bool> SelectionPrepareBossAsync(CommandExecutionContext execution,
        NetworkConnectionId connectionId, GameMessageMetadata metadata, int dungeon, CancellationToken token)
    {
        var session = sessions.FindByConnection(connectionId);
        var run = session is null ? null : bossTowers?.Find(session);
        if (session is null || run is null) return false;
        // Selection checks for an attached run before consuming a stale request; return does the reverse.
        if (TowerProtocol.IsStale(metadata)) return true;
        if (!BossTowerExact(session, run) || !run.Members.Any(member => member.Session == session && member.IsLeader)) return true;
        var participants = run.Members.Select(member => member.Session == session
            ? CaptureParticipant(member.Session) : new SelectedSessionRecipient(member.Session, null)).ToArray();
        var released = await workUnits!.WriteAsync(execution, new("selection-release-boss-tower", participants),
            (_, ct) => bossTowers!.ReleaseForSelectionAsync(BossTowerContext(run, session), dungeon, ct), token).ConfigureAwait(false);
        return !released.Changed;
    }

    protected virtual async ValueTask<bool> SelectionLeaveBossAsync(CommandExecutionContext execution,
        NetworkConnectionId connectionId, GameMessageMetadata metadata, IGameServerMessage response, CancellationToken token)
    {
        if (bossTowers is null) return false;
        if (TowerProtocol.IsStale(metadata)) return true;
        var session = sessions.FindByConnection(connectionId);
        var run = session is null ? null : bossTowers.Find(session);
        if (session is null || run is null) return false;
        if (!BossTowerExact(session, run)) return true;
        var action = await LeaveBossPartyTowerAsync(execution, BossTowerContext(run, session),
            run.Members.Select(member => member.Session).ToArray(), token).ConfigureAwait(false);
        if (action.Changed)
            await ProjectBossTowerPartyDepartureAsync(execution, action, session, response, token).ConfigureAwait(false);
        return true;
    }

    protected virtual async ValueTask<bool> SelectionLeaveIllusionAsync(CommandExecutionContext execution,
        NetworkConnectionId connectionId, GameMessageMetadata metadata, IGameServerMessage response, CancellationToken token)
    {
        if (illusionTowers is null) return false;
        if (TowerProtocol.IsStale(metadata)) return true;
        var session = sessions.FindByConnection(connectionId);
        var run = session is null ? null : illusionTowers.Find(session);
        if (session is null || run is null) return false;
        if (!IllusionTowerExact(session, run)) return true;
        var action = await LeaveIllusionPartyTowerAsync(execution, IllusionTowerContext(run, session),
            run.Members.Select(member => member.Session).ToArray(), token).ConfigureAwait(false);
        if (action.Changed)
            await ProjectIllusionTowerPartyDepartureAsync(execution, action, session, response, token).ConfigureAwait(false);
        return true;
    }
}
