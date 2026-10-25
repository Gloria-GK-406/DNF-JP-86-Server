using System.Collections.Concurrent;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.CharacterDeath;
using Dfo.Server.Protocol.Game.DeleteItem;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.FinishLoading;
using Dfo.Server.Protocol.Game.IllusionTowerContinue;
using Dfo.Server.Protocol.Game.GroundPickup;
using Dfo.Server.Protocol.Game.IllusionTowerInfo;
using Dfo.Server.Protocol.Game.IllusionTowerMap;
using Dfo.Server.Protocol.Game.IllusionTowerRanking;
using Dfo.Server.Protocol.Game.IllusionTowerReward;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game;

public sealed class IllusionTowerProjectionDelivery(
    IIllusionTowerDomainService towers,
    ICommandBus commands,
    ISelectedSessionDomainService sessions,
    ISelectedCharacterCurrentViewDomainService currentViews,
    ICharacterStateProjectionDomainService currentState,
    ICharacterDomainService characters,
    IOnlineCharacterRuntimeDomainService onlineCharacters,
    IGameMessageSender sender,
    TownAreaProjectionDelivery townProjection,
    TimeProvider clock,
    INetworkConnectionCloser connections,
    ILogger<IllusionTowerProjectionDelivery> logger)
{
    private ValueTask SendCommittedBatchAsync(IllusionTowerSnapshot run, SelectedSessionIdentity member,
        GameExplicitMessageDelivery batch, CancellationToken token) => PartyStages.SendCommittedBatchAsync(run, member, batch, token);

    private IllusionTowerPartyCommandStages PartyStages => new(towers, sessions, currentViews, currentState, characters,
        onlineCharacters, clock, sender.SendExplicitAsync,
        session => connections.RequestCloseAsync(session.ConnectionId, CancellationToken.None), logger, townProjection.EnterAsync);

    private static readonly IInventorySlotMappingRuleService SlotMappings = new InventorySlotMappingRuleService();

    public async ValueTask<bool> DieAsync(NetworkConnectionId connectionId,
        GameMessageMetadata metadata, CancellationToken token)
    {
        if (Stale(metadata)) return true;
        var session = sessions.FindByConnection(connectionId);
        var expected = session is null ? null : towers.Find(session);
        if (session is null || expected is null) return false;
        IReadOnlyList<(SelectedSessionIdentity Session, Exception Error)> failures = [];
        if (!Exact(session, expected)) return true;
        var action = await commands.SendAsync(
            new DieInIllusionTowerCommand(Context(expected, session)), token).ConfigureAwait(false);
        if (!action.Changed || action.Run is null) return true;
        failures = await ProjectActionAsync(
            action, new CharacterLifeStateNotification(session.WireUserId), token,
            requester: session, requesterResponse: new CharacterDeathResponse()).ConfigureAwait(false);
        await CloseProjectionFailuresAsync(failures, expected.RunId).ConfigureAwait(false);
        return true;
    }

    public async ValueTask<bool> SelectReturnAsync(NetworkConnectionId connectionId,
        GameMessageMetadata metadata, byte state, byte option, CancellationToken token)
    {
        if (Stale(metadata)) return true;
        var session = sessions.FindByConnection(connectionId);
        var expected = session is null ? null : towers.Find(session);
        if (session is null || expected is null) return false;
        if (state != EplpCommandHandler.LeaveState) return true;
        Exception? failure = null;
        if (!Exact(session, expected)) return true;
        var action = await commands.SendAsync(
            new SelectIllusionTowerReturnCommand(Context(expected, session), option), token).ConfigureAwait(false);
        if (action.Changed)
        {
            try
            {
                await sender.SendExplicitAsync(session.ConnectionId,
                    new GameExplicitMessageDelivery([new EplpCommandResponse(state, option)], []), token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                failure = error;
            }
        }
        if (failure is not null)
            await CloseProjectionFailuresAsync([(session, failure)], expected.RunId).ConfigureAwait(false);
        return true;
    }

    public async ValueTask<bool> LeaveAndReturnAsync(NetworkConnectionId connectionId,
        GameMessageMetadata metadata, IGameServerMessage? response, CancellationToken token)
    {
        if (Stale(metadata)) return true;
        var session = sessions.FindByConnection(connectionId);
        var expected = session is null ? null : towers.Find(session);
        if (session is null || expected is null) return false;
        TowerActionResult? committed = null;
        if (!Exact(session, expected)) return true;
        var action = await commands.SendAsync(
            new LeaveIllusionTowerCommand(Context(expected, session), Disconnected: false), token).ConfigureAwait(false);
        if (action.Changed)
            committed = action;
        if (committed is not null)
            await ProjectCommittedDepartureAsync(committed, session, response, token).ConfigureAwait(false);
        return true;
    }

    public async ValueTask SweepAsync(DateTimeOffset now, CancellationToken token)
    {
        foreach (var context in towers.FindReturnsDue(now))
        {
            IReadOnlyList<(SelectedSessionIdentity Session, Exception Error)> failures;
            try
            {
                failures = await SweepOneAsync(context, now, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                logger.LogWarning(error,
                    "Illusion tower return sweep failed for run {RunId}; continuing with other due runs",
                    context.RunId);
                continue;
            }

            // Transport close may synchronously enter selected-session cleanup, so it runs only
            // after SweepOneAsync has finished with the run.
            foreach (var (session, error) in failures)
            {
                logger.LogWarning(error,
                    "Illusion tower return projection failed for exact session {ConnectionId} in run {RunId}",
                    session.ConnectionId, context.RunId);
                await CloseQuietlyAsync(session).ConfigureAwait(false);
            }
        }
    }

    public ValueTask ProjectDisconnectedActionAsync(TowerActionResult action, CancellationToken token) =>
        ProjectCommittedDepartureAsync(action, null, null, token);

    public ValueTask ProjectCommittedDepartureAsync(TowerActionResult action, SelectedSessionIdentity? returning,
        IGameServerMessage? response, CancellationToken token) => PartyStages.ProjectCommittedDepartureAsync(action, returning, response, token);

    public ValueTask ProjectCurrentHostAsync(IllusionTowerSnapshot captured, CancellationToken token) => PartyStages.ProjectCurrentHostAsync(captured, token);

    public async ValueTask ReprojectCompletionAsync(TowerActionContext context, CancellationToken token)
    {
        var expected = towers.Find(context.Session);
        if (expected is null || expected.RunId != context.RunId || expected.Room.Version != context.RoomVersion
            || expected.Completion is not { Settled: true, Receipt: not null }) return;
        var failures = new List<(SelectedSessionIdentity, Exception)>();
        failures.AddRange(await ProjectActionAsync(
            new TowerActionResult(false, expected) { SettlementCreated = true }, null, token)
            .ConfigureAwait(false));
        await CloseProjectionFailuresAsync(failures, expected.RunId).ConfigureAwait(false);
    }

    private async ValueTask<IReadOnlyList<(SelectedSessionIdentity Session, Exception Error)>> SweepOneAsync(
        TowerActionContext context, DateTimeOffset now, CancellationToken token)
    {
        var failures = new List<(SelectedSessionIdentity, Exception)>();
        var expected = towers.Find(context.Session);
        if (expected is null || expected.RunId != context.RunId || expected.Room.Version != context.RoomVersion
            || expected.ReturnDueAt is not { } due || due > now) return failures;
        var frozen = ExactMembers(expected);
        if (frozen.Count == 0) return failures;
        foreach (var member in frozen)
            _ = await commands.SendAsync(
                new LeaveIllusionTowerCommand(Context(expected, member), Disconnected: false), token).ConfigureAwait(false);
        IGameServerMessage? response = expected.ReturnOption is { } option
            ? new EplpCommandResponse(EplpCommandHandler.LeaveState, option)
            : null;
        await ReturnMembersIsolatedAsync(frozen, response, failures, token, expected.DungeonId == 11000).ConfigureAwait(false);
        return failures;
    }

    private ValueTask<IReadOnlyList<(SelectedSessionIdentity Session, Exception Error)>> ProjectActionAsync(
        TowerActionResult action, IGameServerMessage? shared, CancellationToken token,
        SelectedSessionIdentity? requester = null, IGameServerMessage? requesterResponse = null) =>
        PartyStages.ProjectActionAsync(action, shared, token, requester, requesterResponse);





    private ValueTask AppendExperienceAsync(List<IGameServerMessage> frames, SelectedSessionIdentity session,
        ExperienceAward award, CancellationToken token) => PartyStages.AppendExperienceAsync(frames, session, award, token);

    private static void AppendInventory(List<IGameServerMessage> frames, IReadOnlyList<TowerInventoryUpdate> updates, DateTimeOffset at) =>
        IllusionTowerPartyCommandStages.AppendInventory(frames, updates, at);

    private static void AppendQuest(List<IGameServerMessage> frames, Dfo.Server.Abstractions.Quests.QuestProgressSnapshot? progress) =>
        IllusionTowerPartyCommandStages.AppendQuest(frames, progress);

    private async ValueTask ReturnMembersIsolatedAsync(IReadOnlyList<SelectedSessionIdentity> returning,
        IGameServerMessage? response, List<(SelectedSessionIdentity, Exception)> failures, CancellationToken token, bool restoreInventory)
    {
        var entries = await PartyStages.CaptureReturnEntriesAsync(returning, response, failures, token, restoreInventory).ConfigureAwait(false);
        if (entries.Count == 0) return;
        try
        {
            await townProjection.EnterAsync(entries, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            foreach (var entry in entries)
                failures.Add((entry.Session, error));
        }
    }

    private bool Exact(SelectedSessionIdentity session, IllusionTowerSnapshot expected) => PartyStages.Exact(session, expected);

    private IReadOnlyList<SelectedSessionIdentity> ExactMembers(IllusionTowerSnapshot run) => PartyStages.ExactMembers(run);

    private IllusionTowerSnapshot? CurrentRun(IllusionTowerSnapshot run) => PartyStages.CurrentRun(run);

    private ValueTask<IReadOnlyList<(SelectedSessionIdentity Session, Exception Error)>> SendToExactAsync(IllusionTowerSnapshot run,
        Func<SelectedSessionIdentity, IReadOnlyList<IGameServerMessage>> frames, CancellationToken token) => PartyStages.SendToExactAsync(run, frames, token);

    private static TowerActionContext Context(IllusionTowerSnapshot run, SelectedSessionIdentity session) =>
        IllusionTowerPartyCommandStages.Context(run, session);

    public static bool Stale(GameMessageMetadata metadata) =>
        metadata.InboundOrdinal is > 0 && !metadata.SequenceIsFresh;

    private ValueTask CloseQuietlyAsync(SelectedSessionIdentity session) => PartyStages.CloseQuietlyAsync(session);

    private ValueTask CloseProjectionFailuresAsync(IReadOnlyList<(SelectedSessionIdentity Session, Exception Error)> failures, Guid runId) =>
        PartyStages.CloseProjectionFailuresAsync(failures, runId);
}

public sealed class IllusionTowerDepartureProjectionQueue
{
    private readonly ConcurrentQueue<TowerActionResult> pending = new();

    public void Enqueue(TowerActionResult action)
    {
        GameplayWriteAssertion.Writing(nameof(IllusionTowerDepartureProjectionQueue) + "." + nameof(Enqueue));
        pending.Enqueue(action);
    }

    internal bool TryDequeue(out TowerActionResult? action)
    {
        GameplayWriteAssertion.Writing(nameof(IllusionTowerDepartureProjectionQueue) + "." + nameof(TryDequeue));
        return pending.TryDequeue(out action);
    }
}

public sealed class IllusionTowerReturnSweepService(
    IllusionTowerProjectionDelivery delivery,
    IllusionTowerDepartureProjectionQueue departures,
    TimeProvider clock,
    ILogger<IllusionTowerReturnSweepService> logger,
    IGameplayQueue queue) : BackgroundService
{
    private const string Command = "timer:IllusionTowerReturnSweepService";
    private readonly CoalescingGameplaySource ticks = new(queue);

    /// <summary>One tick as a timer CMD: the departure queue first, then the returns due now.</summary>
    public Task TickAsync(CancellationToken token) => GameplayTimerWork.RunAsync(queue, Command, TickCoreAsync, token);

    public ValueTask SweepAsync(DateTimeOffset now, CancellationToken token) => delivery.SweepAsync(now, token);

    public async ValueTask DrainDeparturesAsync(CancellationToken token)
    {
        while (departures.TryDequeue(out var action))
        {
            try
            {
                await delivery.ProjectDisconnectedActionAsync(action!, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                logger.LogWarning(error,
                    "Illusion tower disconnected projection failed for run {RunId}; continuing with queued projections",
                    action?.Run?.RunId);
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await GameplayTimerWork.TickAsync(ticks, Command, TickCoreAsync, logger, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                // A failed sweep CMD is absorbed by TickAsync; what reaches here is not one (F-30).
                logger.LogError(error,
                    "Unexpected illusion tower projection sweep failure; the hosted service will continue");
            }
        }
    }

    private async ValueTask TickCoreAsync(CancellationToken token)
    {
        await DrainDeparturesAsync(token).ConfigureAwait(false);
        await delivery.SweepAsync(clock.GetUtcNow(), token).ConfigureAwait(false);
    }
}

public sealed class IllusionTowerDepartureProjectionSink(IllusionTowerDepartureProjectionQueue queue)
    : IIllusionTowerDepartureSink
{
    public ValueTask DepartedAsync(TowerActionResult action, CancellationToken token)
    {
        queue.Enqueue(action);
        return ValueTask.CompletedTask;
    }
}
