using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Protocol.Game.DungeonClear;
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
internal abstract class TowerAlgorithmDriver
{
    protected readonly ISelectedSessionDomainService sessions;
    protected readonly IBossTowerDomainService bossTowers;
    protected readonly IIllusionTowerDomainService illusionTowers;
    protected readonly ILogger logger;
    protected readonly ITowerAlgorithmMutations? mutations;
    protected readonly GameDungeonExitCommandHandler exit;
    protected readonly TowerReturnCommandHandler returns;
    internal readonly object[] Dependencies;
    protected CommandExecutionContext Execution => new(Guid.NewGuid(), typeof(GameGiveUpGameCommand), typeof(GameDungeonExitCommandHandler))
    { Request = new CommandRequestContext.TrustedInternal(new(Dfo.Server.Abstractions.Execution.GameplayCallSource.Timer,
        Dfo.Server.Abstractions.Execution.GameplayCallChain.NewRoot(Dfo.Server.Abstractions.Execution.GameplayCallSource.Timer), "component: tower")) };
    protected TowerAlgorithmDriver(ISelectedSessionDomainService sessions, IGameMessageSender sender,
        TownAreaProjectionDelivery town, TimeProvider clock, INetworkConnectionCloser closer, ILogger logger,
        ISelectedCharacterCurrentViewDomainService views, ICharacterStateProjectionDomainService state,
        ICharacterDomainService characters, IOnlineCharacterRuntimeDomainService positions,
        IBossTowerDomainService? boss = null, IIllusionTowerDomainService? illusion = null,
        ITowerAlgorithmMutations? mutations = null, IPartyService? parties = null, ITownAreaDomainService? areas = null,
        IDungeonRunDomainService? runs = null, TowerAlgorithmDriver? projectionSource = null, PartyNotificationDelivery? notifications = null)
    {
        this.sessions = sessions; this.logger = logger; this.mutations = mutations;
        var supplied = (projectionSource?.Dependencies ?? (town is null ? [] : ProtocolAlgorithmFixture.TownDependencies(town))).ToList();
        supplied.AddRange(new object?[] { sessions, sender, clock, closer, views, state, characters, positions, parties, areas, runs }.OfType<object>());
        if (notifications is not null) supplied.Add(ProtocolAlgorithmFixture.Field<Dfo.Server.Abstractions.DomainEvents.IEventService>(notifications));
        bossTowers = boss is null ? (IBossTowerDomainService)ProtocolAlgorithmFixture.Default(typeof(IBossTowerDomainService))
            : mutations is null ? boss : ProtocolAlgorithmFixture.Decorate(boss, (method, args) => method.Name switch
            {
                nameof(IBossTowerDomainService.LeaveAsync) => (true, mutations.BossLeave((BossTowerActionContext)args[0]!)),
                nameof(IBossTowerDomainService.SynchronizeLeaderAsync) => (true, mutations.BossSynchronize((BossTowerActionContext)args[0]!, (SelectedSessionIdentity)args[1]!)),
                nameof(IBossTowerDomainService.StartAsync) => (true, mutations.BossStart((BossTowerActionContext)args[0]!)),
                _ => (false, null),
            });
        illusionTowers = illusion is null ? (IIllusionTowerDomainService)ProtocolAlgorithmFixture.Default(typeof(IIllusionTowerDomainService))
            : mutations is null ? illusion : ProtocolAlgorithmFixture.Decorate(illusion, (method, args) => method.Name switch
            {
                nameof(IIllusionTowerDomainService.LeaveAsync) => (true, mutations.IllusionLeave((TowerActionContext)args[0]!)),
                nameof(IIllusionTowerDomainService.SynchronizeLeaderAsync) => (true, mutations.IllusionSynchronize((TowerActionContext)args[0]!, (SelectedSessionIdentity)args[1]!)),
                _ => (false, null),
            });
        supplied.Add(bossTowers); supplied.Add(illusionTowers);
        Dependencies = ProtocolAlgorithmFixture.Complete(supplied.ToArray());
        exit = ProtocolAlgorithmFixture.Create<GameDungeonExitCommandHandler>(Dependencies);
        returns = ProtocolAlgorithmFixture.Create<TowerReturnCommandHandler>(Dependencies);
    }
}

