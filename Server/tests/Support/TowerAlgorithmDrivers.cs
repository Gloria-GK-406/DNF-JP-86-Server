using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.TownProjection;
using Microsoft.Extensions.Logging;
namespace Dfo.Server.Testing;

// Component fixtures use the production shared algorithms. Source/transaction behavior is tested
// separately through the registered internal entries; this driver does not impersonate a bus.
internal interface ITowerAlgorithmMutations
{
    ValueTask<BossTowerActionResult> BossLeave(BossTowerActionContext context) => throw new NotSupportedException();
    ValueTask<BossTowerActionResult> BossSynchronize(BossTowerActionContext context, SelectedSessionIdentity leader) => throw new NotSupportedException();
    ValueTask<BossTowerActionResult> BossStart(BossTowerActionContext context) => throw new NotSupportedException();
    ValueTask<TowerActionResult> IllusionLeave(TowerActionContext context) => throw new NotSupportedException();
    ValueTask<TowerActionResult> IllusionSynchronize(TowerActionContext context, SelectedSessionIdentity leader) => throw new NotSupportedException();
}
internal abstract class TowerAlgorithmDriver : PartyMutationProtocolCommandHandler
{
    protected readonly ITowerAlgorithmMutations? mutations;
    private readonly TownAreaProjectionDelivery town;
    protected TowerAlgorithmDriver(ISelectedSessionDomainService sessions, IGameMessageSender sender,
        TownAreaProjectionDelivery town, TimeProvider clock, INetworkConnectionCloser closer, ILogger logger,
        ISelectedCharacterCurrentViewDomainService views, ICharacterStateProjectionDomainService state,
        ICharacterDomainService characters, IOnlineCharacterRuntimeDomainService positions,
        IBossTowerDomainService? boss = null, IIllusionTowerDomainService? illusion = null,
        ITowerAlgorithmMutations? mutations = null, IPartyService? parties = null, ITownAreaDomainService? areas = null,
        IDungeonRunDomainService? runs = null)
        : base(sessions, parties, areas, null!, null!, null, logger, views, state, characters, positions,
            bossTowers: boss, illusionTowers: illusion, clock: clock, register: sender is null ? null : sender.SendExplicitAsync,
            close: session => closer.RequestCloseAsync(session.ConnectionId, CancellationToken.None), dungeonRuns: runs)
    { this.mutations = mutations; this.town = town; }
    protected override ValueTask<int> ReturnTownAsync(IReadOnlyList<TownAreaEntry> entries, CancellationToken token) => town.EnterAsync(entries, token);
    protected override ValueTask<BossTowerActionResult> LeaveBossPartyTowerAsync(CommandExecutionContext? execution, BossTowerActionContext context, IReadOnlyList<SelectedSessionIdentity> owners, CancellationToken token) => mutations!.BossLeave(context);
    protected override ValueTask<BossTowerActionResult> SynchronizeBossTowerPartyTowerAsync(CommandExecutionContext? execution, BossTowerActionContext context, SelectedSessionIdentity leader, IReadOnlyList<SelectedSessionIdentity> owners, CancellationToken token) => mutations!.BossSynchronize(context, leader);
    protected override ValueTask<BossTowerActionResult> StartBossPartyTowerAsync(CommandExecutionContext? execution, BossTowerSnapshot run, SelectedSessionIdentity leader, CancellationToken token) => mutations!.BossStart(BossTowerContext(run, leader));
    protected override ValueTask<TowerActionResult> LeaveIllusionPartyTowerAsync(CommandExecutionContext? execution, TowerActionContext context, IReadOnlyList<SelectedSessionIdentity> owners, CancellationToken token) => mutations!.IllusionLeave(context);
    protected override ValueTask<TowerActionResult> SynchronizeIllusionTowerPartyTowerAsync(CommandExecutionContext? execution, TowerActionContext context, SelectedSessionIdentity leader, IReadOnlyList<SelectedSessionIdentity> owners, CancellationToken token) => mutations!.IllusionSynchronize(context, leader);
}

