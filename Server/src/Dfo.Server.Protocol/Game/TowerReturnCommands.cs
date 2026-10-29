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
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.Party;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game;

public sealed record CaptureBossTowerDeparturesCommand : ICommand<ImmutableArray<TowerDepartureEntryId>>;
public sealed record ProjectBossTowerDepartureCommand(TowerDepartureEntryId Entry) : ICommand<bool>;
public sealed record CaptureBossTowerReturnsDueCommand : ICommand<TowerReturnsDue<TowerActionContext>>;
public sealed record ReturnBossTowerRunCommand(TowerActionContext Context, DateTimeOffset CapturedAt) : ICommand<bool>;
public sealed record CaptureIllusionTowerDeparturesCommand : ICommand<ImmutableArray<TowerDepartureEntryId>>;
public sealed record ProjectIllusionTowerDepartureCommand(TowerDepartureEntryId Entry) : ICommand<bool>;
public sealed record CaptureIllusionTowerReturnsDueCommand : ICommand<TowerReturnsDue<TowerActionContext>>;
public sealed record ReturnIllusionTowerRunCommand(TowerActionContext Context, DateTimeOffset CapturedAt) : ICommand<bool>;

internal sealed partial class TowerReturnCommandHandler :
    ICommandHandler<CaptureBossTowerDeparturesCommand, ImmutableArray<TowerDepartureEntryId>>,
    ICommandHandler<ProjectBossTowerDepartureCommand, bool>,
    ICommandHandler<CaptureBossTowerReturnsDueCommand, TowerReturnsDue<TowerActionContext>>,
    ICommandHandler<ReturnBossTowerRunCommand, bool>,
    ICommandHandler<CaptureIllusionTowerDeparturesCommand, ImmutableArray<TowerDepartureEntryId>>,
    ICommandHandler<ProjectIllusionTowerDepartureCommand, bool>,
    ICommandHandler<CaptureIllusionTowerReturnsDueCommand, TowerReturnsDue<TowerActionContext>>,
    ICommandHandler<ReturnIllusionTowerRunCommand, bool>,
    ICommandHandler<MaintainDungeonReturnsCommand, ImmutableArray<ReturnDungeonRunCommand>>,
    ICommandHandler<ReturnDungeonRunCommand, ImmutableArray<SelectedSessionIdentity>>,
    ICommandHandler<ProjectDungeonReturnCommand, bool>
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
    private readonly ITowerRunKind bossKind;
    private readonly ITowerRunKind illusionKind;
    internal TowerPartyDeparture Departures { get; }
    private readonly TimeProvider clock;
    private readonly IGameCommandOutbox outbox;
    private readonly ICommandConnectionClosures closures;
    private readonly IWeaknessDomainService weaknessState;
    private readonly ICommandWorkUnits workUnits;
    private readonly IOnlineUserQueryDomainService onlineUsers;
    private readonly IPartyService parties;
    private readonly IOutboundOrdering ordering;
    private const bool projectWeakness = false;

    private readonly IBossTowerDepartureProjectionDomainService bossDepartures;
    private readonly IIllusionTowerDepartureProjectionDomainService illusionDepartures;
    public TowerReturnCommandHandler(ISelectedSessionDomainService sessions, IPartyService parties,
        ITownAreaDomainService townAreas, ICommandProjections projections, IPartyProjectionStateDomainService projectionState,
        ISelectedCharacterCurrentViewDomainService currentViews, ICharacterStateProjectionDomainService currentState,
        ICharacterDomainService characters, IOnlineCharacterRuntimeDomainService onlineCharacters,
        ITownMovementDomainService towns, IMarriageHousingQueryDomainService housing,
        IBossTowerDomainService bossTowers, IIllusionTowerDomainService illusionTowers,
        IOnlineUserQueryDomainService onlineUsers, ICommandWorkUnits units, IGameCommandOutbox outbox,
        ICommandConnectionClosures closures, IBossTowerDepartureProjectionDomainService bossDepartures,
        IIllusionTowerDepartureProjectionDomainService illusionDepartures, TimeProvider clock,
        ILogger<TowerReturnCommandHandler> logger, IDungeonRunDomainService wipeRuns,
        IDungeonRunProjectionDomainService wipeRegistry, Dfo.Server.Abstractions.World.IDungeonCardPresentationControl cards,
        IAltarDungeonDomainService altars, ITournamentDungeonDomainService tournaments,
        Dfo.Server.Abstractions.Inventory.IInventorySlotProjectionDomainService slots, IAltarSchedulerWakeSignal wake,
        IOutboundOrdering ordering, IWeaknessDomainService weaknessState)

    {
        this.sessions = sessions; this.townAreas = townAreas; this.projections = projections; this.projectionState = projectionState; this.logger = logger; this.currentViews = currentViews; this.currentState = currentState; this.characters = characters; this.onlineCharacters = onlineCharacters; this.towns = towns; this.housing = housing; this.bossTowers = bossTowers; this.illusionTowers = illusionTowers; bossKind = new BossTowerRunKind(bossTowers); illusionKind = new IllusionTowerRunKind(illusionTowers); this.clock = clock; this.outbox = outbox; this.closures = closures; this.weaknessState = weaknessState; this.workUnits = units; this.onlineUsers = onlineUsers; this.parties = parties; this.ordering = ordering; this.bossDepartures = bossDepartures; this.illusionDepartures = illusionDepartures; this.wipeRuns = wipeRuns; this.wipeRegistry = wipeRegistry;
      this.wipeCards = cards; this.wipeAltars = altars; this.wipeTournaments = tournaments; this.wipeSlots = slots; this.wipeWake = wake;
        Departures = new TowerPartyDeparture(this.sessions, this.onlineUsers, this.characters, this.clock, this.outbox,
            this.currentViews, this.currentState, this.onlineCharacters, workUnits, this.closures, closeTargets, this.logger,
            ReturnTownIsolatedAsync);
    }

    public ValueTask<ImmutableArray<TowerDepartureEntryId>> HandleAsync(CommandExecutionContext context,
        CaptureBossTowerDeparturesCommand command, CancellationToken token) => ValueTask.FromResult(bossDepartures.SnapshotPending());
    public ValueTask<ImmutableArray<TowerDepartureEntryId>> HandleAsync(CommandExecutionContext context,
        CaptureIllusionTowerDeparturesCommand command, CancellationToken token) => ValueTask.FromResult(illusionDepartures.SnapshotPending());
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, ProjectBossTowerDepartureCommand command, CancellationToken token)
    {
        if (!bossDepartures.TryClaim(command.Entry, out var action)) return false;
        await Departures.ProjectDepartureAsync(context, bossKind, action!, null, null, token).ConfigureAwait(false);
        return true;
    }
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, ProjectIllusionTowerDepartureCommand command, CancellationToken token)
    {
        if (!illusionDepartures.TryClaim(command.Entry, out var action)) return false;
        await Departures.ProjectDepartureAsync(context, illusionKind, action!, null, null, token).ConfigureAwait(false);
        return true;
    }
    public ValueTask<TowerReturnsDue<TowerActionContext>> HandleAsync(CommandExecutionContext context,
        CaptureBossTowerReturnsDueCommand command, CancellationToken token)
    {
        var now = clock.GetUtcNow();
        return ValueTask.FromResult(new TowerReturnsDue<TowerActionContext>(now, bossTowers.FindReturnsDue(now).ToImmutableArray()));
    }
    public ValueTask<TowerReturnsDue<TowerActionContext>> HandleAsync(CommandExecutionContext context,
        CaptureIllusionTowerReturnsDueCommand command, CancellationToken token)
    {
        var now = clock.GetUtcNow();
        return ValueTask.FromResult(new TowerReturnsDue<TowerActionContext>(now, illusionTowers.FindReturnsDue(now).ToImmutableArray()));
    }
    public ValueTask<bool> HandleAsync(CommandExecutionContext execution, ReturnBossTowerRunCommand command, CancellationToken token) => ReturnBossTowerRunAsync(execution, command, token);
    public ValueTask<bool> HandleAsync(CommandExecutionContext execution, ReturnIllusionTowerRunCommand command, CancellationToken token) => ReturnIllusionTowerRunAsync(execution, command, token);
}