internal sealed class BossTowerProjectionDriver(IBossTowerDomainService towers, ITowerAlgorithmMutations? mutations,
    ISelectedSessionDomainService selected, ISelectedCharacterCurrentViewDomainService views,
    ICharacterStateProjectionDomainService state, ICharacterDomainService characters, IOnlineCharacterRuntimeDomainService positions,
    IGameMessageSender sender, TownAreaProjectionDelivery town, TimeProvider clock, INetworkConnectionCloser closer, ILogger logger)
    : TowerAlgorithmDriver(selected, sender, town, clock, closer, logger, views, state, characters, positions, boss: towers, mutations: mutations)
{
    public bool HasRun(SelectedSessionIdentity session) => bossTowers.Find(session) is not null;
    public ValueTask ProjectCommittedDepartureAsync(BossTowerActionResult action, SelectedSessionIdentity? returning, IGameServerMessage? response, CancellationToken token) => exit.ProjectBossTowerPartyDepartureAsync(Execution, action, returning, response, token);
    public ValueTask ProjectDisconnectedActionAsync(BossTowerActionResult action, CancellationToken token) => ProjectCommittedDepartureAsync(action, null, null, token);
    public ValueTask ProjectCurrentHostAsync(BossTowerSnapshot run, CancellationToken token) => exit.ProjectBossTowerPartyHostAsync(run, token);
    public async ValueTask ReprojectCompletionAsync(BossTowerActionContext context, CancellationToken token)
    {
        var run = bossTowers.Find(context.Session);
        if (run is null || run.RunId != context.RunId || run.Room.Version != context.RoomVersion || run.Completion is not { Settled: true, Receipt: not null }) return;
        var failures = await BossTowerActionProjection.BossTowerProjectActionAsync(sessions, (ICharacterDomainService)Dependencies.LastOrDefault(value => value is ICharacterDomainService)!, bossTowers, Dependencies.OfType<TimeProvider>().Last(), (IGameCommandOutbox)Dependencies.LastOrDefault(value => value is IGameCommandOutbox)!, new(false, run) { SettlementCreated = true }, null, token);
        await BossTowerActionProjection.BossTowerCloseProjectionFailuresAsync(sessions, this.logger, (ICommandConnectionClosures)Dependencies.LastOrDefault(value => value is ICommandConnectionClosures)!, (IOnlineUserQueryDomainService)Dependencies.LastOrDefault(value => value is IOnlineUserQueryDomainService)!, failures, run.RunId);
    }
    public async ValueTask<bool> LeaveAndReturnAsync(NetworkConnectionId id, GameMessageMetadata metadata, IGameServerMessage? response, CancellationToken token)
    {
        if (TowerProtocol.IsStale(metadata)) return true;
        var session = sessions.FindByConnection(id); var run = session is null ? null : bossTowers.Find(session);
        if (session is null || run is null) return false;
        if (!BossTowerActionProjection.BossTowerExact(sessions, bossTowers, session, run)) return true;
        var action = await this.mutations!.BossLeave(new(session, run.RunId, run.Room.Version));
        if (action.Changed) await ProjectCommittedDepartureAsync(action, session, response, token);
        return true;
    }
    public async ValueTask SweepAsync(DateTimeOffset at, CancellationToken token)
    {
        foreach (var context in bossTowers.FindReturnsDue(at))
        {
            try { await returns.ReturnBossTowerRunAsync(Execution, new(context, at), token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error) { this.logger.LogWarning(error, "Component sweep failed; continuing"); }
        }
    }
}
internal sealed class BossTowerPartyDriver(IBossTowerDomainService towers, IPartyService parties, ISelectedSessionDomainService sessions,
    ITowerAlgorithmMutations mutations, PartyNotificationDelivery notifications, BossTowerProjectionDriver projection,
    INetworkConnectionCloser closer, ITownAreaDomainService areas, ILogger logger, IDungeonRunDomainService? runs = null)
    : TowerAlgorithmDriver(sessions, null!, null!, TimeProvider.System, closer, logger, null!, null!, null!, null!,
        boss: towers, mutations: mutations, parties: parties, areas: areas, runs: runs, projectionSource: projection, notifications: notifications)
{
    public ValueTask<BossTowerPartyAttempt> TryLeaveAsync(SelectedSessionIdentity caller, bool allowVirtual, IGameServerMessage? departureResponse, CancellationToken token) => exit.CoordinateBossTowerPartyAsync(Execution, PartyChange.Leave, caller, null, allowVirtual, departureResponse, token);
    public ValueTask<BossTowerPartyAttempt> TryRemoveAsync(SelectedSessionIdentity caller, byte targetSlot, CancellationToken token) => exit.CoordinateBossTowerPartyAsync(Execution, PartyChange.Remove, caller, targetSlot, false, null, token);
    public ValueTask<BossTowerPartyAttempt> TryTransferAsync(SelectedSessionIdentity caller, byte targetSlot, CancellationToken token) => exit.CoordinateBossTowerPartyAsync(Execution, PartyChange.Transfer, caller, targetSlot, false, null, token);
}

internal sealed class IllusionTowerProjectionDriver(IIllusionTowerDomainService towers, ITowerAlgorithmMutations? mutations,
    ISelectedSessionDomainService selected, ISelectedCharacterCurrentViewDomainService views,
    ICharacterStateProjectionDomainService state, ICharacterDomainService characters, IOnlineCharacterRuntimeDomainService positions,
    IGameMessageSender sender, TownAreaProjectionDelivery town, TimeProvider clock, INetworkConnectionCloser closer, ILogger logger)
    : TowerAlgorithmDriver(selected, sender, town, clock, closer, logger, views, state, characters, positions, illusion: towers, mutations: mutations)
{
    public bool HasRun(SelectedSessionIdentity session) => illusionTowers.Find(session) is not null;
    public ValueTask ProjectCommittedDepartureAsync(TowerActionResult action, SelectedSessionIdentity? returning, IGameServerMessage? response, CancellationToken token) => exit.ProjectIllusionTowerPartyDepartureAsync(Execution, action, returning, response, token);
    public ValueTask ProjectDisconnectedActionAsync(TowerActionResult action, CancellationToken token) => ProjectCommittedDepartureAsync(action, null, null, token);
    public ValueTask ProjectCurrentHostAsync(IllusionTowerSnapshot run, CancellationToken token) => exit.ProjectIllusionTowerPartyHostAsync(run, token);
    public async ValueTask ReprojectCompletionAsync(TowerActionContext context, CancellationToken token)
    {
        var run = illusionTowers.Find(context.Session);
        if (run is null || run.RunId != context.RunId || run.Room.Version != context.RoomVersion || run.Completion is not { Settled: true, Receipt: not null }) return;
        var failures = await IllusionTowerActionProjection.IllusionTowerProjectActionAsync(sessions, (ICharacterDomainService)Dependencies.LastOrDefault(value => value is ICharacterDomainService)!, illusionTowers, Dependencies.OfType<TimeProvider>().Last(), (IGameCommandOutbox)Dependencies.LastOrDefault(value => value is IGameCommandOutbox)!, new(false, run) { SettlementCreated = true }, null, token);
        await IllusionTowerActionProjection.IllusionTowerCloseProjectionFailuresAsync(sessions, this.logger, (ICommandConnectionClosures)Dependencies.LastOrDefault(value => value is ICommandConnectionClosures)!, (IOnlineUserQueryDomainService)Dependencies.LastOrDefault(value => value is IOnlineUserQueryDomainService)!, failures, run.RunId);
    }
    public async ValueTask<bool> LeaveAndReturnAsync(NetworkConnectionId id, GameMessageMetadata metadata, IGameServerMessage? response, CancellationToken token)
    {
        if (TowerProtocol.IsStale(metadata)) return true;
        var session = sessions.FindByConnection(id); var run = session is null ? null : illusionTowers.Find(session);
        if (session is null || run is null) return false;
        if (!IllusionTowerActionProjection.IllusionTowerExact(sessions, illusionTowers, session, run)) return true;
        var action = await this.mutations!.IllusionLeave(new(session, run.RunId, run.Room.Version));
        if (action.Changed) await ProjectCommittedDepartureAsync(action, session, response, token);
        return true;
    }
    public async ValueTask SweepAsync(DateTimeOffset at, CancellationToken token)
    {
        foreach (var context in illusionTowers.FindReturnsDue(at))
        {
            try { await returns.ReturnIllusionTowerRunAsync(Execution, new(context, at), token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error) { this.logger.LogWarning(error, "Component sweep failed; continuing"); }
        }
    }
}
internal sealed class IllusionTowerPartyDriver(IIllusionTowerDomainService towers, IPartyService parties, ISelectedSessionDomainService sessions,
    ITowerAlgorithmMutations mutations, PartyNotificationDelivery notifications, IllusionTowerProjectionDriver projection,
    INetworkConnectionCloser closer, ITownAreaDomainService areas, ILogger logger, IDungeonRunDomainService? runs = null)
    : TowerAlgorithmDriver(sessions, null!, null!, TimeProvider.System, closer, logger, null!, null!, null!, null!,
        illusion: towers, mutations: mutations, parties: parties, areas: areas, runs: runs, projectionSource: projection, notifications: notifications)
{
    public ValueTask<IllusionTowerPartyAttempt> TryLeaveAsync(SelectedSessionIdentity caller, bool allowVirtual, IGameServerMessage? departureResponse, CancellationToken token) => exit.CoordinateIllusionTowerPartyAsync(Execution, PartyChange.Leave, caller, null, allowVirtual, departureResponse, token);
    public ValueTask<IllusionTowerPartyAttempt> TryRemoveAsync(SelectedSessionIdentity caller, byte targetSlot, CancellationToken token) => exit.CoordinateIllusionTowerPartyAsync(Execution, PartyChange.Remove, caller, targetSlot, false, null, token);
    public ValueTask<IllusionTowerPartyAttempt> TryTransferAsync(SelectedSessionIdentity caller, byte targetSlot, CancellationToken token) => exit.CoordinateIllusionTowerPartyAsync(Execution, PartyChange.Transfer, caller, targetSlot, false, null, token);
}
