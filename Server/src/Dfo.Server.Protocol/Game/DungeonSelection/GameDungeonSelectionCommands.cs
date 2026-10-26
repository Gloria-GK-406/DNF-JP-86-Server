using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Application.Execution;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Parties;
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
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using System.Collections.Immutable;
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

internal sealed partial class GameDungeonSelectionCommandHandler :
    ICommandHandler<GameEnterSelectDungeonCommand, bool>, ICommandHandler<GameBackToVillageCommand, bool>
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
    private readonly TimeProvider clock;
    private readonly IGameCommandOutbox outbox;
    private readonly ICommandConnectionClosures closures;
    private readonly IWeaknessDomainService weaknessState;
    private readonly ICommandWorkUnits workUnits;
    private readonly IOnlineUserQueryDomainService onlineUsers;
    private readonly IDungeonRunDomainService dungeonRuns;
    private readonly IDungeonRunProjectionDomainService departureSink;
    private readonly IPartyService parties;
    private const bool projectWeakness = true;

    private readonly IDungeonVisitDomainService visits;
    private readonly IDungeonEntryIntentDomainService entryIntents;
    private readonly IDespairTowerDomainService despair;
    private readonly ISaoTowerDomainService sao;

    public GameDungeonSelectionCommandHandler(
        ISelectedSessionDomainService sessions, ISelectedCharacterCurrentViewDomainService currentViews,
        IOnlineCharacterRuntimeDomainService onlineCharacters, ITownAreaDomainService townAreas, IPartyService parties,
        IPartyProjectionStateDomainService projectionState, ICharacterStateProjectionDomainService currentState,
        ICharacterDomainService characters, ITownMovementDomainService towns, IDungeonVisitDomainService visits,
        IDungeonRunDomainService runs, IDungeonRunProjectionDomainService departureSink, ICommandProjections projections,
        IGameCommandOutbox outbox, ICommandWorkUnits units, ICommandConnectionClosures closures, IOnlineUserQueryDomainService onlineUsers,
        ILogger<GameDungeonSelectionCommandHandler> logger, IDungeonEntryIntentDomainService entryIntents,
        IMarriageHousingQueryDomainService housing, IBossTowerDomainService bossTowers, IIllusionTowerDomainService illusionTowers,
        IDespairTowerDomainService despair, ISaoTowerDomainService sao, TimeProvider clock, IWeaknessDomainService weakness)

    {
        this.sessions = sessions; this.townAreas = townAreas; this.projections = projections; this.projectionState = projectionState; this.logger = logger; this.currentViews = currentViews; this.currentState = currentState; this.characters = characters; this.onlineCharacters = onlineCharacters; this.towns = towns; this.housing = housing; this.bossTowers = bossTowers; this.illusionTowers = illusionTowers; this.clock = clock; this.outbox = outbox; this.closures = closures; this.weaknessState = weakness; this.workUnits = units; this.onlineUsers = onlineUsers; this.dungeonRuns = runs; this.departureSink = departureSink; this.parties = parties;
        this.visits = visits;
        this.entryIntents = entryIntents;
        this.despair = despair;
        this.sao = sao;
    }

    public ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameEnterSelectDungeonCommand command, CancellationToken token) =>
        EnterSelectDungeonAsync(execution, command.Dispatch, command.Input, token);

    public ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameBackToVillageCommand command, CancellationToken token) =>
        BackToVillageAsync(execution, command.Dispatch, command.Input, token);

    private async ValueTask SelectionQuotaAsync(SelectedSessionIdentity session, CancellationToken token) =>
        _ = await projections.ProjectAsync(new Dfo.Server.Abstractions.Notifications.NotificationAudience.TheseSessions([session]),
            new Dfo.Server.Abstractions.UsageQuotas.UsageQuotaViewChanged(session.CharacterId,
                Dfo.Server.Abstractions.UsageQuotas.UsageQuotaFamily.MirrorDungeon), token).ConfigureAwait(false);

    private async ValueTask<bool> SelectionPrepareBossAsync(CommandExecutionContext execution,
        NetworkConnectionId connectionId, GameMessageMetadata metadata, int dungeon, CancellationToken token)
    {
        var session = sessions.FindByConnection(connectionId);
        var run = session is null ? null : bossTowers.Find(session);
        if (session is null || run is null) return false;
        // Selection checks for an attached run before consuming a stale request; return does the reverse.
        if (TowerProtocol.IsStale(metadata)) return true;
        if (!BossTowerActionProjection.BossTowerExact(sessions, bossTowers, session, run) || !run.Members.Any(member => member.Session == session && member.IsLeader)) return true;
        var participants = run.Members.Select(member => member.Session == session
            ? CaptureParticipant(member.Session) : new SelectedSessionRecipient(member.Session, null)).ToArray();
        var released = await workUnits.WriteAsync(execution, new("selection-release-boss-tower", participants),
            (_, ct) => bossTowers.ReleaseForSelectionAsync(BossTowerActionProjection.BossTowerContext(run, session), dungeon, ct), token).ConfigureAwait(false);
        return !released.Changed;
    }

    private async ValueTask<bool> SelectionLeaveBossAsync(CommandExecutionContext execution,
        NetworkConnectionId connectionId, GameMessageMetadata metadata, IGameServerMessage response, CancellationToken token)
    {
        if (bossTowers is null) return false;
        if (TowerProtocol.IsStale(metadata)) return true;
        var session = sessions.FindByConnection(connectionId);
        var run = session is null ? null : bossTowers.Find(session);
        if (session is null || run is null) return false;
        if (!BossTowerActionProjection.BossTowerExact(sessions, bossTowers, session, run)) return true;
        var action = await LeaveBossPartyTowerAsync(execution, BossTowerActionProjection.BossTowerContext(run, session),
            run.Members.Select(member => member.Session).ToArray(), token).ConfigureAwait(false);
        if (action.Changed)
            await ProjectBossTowerPartyDepartureAsync(execution, action, session, response, token).ConfigureAwait(false);
        return true;
    }

    private async ValueTask<bool> SelectionLeaveIllusionAsync(CommandExecutionContext execution,
        NetworkConnectionId connectionId, GameMessageMetadata metadata, IGameServerMessage response, CancellationToken token)
    {
        if (illusionTowers is null) return false;
        if (TowerProtocol.IsStale(metadata)) return true;
        var session = sessions.FindByConnection(connectionId);
        var run = session is null ? null : illusionTowers.Find(session);
        if (session is null || run is null) return false;
        if (!IllusionTowerActionProjection.IllusionTowerExact(sessions, illusionTowers, session, run)) return true;
        var action = await LeaveIllusionPartyTowerAsync(execution, IllusionTowerActionProjection.IllusionTowerContext(run, session),
            run.Members.Select(member => member.Session).ToArray(), token).ConfigureAwait(false);
        if (action.Changed)
            await ProjectIllusionTowerPartyDepartureAsync(execution, action, session, response, token).ConfigureAwait(false);
        return true;
    }
}
