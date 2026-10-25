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
internal partial class GameSelectDungeonCommandHandler : PartyMutationProtocolCommandHandler, ICommandHandler<GameSelectDungeonCommand, bool>
{
    private static readonly IInventorySlotMappingRuleService SlotMappings = new InventorySlotMappingRuleService();
    private readonly ICommandConnectionClosures entryClosures;
    private readonly IDungeonVisitDomainService visits;
    private readonly IDungeonRunDomainService runs;
    private readonly IDungeonEntryIntentDomainService entryIntents;
    private readonly ICharacterQuestLogDomainService questLogs;
    private readonly ICharacterFatigueDomainService fatigue;
    private readonly IInventorySlotProjectionDomainService inventoryProjection;
    private readonly IOutboundOrdering ordering;
    private readonly IDungeonEntryDecisionDomainService decisions;
    private readonly IHellDungeonEntryDomainService hell;
    private readonly ICycleDungeonEntryDomainService cycles;
    private readonly IMirrorDungeonEntryDomainService mirrors;
    private readonly IDimensionDungeonDomainService dimensions;
    private readonly IDespairTowerDomainService despair;
    private readonly ITournamentDungeonDomainService tournaments;
    private readonly IAltarDungeonDomainService altars;
    private readonly ISaoTowerDomainService sao;
    private readonly ITauKingdomDomainService tau;
    private readonly IHerasDungeonDomainService heras;
    private readonly ICoupleDungeonDomainService couples;
    private readonly ISouthernDaleDomainService southern;
    private readonly ISealedTreasureDomainService sealedTreasure;
    private readonly IGoldSilverHornDomainService goldSilverHorn;
    public GameSelectDungeonCommandHandler(ISelectedSessionDomainService sessions, ISelectedCharacterCurrentViewDomainService currentViews,
        IOnlineCharacterRuntimeDomainService onlineCharacters, ITownAreaDomainService townAreas, IPartyService parties,
        IPartyProjectionStateDomainService projectionState, ICharacterStateProjectionDomainService currentState,
        ICharacterDomainService characters, ITownMovementDomainService towns, IDungeonRunProjectionDomainService departureSink,
        ICommandProjections projections, IGameCommandOutbox outbox, ICommandWorkUnits units, ICommandConnectionClosures closures,
        IOnlineUserQueryDomainService onlineUsers, ILogger<GameSelectDungeonCommandHandler> logger,
        IBossTowerDomainService bossTowers, IIllusionTowerDomainService illusionTowers, IMarriageHousingQueryDomainService housing, TimeProvider clock,
        IDungeonVisitDomainService visits,
        IDungeonRunDomainService runs,
        IDungeonEntryIntentDomainService entryIntents,
        ICharacterQuestLogDomainService questLogs,
        ICharacterFatigueDomainService fatigue,
        IInventorySlotProjectionDomainService inventoryProjection,
        IOutboundOrdering ordering,
        IDungeonEntryDecisionDomainService decisions,
        IHellDungeonEntryDomainService hell,
        ICycleDungeonEntryDomainService cycles,
        IMirrorDungeonEntryDomainService mirrors,
        IDimensionDungeonDomainService dimensions,
        IDespairTowerDomainService despair,
        ITournamentDungeonDomainService tournaments,
        IAltarDungeonDomainService altars,
        ISaoTowerDomainService sao,
        ITauKingdomDomainService tau,
        IHerasDungeonDomainService heras,
        ICoupleDungeonDomainService couples,
        ISouthernDaleDomainService southern,
        ISealedTreasureDomainService sealedTreasure,
        IGoldSilverHornDomainService goldSilverHorn)
        : base(sessions, parties, townAreas, projections, projectionState.StreamOf, ordering, logger, currentViews, currentState,
            characters, onlineCharacters, towns, housing, bossTowers, illusionTowers, clock, outbox.RegisterExplicitAsync,
            session => onlineUsers.TryGetByConnection(session.ConnectionId, out var user) && user is not null
                && user.AccountId == session.AccountId && user.CurrentCharacterId == session.CharacterId
                    ? closures.RequestCloseAsync(user.Connection, CancellationToken.None) : ValueTask.CompletedTask,
            workUnits: units, onlineUsers: onlineUsers, dungeonRuns: runs, departureSink: departureSink)
    {
        entryClosures = closures;
        this.visits = visits;
        this.runs = runs;
        this.entryIntents = entryIntents;
        this.questLogs = questLogs;
        this.fatigue = fatigue;
        this.inventoryProjection = inventoryProjection;
        this.ordering = ordering;
        this.decisions = decisions;
        this.hell = hell;
        this.cycles = cycles;
        this.mirrors = mirrors;
        this.dimensions = dimensions;
        this.despair = despair;
        this.tournaments = tournaments;
        this.altars = altars;
        this.sao = sao;
        this.tau = tau;
        this.heras = heras;
        this.couples = couples;
        this.southern = southern;
        this.sealedTreasure = sealedTreasure;
        this.goldSilverHorn = goldSilverHorn;
    }
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameSelectDungeonCommand command, CancellationToken token)
    {
        await SelectDungeonAsync(execution, command.Dispatch, command.Input, token).ConfigureAwait(false);
        return true;
    }
    protected virtual ValueTask<DungeonEntryAttempt> RouteEntryAsync(DungeonEntryContext entry, CancellationToken cancellationToken) => couples?.Supports(entry.DungeonId) == true
        ? couples.EnterAsync(entry, cancellationToken)
        : goldSilverHorn?.Supports(entry.DungeonId) == true
        ? goldSilverHorn.EnterAsync(entry, cancellationToken)
        : sealedTreasure?.Supports(entry.DungeonId) == true
        ? sealedTreasure.EnterAsync(entry, cancellationToken)
        : southern?.Supports(entry.DungeonId) == true
        ? southern.EnterAsync(entry, cancellationToken)
        : heras?.Supports(entry.DungeonId) == true
        ? heras.EnterAsync(entry, cancellationToken)
        : tau?.Supports(entry.DungeonId) == true
        ? tau.EnterAsync(entry, cancellationToken)
        : altars?.Supports(entry.DungeonId) == true
        ? altars.EnterAsync(entry, cancellationToken)
        : tournaments?.Supports(entry.DungeonId) == true
        ? tournaments.EnterAsync(entry, cancellationToken)
        : sao?.Supports(entry.DungeonId) == true
        ? sao.EnterAsync(entry, cancellationToken)
        : despair?.IsTower(entry.DungeonId) == true
        ? despair.EnterAsync(entry, cancellationToken)
        : entry.MirrorQuestId is not null
        ? mirrors?.EnterAsync(entry, cancellationToken)
            ?? ValueTask.FromResult(DungeonEntryAttempt.Refused(DungeonEntryFailure.MissingPrerequisite))
        : entry.Facts.Intent.Kind == DungeonEntryIntentKind.Cycle
        ? cycles?.EnterAsync(entry, cancellationToken)
            ?? ValueTask.FromResult(DungeonEntryAttempt.Refused(DungeonEntryFailure.MissingPrerequisite))
        : decisions.ResolveEntryKind(entry) switch
        {
            DungeonEntryKind.Normal when dimensions is not null => dimensions.EnterAsync(entry, cancellationToken),
            DungeonEntryKind.Normal => visits.TryEnterPartyAsync(entry.Participants, entry.DungeonId,
                entry.Facts, cancellationToken),
            DungeonEntryKind.ManualHell => hell?.EnterManualAsync(entry, cancellationToken)
                ?? ValueTask.FromResult(DungeonEntryAttempt.Refused(DungeonEntryFailure.HellUnavailable)),
            DungeonEntryKind.RandomHell => hell?.EnterRandomAsync(entry, cancellationToken)
                ?? ValueTask.FromResult(DungeonEntryAttempt.Refused(DungeonEntryFailure.HellUnavailable)),
            _ => throw new InvalidOperationException("Unknown dungeon entry kind."),
        };
    protected virtual NetworkConnectionContext? CaptureEntryConnection(SelectedSessionIdentity session) =>
        onlineUsers?.TryGetByConnection(session.ConnectionId, out var user) == true && user is not null
            && user.AccountId == session.AccountId && user.CurrentCharacterId == session.CharacterId ? user.Connection : null;
    protected virtual ValueTask CloseEntryAsync(SelectedSessionIdentity session, NetworkConnectionContext? captured) =>
        captured is not null ? entryClosures.RequestCloseAsync(captured, CancellationToken.None) : ValueTask.CompletedTask;
    protected virtual ValueTask<Dfo.Server.Abstractions.World.BossTower.BossTowerEntryResult> AdmitBossAsync(CommandExecutionContext execution, IReadOnlyList<TowerEntryMember> members, SelectedSessionIdentity requester, uint seed, CancellationToken token, byte requestMode, byte difficulty, byte practiceMode) =>
        workUnits!.WriteAsync(execution, new("boss-tower-entry", members.Select(value => value.Session == requester ? CaptureParticipant(value.Session) : new SelectedSessionRecipient(value.Session, null)).ToArray()),
            (_, ct) => bossTowers.EnterAsync(members, requester, seed, ct, requestMode, difficulty, practiceMode), token);
    protected virtual ValueTask<TowerEntryResult> AdmitIllusionAsync(CommandExecutionContext execution, IReadOnlyList<TowerEntryMember> members, SelectedSessionIdentity requester, uint seed, CancellationToken token, int dungeonId) =>
        workUnits!.WriteAsync(execution, new("illusion-tower-entry", []), (_, ct) => illusionTowers.EnterAsync(members, requester, seed, ct, dungeonId), token);
}