internal sealed class BossTowerProjectionDriver(IBossTowerDomainService towers, ITowerAlgorithmMutations? mutations,
    ISelectedSessionDomainService selected, ISelectedCharacterCurrentViewDomainService views,
    ICharacterStateProjectionDomainService state, ICharacterDomainService characters, IOnlineCharacterRuntimeDomainService positions,
    IGameMessageSender sender, TownAreaProjectionDelivery town, TimeProvider clock, INetworkConnectionCloser closer, ILogger logger)
    : TowerAlgorithmDriver(selected, sender, town, clock, closer, logger, views, state, characters, positions, boss: towers, mutations: mutations)
{
    public bool HasRun(SelectedSessionIdentity session) => bossTowers.Find(session) is not null;
    public ValueTask ProjectCommittedDepartureAsync(BossTowerActionResult action, SelectedSessionIdentity? returning, IGameServerMessage? response, CancellationToken token) => ProjectBossTowerPartyDepartureAsync(null, action, returning, response, token);
    public ValueTask ProjectDisconnectedActionAsync(BossTowerActionResult action, CancellationToken token) => ProjectCommittedDepartureAsync(action, null, null, token);
    public ValueTask ProjectCurrentHostAsync(BossTowerSnapshot run, CancellationToken token) => ProjectBossTowerPartyHostAsync(run, token);
    public async ValueTask ReprojectCompletionAsync(BossTowerActionContext context, CancellationToken token)
    {
        var run = bossTowers.Find(context.Session);
        if (run is null || run.RunId != context.RunId || run.Room.Version != context.RoomVersion || run.Completion is not { Settled: true, Receipt: not null }) return;
        var failures = await BossTowerProjectActionAsync(new(false, run) { SettlementCreated = true }, null, token);
        await BossTowerCloseProjectionFailuresAsync(failures, run.RunId);
    }
    public async ValueTask<bool> LeaveAndReturnAsync(NetworkConnectionId id, GameMessageMetadata metadata, IGameServerMessage? response, CancellationToken token)
    {
        if (TowerProtocol.IsStale(metadata)) return true;
        var session = sessions.FindByConnection(id); var run = session is null ? null : bossTowers.Find(session);
        if (session is null || run is null) return false;
        if (!BossTowerExact(session, run)) return true;
        var action = await this.mutations!.BossLeave(new(session, run.RunId, run.Room.Version));
        if (action.Changed) await ProjectCommittedDepartureAsync(action, session, response, token);
        return true;
    }
    public async ValueTask SweepAsync(DateTimeOffset at, CancellationToken token)
    {
        foreach (var context in bossTowers.FindReturnsDue(at))
        {
            try { await ReturnBossTowerRunAsync(null, new(context, at), token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error) { this.logger.LogWarning(error, "Component sweep failed; continuing"); }
        }
    }
}
internal sealed class BossTowerPartyDriver(IBossTowerDomainService towers, IPartyService parties, ISelectedSessionDomainService sessions,
    ITowerAlgorithmMutations mutations, PartyNotificationDelivery notifications, BossTowerProjectionDriver projection,
    INetworkConnectionCloser closer, ITownAreaDomainService areas, ILogger logger, IDungeonRunDomainService? runs = null)
    : TowerAlgorithmDriver(sessions, null!, null!, TimeProvider.System, closer, logger, null!, null!, null!, null!,
        boss: towers, mutations: mutations, parties: parties, areas: areas, runs: runs)
{
    public ValueTask<BossTowerPartyAttempt> TryLeaveAsync(SelectedSessionIdentity caller, bool allowVirtual, IGameServerMessage? departureResponse, CancellationToken token) => CoordinateBossTowerPartyAsync(null, PartyChange.Leave, caller, null, allowVirtual, departureResponse, token);
    public ValueTask<BossTowerPartyAttempt> TryRemoveAsync(SelectedSessionIdentity caller, byte targetSlot, CancellationToken token) => CoordinateBossTowerPartyAsync(null, PartyChange.Remove, caller, targetSlot, false, null, token);
    public ValueTask<BossTowerPartyAttempt> TryTransferAsync(SelectedSessionIdentity caller, byte targetSlot, CancellationToken token) => CoordinateBossTowerPartyAsync(null, PartyChange.Transfer, caller, targetSlot, false, null, token);
    protected override ValueTask ProjectPartyAsync(Dfo.Server.Data.Parties.PartyNotificationPlan plan, PartyNotificationRecipientPlan recipients, CancellationToken token) => notifications.DeliverAsync(plan, recipients, token);
    protected override ValueTask ProjectBossTowerPartyDepartureAsync(CommandExecutionContext? execution, BossTowerActionResult action, SelectedSessionIdentity? returning, IGameServerMessage? response, CancellationToken token) => projection.ProjectCommittedDepartureAsync(action, returning, response, token);
    protected override ValueTask ProjectBossTowerPartyHostAsync(BossTowerSnapshot run, CancellationToken token) => projection.ProjectCurrentHostAsync(run, token);
}

internal sealed class IllusionTowerProjectionDriver(IIllusionTowerDomainService towers, ITowerAlgorithmMutations? mutations,
    ISelectedSessionDomainService selected, ISelectedCharacterCurrentViewDomainService views,
    ICharacterStateProjectionDomainService state, ICharacterDomainService characters, IOnlineCharacterRuntimeDomainService positions,
    IGameMessageSender sender, TownAreaProjectionDelivery town, TimeProvider clock, INetworkConnectionCloser closer, ILogger logger)
    : TowerAlgorithmDriver(selected, sender, town, clock, closer, logger, views, state, characters, positions, illusion: towers, mutations: mutations)
{
    public bool HasRun(SelectedSessionIdentity session) => illusionTowers.Find(session) is not null;
    public ValueTask ProjectCommittedDepartureAsync(TowerActionResult action, SelectedSessionIdentity? returning, IGameServerMessage? response, CancellationToken token) => ProjectIllusionTowerPartyDepartureAsync(null, action, returning, response, token);
    public ValueTask ProjectDisconnectedActionAsync(TowerActionResult action, CancellationToken token) => ProjectCommittedDepartureAsync(action, null, null, token);
    public ValueTask ProjectCurrentHostAsync(IllusionTowerSnapshot run, CancellationToken token) => ProjectIllusionTowerPartyHostAsync(run, token);
    public async ValueTask ReprojectCompletionAsync(TowerActionContext context, CancellationToken token)
    {
        var run = illusionTowers.Find(context.Session);
        if (run is null || run.RunId != context.RunId || run.Room.Version != context.RoomVersion || run.Completion is not { Settled: true, Receipt: not null }) return;
        var failures = await IllusionTowerProjectActionAsync(new(false, run) { SettlementCreated = true }, null, token);
        await IllusionTowerCloseProjectionFailuresAsync(failures, run.RunId);
    }
    public async ValueTask<bool> LeaveAndReturnAsync(NetworkConnectionId id, GameMessageMetadata metadata, IGameServerMessage? response, CancellationToken token)
    {
        if (TowerProtocol.IsStale(metadata)) return true;
        var session = sessions.FindByConnection(id); var run = session is null ? null : illusionTowers.Find(session);
        if (session is null || run is null) return false;
        if (!IllusionTowerExact(session, run)) return true;
        var action = await this.mutations!.IllusionLeave(new(session, run.RunId, run.Room.Version));
        if (action.Changed) await ProjectCommittedDepartureAsync(action, session, response, token);
        return true;
    }
    public async ValueTask SweepAsync(DateTimeOffset at, CancellationToken token)
    {
        foreach (var context in illusionTowers.FindReturnsDue(at))
        {
            try { await ReturnIllusionTowerRunAsync(null, new(context, at), token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error) { this.logger.LogWarning(error, "Component sweep failed; continuing"); }
        }
    }
}
internal sealed class IllusionTowerPartyDriver(IIllusionTowerDomainService towers, IPartyService parties, ISelectedSessionDomainService sessions,
    ITowerAlgorithmMutations mutations, PartyNotificationDelivery notifications, IllusionTowerProjectionDriver projection,
    INetworkConnectionCloser closer, ITownAreaDomainService areas, ILogger logger, IDungeonRunDomainService? runs = null)
    : TowerAlgorithmDriver(sessions, null!, null!, TimeProvider.System, closer, logger, null!, null!, null!, null!,
        illusion: towers, mutations: mutations, parties: parties, areas: areas, runs: runs)
{
    public ValueTask<IllusionTowerPartyAttempt> TryLeaveAsync(SelectedSessionIdentity caller, bool allowVirtual, IGameServerMessage? departureResponse, CancellationToken token) => CoordinateIllusionTowerPartyAsync(null, PartyChange.Leave, caller, null, allowVirtual, departureResponse, token);
    public ValueTask<IllusionTowerPartyAttempt> TryRemoveAsync(SelectedSessionIdentity caller, byte targetSlot, CancellationToken token) => CoordinateIllusionTowerPartyAsync(null, PartyChange.Remove, caller, targetSlot, false, null, token);
    public ValueTask<IllusionTowerPartyAttempt> TryTransferAsync(SelectedSessionIdentity caller, byte targetSlot, CancellationToken token) => CoordinateIllusionTowerPartyAsync(null, PartyChange.Transfer, caller, targetSlot, false, null, token);
    protected override ValueTask ProjectPartyAsync(Dfo.Server.Data.Parties.PartyNotificationPlan plan, PartyNotificationRecipientPlan recipients, CancellationToken token) => notifications.DeliverAsync(plan, recipients, token);
    protected override ValueTask ProjectIllusionTowerPartyDepartureAsync(CommandExecutionContext? execution, TowerActionResult action, SelectedSessionIdentity? returning, IGameServerMessage? response, CancellationToken token) => projection.ProjectCommittedDepartureAsync(action, returning, response, token);
    protected override ValueTask ProjectIllusionTowerPartyHostAsync(IllusionTowerSnapshot run, CancellationToken token) => projection.ProjectCurrentHostAsync(run, token);
}
