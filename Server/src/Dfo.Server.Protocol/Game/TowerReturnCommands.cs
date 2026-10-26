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
public sealed record CaptureBossTowerReturnsDueCommand : ICommand<TowerReturnsDue<BossTowerActionContext>>;
public sealed record ReturnBossTowerRunCommand(BossTowerActionContext Context, DateTimeOffset CapturedAt) : ICommand<bool>;
public sealed record CaptureIllusionTowerDeparturesCommand : ICommand<ImmutableArray<TowerDepartureEntryId>>;
public sealed record ProjectIllusionTowerDepartureCommand(TowerDepartureEntryId Entry) : ICommand<bool>;
public sealed record CaptureIllusionTowerReturnsDueCommand : ICommand<TowerReturnsDue<TowerActionContext>>;
public sealed record ReturnIllusionTowerRunCommand(TowerActionContext Context, DateTimeOffset CapturedAt) : ICommand<bool>;

internal sealed partial class TowerReturnCommandHandler : PartyMutationProtocolCommandHandler,
    ICommandHandler<CaptureBossTowerDeparturesCommand, ImmutableArray<TowerDepartureEntryId>>,
    ICommandHandler<ProjectBossTowerDepartureCommand, bool>,
    ICommandHandler<CaptureBossTowerReturnsDueCommand, TowerReturnsDue<BossTowerActionContext>>,
    ICommandHandler<ReturnBossTowerRunCommand, bool>,
    ICommandHandler<CaptureIllusionTowerDeparturesCommand, ImmutableArray<TowerDepartureEntryId>>,
    ICommandHandler<ProjectIllusionTowerDepartureCommand, bool>,
    ICommandHandler<CaptureIllusionTowerReturnsDueCommand, TowerReturnsDue<TowerActionContext>>,
    ICommandHandler<ReturnIllusionTowerRunCommand, bool>,
    ICommandHandler<MaintainDungeonReturnsCommand, bool>, ICommandHandler<ReturnDungeonRunCommand, bool>
{
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
        IOutboundOrdering? ordering = null)
        : base(sessions, parties, townAreas, projections, projectionState.StreamOf, ordering, logger,
            currentViews, currentState, characters, onlineCharacters, towns, housing, bossTowers, illusionTowers, clock,
            outbox.RegisterExplicitAsync,
            session => onlineUsers.TryGetByConnection(session.ConnectionId, out var user) && user is not null
                && user.AccountId == session.AccountId && user.CurrentCharacterId == session.CharacterId
                ? closures.RequestCloseAsync(user.Connection, CancellationToken.None) : ValueTask.CompletedTask,
            workUnits: units, onlineUsers: onlineUsers)
    { this.bossDepartures = bossDepartures; this.illusionDepartures = illusionDepartures; this.wipeRuns = wipeRuns; this.wipeRegistry = wipeRegistry;
      this.wipeCards = cards; this.wipeAltars = altars; this.wipeTournaments = tournaments; this.wipeSlots = slots; this.wipeWake = wake; }

    public ValueTask<ImmutableArray<TowerDepartureEntryId>> HandleAsync(CommandExecutionContext context,
        CaptureBossTowerDeparturesCommand command, CancellationToken token) => ValueTask.FromResult(bossDepartures.SnapshotPending());
    public ValueTask<ImmutableArray<TowerDepartureEntryId>> HandleAsync(CommandExecutionContext context,
        CaptureIllusionTowerDeparturesCommand command, CancellationToken token) => ValueTask.FromResult(illusionDepartures.SnapshotPending());
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, ProjectBossTowerDepartureCommand command, CancellationToken token)
    {
        if (!bossDepartures.TryClaim(command.Entry, out var action)) return false;
        await ProjectBossTowerPartyDepartureAsync(context, action!, null, null, token).ConfigureAwait(false);
        return true;
    }
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, ProjectIllusionTowerDepartureCommand command, CancellationToken token)
    {
        if (!illusionDepartures.TryClaim(command.Entry, out var action)) return false;
        await ProjectIllusionTowerPartyDepartureAsync(context, action!, null, null, token).ConfigureAwait(false);
        return true;
    }
    public ValueTask<TowerReturnsDue<BossTowerActionContext>> HandleAsync(CommandExecutionContext context,
        CaptureBossTowerReturnsDueCommand command, CancellationToken token)
    {
        var now = clock.GetUtcNow();
        return ValueTask.FromResult(new TowerReturnsDue<BossTowerActionContext>(now, bossTowers.FindReturnsDue(now).ToImmutableArray()));
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