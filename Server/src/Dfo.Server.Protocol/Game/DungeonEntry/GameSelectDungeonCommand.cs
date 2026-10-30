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
using Dfo.Server.Protocol.Game.Weakness;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Party;
using Microsoft.Extensions.Logging;
namespace Dfo.Server.Protocol.Game.DungeonEntry;
public sealed record GameSelectDungeonCommand(NetworkDispatchContext Dispatch, SelectDungeonRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
internal sealed partial class GameSelectDungeonCommandHandler : ICommandHandler<GameSelectDungeonCommand, bool>
{
    private readonly ISelectedSessionDomainService sessions;
    private readonly ITownAreaDomainService townAreas;
    private readonly ICommandProjections projections;
    private readonly IPartyProjectionStateDomainService projectionState;
    private readonly ILogger logger;
    private readonly ISelectedCharacterCurrentViewDomainService currentViews;
    private readonly ICharacterStateProjectionDomainService currentState;
    private readonly ICharacterDomainService characters;
    private readonly ITownTransitDomainService transit;
    private readonly IMarriageHousingQueryDomainService housing;
    private readonly IBossTowerDomainService bossTowers;
    private readonly IIllusionTowerDomainService illusionTowers;
    private readonly TimeProvider clock;
    private readonly IGameCommandOutbox outbox;
    private readonly ICommandConnectionClosures closures;
    private readonly ICommandWorkUnits workUnits;
    private readonly IOnlineUserQueryDomainService onlineUsers;
    private readonly IDungeonRunProjectionDomainService departureSink;
    private readonly IPartyService parties;

    private readonly ICommandConnectionClosures entryClosures;
    private readonly IDungeonVisitDomainService visits;
    private readonly IDungeonRunEntryDomainService runs;
    private readonly IDungeonEntryIntentDomainService entryIntents;
    private readonly ICharacterQuestLogDomainService questLogs;
    private readonly ICharacterFatigueDomainService fatigue;
    private readonly IOutboundOrdering ordering;
    private readonly IDespairTowerDomainService despair;
    private readonly IDungeonEntryRoutingDomainService entries;
    public GameSelectDungeonCommandHandler(ISelectedSessionDomainService sessions, ISelectedCharacterCurrentViewDomainService currentViews,
        ITownAreaDomainService townAreas, IPartyService parties,
        IPartyProjectionStateDomainService projectionState, ICharacterStateProjectionDomainService currentState,
        ICharacterDomainService characters, ITownTransitDomainService transit, IDungeonRunProjectionDomainService departureSink,
        ICommandProjections projections, IGameCommandOutbox outbox, ICommandWorkUnits units, ICommandConnectionClosures closures,
        IOnlineUserQueryDomainService onlineUsers, ILogger<GameSelectDungeonCommandHandler> logger,
        IBossTowerDomainService bossTowers, IIllusionTowerDomainService illusionTowers, IMarriageHousingQueryDomainService housing, TimeProvider clock,
        IDungeonVisitDomainService visits,
        IDungeonRunEntryDomainService runs,
        IDungeonEntryIntentDomainService entryIntents,
        ICharacterQuestLogDomainService questLogs,
        ICharacterFatigueDomainService fatigue,
        IOutboundOrdering ordering,
        IDespairTowerDomainService despair,
        IDungeonEntryRoutingDomainService entries)

    {
        this.sessions = sessions; this.townAreas = townAreas; this.projections = projections; this.projectionState = projectionState; this.logger = logger; this.currentViews = currentViews; this.currentState = currentState; this.characters = characters; this.transit = transit; this.housing = housing; this.bossTowers = bossTowers; this.illusionTowers = illusionTowers; this.clock = clock; this.outbox = outbox; this.closures = closures; this.workUnits = units; this.onlineUsers = onlineUsers; this.departureSink = departureSink; this.parties = parties;
        entryClosures = closures;
        this.visits = visits;
        this.runs = runs;
        this.entryIntents = entryIntents;
        this.questLogs = questLogs;
        this.fatigue = fatigue;
        this.ordering = ordering;
        this.despair = despair;
        this.entries = entries;
    }
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameSelectDungeonCommand command, CancellationToken token)
    {
        await SelectDungeonAsync(execution, command.Dispatch, command.Input, token).ConfigureAwait(false);
        return true;
    }
    private NetworkConnectionContext? CaptureEntryConnection(SelectedSessionIdentity session) =>
        onlineUsers.TryGetByConnection(session.ConnectionId, out var user) == true && user is not null
            && user.AccountId == session.AccountId && user.CurrentCharacterId == session.CharacterId ? user.Connection : null;
    private ValueTask CloseEntryAsync(SelectedSessionIdentity session, NetworkConnectionContext? captured) =>
        captured is not null ? entryClosures.RequestCloseAsync(captured, CancellationToken.None) : ValueTask.CompletedTask;
    private ValueTask<Dfo.Server.Abstractions.World.BossTower.BossTowerEntryResult> AdmitBossAsync(CommandExecutionContext execution, IReadOnlyList<TowerEntryMember> members, SelectedSessionIdentity requester, uint seed, CancellationToken token, byte requestMode, byte difficulty, byte practiceMode) =>
        workUnits.WriteAsync(execution, new("boss-tower-entry", members.Select(value => value.Session == requester ? CaptureParticipant(value.Session) : new SelectedSessionRecipient(value.Session, null)).ToArray()),
            (_, ct) => bossTowers.EnterAsync(members, requester, seed, ct, requestMode, difficulty, practiceMode), token);
    private ValueTask<TowerEntryResult> AdmitIllusionAsync(CommandExecutionContext execution, IReadOnlyList<TowerEntryMember> members, SelectedSessionIdentity requester, uint seed, CancellationToken token, int dungeonId) =>
        workUnits.WriteAsync(execution, new("illusion-tower-entry", []), (_, ct) => illusionTowers.EnterAsync(members, requester, seed, ct, dungeonId), token);
}
