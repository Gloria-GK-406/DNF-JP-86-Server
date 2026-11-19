using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Raids;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Raids;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.AntonRaid;

internal sealed record MaintainAntonRaidCommand : ICommand<AntonRaidDeadlineScan>;
internal sealed record AdvanceAntonRaidCommand(AntonRaidDeadline Deadline) : ICommand<bool>;
internal sealed record AdvanceAntonRaidCombatCommand(AntonRaidCombatDeadline Deadline) : ICommand<bool>;
internal sealed record ReturnAntonRaidRunCommand(DungeonRunId Run) : ICommand<bool>;
internal sealed record ExpireAntonRaidRejoinCommand(AntonRaidRejoinExpiry Expiry) : ICommand<bool>;
internal sealed record RestoreAntonRaidSquadsCommand : ICommand<bool>;
internal sealed record CleanTerminalAntonRaidsCommand : ICommand<bool>;
internal sealed record DepartAntonRaidMemberCommand(AntonRaidMemberDeparture Departure) : ICommand<bool>;
internal sealed record AntonRaidDeadlineScan(DateTimeOffset? Next, IReadOnlyList<AntonRaidDeadline> Due,
    IReadOnlyList<AntonRaidCombatDeadline> Combat, IReadOnlyList<Guid> Returns, IReadOnlyList<AntonRaidRejoinExpiry> Expired,
    bool RestoreSquads, bool CleanTerminal, IReadOnlyList<AntonRaidMemberDeparture> Departures);

internal sealed class AntonRaidDeadlineHandler(IAntonRaidDomainService raids, ICommandWorkUnits units,
    IOnlineUserQueryDomainService users, AntonRaidSquadProjection squads, ICommandProjections projections,
    TimeProvider clock, ILogger<AntonRaidDeadlineHandler> logger,
    IDungeonRunDepartureDomainService runDeparture, IDungeonRunProjectionDomainService runProjection,
    DungeonTownReturnProjection townReturn,
    Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService sessions)
    : ICommandHandler<MaintainAntonRaidCommand, AntonRaidDeadlineScan>, ICommandHandler<AdvanceAntonRaidCommand, bool>,
      ICommandHandler<AdvanceAntonRaidCombatCommand, bool>, ICommandHandler<ReturnAntonRaidRunCommand, bool>,
      ICommandHandler<ExpireAntonRaidRejoinCommand, bool>, ICommandHandler<RestoreAntonRaidSquadsCommand, bool>,
      ICommandHandler<CleanTerminalAntonRaidsCommand, bool>, ICommandHandler<DepartAntonRaidMemberCommand, bool>
{
    public ValueTask<AntonRaidDeadlineScan> HandleAsync(CommandExecutionContext context,
        MaintainAntonRaidCommand command, CancellationToken ct)
    {
        var pending = raids.PreparationDeadlines();
        var combat = raids.CombatDeadlines();
        var returns = raids.PendingRunReturns();
        var rejoins = raids.RejoinExpirations();
        var now = clock.GetUtcNow();
        var next = pending.Select(deadline => (DateTimeOffset?)deadline.At)
            .Concat(combat.Select(deadline => (DateTimeOffset?)deadline.At))
            .Concat(rejoins.Select(expiry => (DateTimeOffset?)expiry.At)).Min();
        var restoreSquads = raids.HasSquadRestorations();
        var cleanTerminal = raids.HasTerminalRaidCleanup();
        var departures = raids.PendingMemberDepartures();
        if (returns.Count > 0 || restoreSquads || cleanTerminal || departures.Count > 0)
            next = now;
        if (next <= now)
            next = now.AddMilliseconds(100);
        return ValueTask.FromResult(new AntonRaidDeadlineScan(next, pending.Where(deadline => deadline.At <= now).ToArray(),
            combat.Where(deadline => deadline.At <= now).OrderBy(deadline => deadline.At).ThenBy(deadline => deadline.Generation).ToArray(),
            returns, rejoins.Where(expiry => expiry.At <= now).ToArray(), restoreSquads, cleanTerminal, departures));
    }

    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, AdvanceAntonRaidCombatCommand command, CancellationToken ct)
    {
        var cohort = raids.CombatParticipants(command.Deadline);
        var capture = TimerRecipientCapture.Capture(cohort, users);
        if (command.Deadline.Kind != AntonRaidCombatDeadlineKind.Intermission)
            return await units.WriteAsync(context, new("anton-raid.combat-deadline", capture),
                (_, token) => raids.AdvanceCombatAsync(command.Deadline, token), ct).ConfigureAwait(false);

        var start = await units.WriteAsync(context, new("anton-raid.intermission", capture),
            (_, token) => raids.FinishIntermissionAsync(command.Deadline, token), ct).ConfigureAwait(false);
        if (start is not { Change: { } change, Deadline: { } until })
        {
            if (start.Tickets is { } tickets)
                await AntonRaidRuntimeProjection.SendAsync(projections, cohort, [AntonRaidRuntimeNotice.Tickets(tickets)], ct)
                    .ConfigureAwait(false);
            return false;
        }
        await AntonRaidRecruitmentProjection.DeliverAsync(projections, change, ct).ConfigureAwait(false);
        await AntonRaidRuntimeProjection.SendAsync(projections, cohort,
            [.. AntonRaidRuntimeNotice.Timer(until, clock.GetUtcNow()),
                new AntonRaidRuntimeNotice(ServerMessageType.GameAntonRaidPrepare, ReadOnlyMemory<byte>.Empty)], ct).ConfigureAwait(false);
        return true;
    }

    public async ValueTask<bool> HandleAsync(CommandExecutionContext context,
        ReturnAntonRaidRunCommand command, CancellationToken ct)
    {
        if (!raids.PendingRunReturns().Contains(command.Run.Value))
            return false;
        var current = runDeparture.Find(command.Run);
        if (current?.Layout.Mode is not DungeonRunMode.AntonRaid)
            return raids.CompleteRunReturn(command.Run.Value);
        var frozen = runProjection.Find(command.Run).ToArray();
        foreach (var member in frozen)
            raids.QueueMemberDeparture(new(command.Run, member, EnableClear: true));
        await units.WriteAsync(context, new("anton-raid.force-return",
                frozen.Where(sessions.IsInWorld).Select(member => new SelectedSessionRecipient(member, null)).ToArray()),
            (_, _) => runDeparture.EndRunAsync(command.Run), ct).ConfigureAwait(false);
        return raids.CompleteRunReturn(command.Run.Value);
    }

    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, ExpireAntonRaidRejoinCommand command, CancellationToken ct)
    {
        if (raids.ExpireRejoin(command.Expiry) is not { } change)
            return false;
        await squads.DeliverAsync(change.Squads, ct).ConfigureAwait(false);
        await AntonRaidRecruitmentProjection.DeliverAsync(projections, change, ct).ConfigureAwait(false);
        return true;
    }

    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, RestoreAntonRaidSquadsCommand command, CancellationToken ct)
    {
        await squads.DeliverAsync(raids.RestoreAvailableSquads(), ct).ConfigureAwait(false);
        return true;
    }

    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, CleanTerminalAntonRaidsCommand command, CancellationToken ct)
    {
        await squads.DeliverChangesAsync(raids.CleanTerminalRaids(), ct).ConfigureAwait(false);
        return true;
    }

    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, DepartAntonRaidMemberCommand command, CancellationToken ct)
    {
        var departure = command.Departure;
        if (!raids.PendingMemberDepartures().Contains(departure))
            return false;
        var session = departure.Session;
        if (departure.EnableClear && raids.PendingRunReturns().Contains(departure.Run.Value))
            return false;
        if (runDeparture.Find(departure.Run)?.Layout.Mode is DungeonRunMode.AntonRaid)
        {
            IReadOnlyList<SelectedSessionIdentity> audience = sessions.IsInWorld(session) ? [session] : [];
            await units.WriteAsync(context, new("anton-raid.member-departure", TimerRecipientCapture.Capture(audience, users)),
                (_, _) => runDeparture.EndAsync(session.CharacterId, departure.Run), ct).ConfigureAwait(false);
        }
        runProjection.ParticipantDeparted(departure.Run, session.CharacterId);
        if (sessions.IsInWorld(session) && runDeparture.Find(session.CharacterId) is null)
        {
            if (departure.EnableClear)
                await AntonRaidRuntimeProjection.SendAsync(projections, [session],
                    [new Dfo.Server.Protocol.Game.DungeonClear.EnableClearDungeonNotification()], ct).ConfigureAwait(false);
            if (!await townReturn.ReturnAsync(session, ct, replayExistingTown: true).ConfigureAwait(false))
                return false;
        }
        return raids.CompleteMemberDeparture(departure);
    }

    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, AdvanceAntonRaidCommand command, CancellationToken ct)
    {
        var cohort = raids.PreparationParticipants(command.Deadline);
        if (cohort.Count == 0)
            return false;

        Exception? failure = null;
        var result = await units.WriteResultAsync(context,
            new("AntonRaidPreparation", TimerRecipientCapture.Capture(cohort, users)), async (unit, token) =>
            {
                try
                {
                    return await raids.FinishPreparationAsync(command.Deadline, token).ConfigureAwait(false);
                }
                catch (Exception error) when (error is not OperationCanceledException && !TransactionOutcomeUncertainException.Contains(error))
                {
                    failure = error;
                    unit.RequireRollback(error);
                    return new AntonRaidPreparationOutcome();
                }
            }, ct).ConfigureAwait(false);
        AntonRaidPreparationOutcome outcome;
        if (result is CommandWriteResult<AntonRaidPreparationOutcome>.CommitRolledBack rollback)
            failure = rollback.Cause;
        if (failure is not null)
        {
            logger.LogError(failure, "Anton raid preparation rolled back for {RaidNumber}", command.Deadline.Number);
            outcome = raids.CancelPreparation(command.Deadline);
            if (outcome.Change?.Raid is { } restored)
            {
                var refreshedTickets = await units.WriteAsync(context,
                    new("AntonRaidPreparationRecovery", TimerRecipientCapture.Capture(cohort, users)),
                    (_, token) => raids.TicketsAsync(restored.Leader.Session, token), ct).ConfigureAwait(false);
                outcome = outcome with { Tickets = refreshedTickets };
            }
        }
        else
        {
            outcome = ((CommandWriteResult<AntonRaidPreparationOutcome>.Returned)result).Value;
        }

        if (outcome.Change is not { Raid: { } raid } change)
            return false;
        await squads.DeliverAsync(change.Squads, ct).ConfigureAwait(false);
        await AntonRaidRecruitmentProjection.DeliverAsync(projections, change, ct).ConfigureAwait(false);
        var notices = new List<IGameNotiMessage> { AntonRaidRuntimeNotice.State(raid.Stage, raid.Phase) };
        if (outcome.Tickets is { } tickets)
            notices.Add(AntonRaidRuntimeNotice.Tickets(tickets));
        await AntonRaidRuntimeProjection.SendAsync(projections, cohort, notices, ct).ConfigureAwait(false);
        if (outcome.Debits is { } debits)
        {
            var now = clock.GetUtcNow();
            foreach (var member in cohort)
            {
                var adjustments = debits.Where(debit => debit.CharacterId == member.CharacterId).Select(debit =>
                {
                    var slot = A21InventoryLayout.ToGameSlot(debit.Slot);
                    return debit.Remaining is { } item
                        ? new InventoryDeltaMessage(slot.Space, [new InventoryItemState(null, slot.Index, item)], now)
                        : GameInventoryDelivery.ClearedSlotNotification(slot.Space, slot.Index, now);
                }).ToArray();
                await AntonRaidRuntimeProjection.SendAsync(projections, [member], adjustments, ct).ConfigureAwait(false);
            }
        }
        return outcome.AttackDeadline.HasValue;
    }
}

internal sealed class AntonRaidDeadlineSource : DungeonDeadlineSource
{
    public override DungeonDeadlineKind Kind => DungeonDeadlineKind.AntonRaid;

    public override async ValueTask<DateTimeOffset?> RunAsync(DungeonDeadlineRound round, CancellationToken ct)
    {
        var scan = await round.SubmitAsync(new MaintainAntonRaidCommand(), ct).ConfigureAwait(false);
        foreach (var deadline in scan.Due)
            await round.ItemAsync(new AdvanceAntonRaidCommand(deadline), "preparation deadline", ct).ConfigureAwait(false);
        foreach (var deadline in scan.Combat)
            await round.ItemAsync(new AdvanceAntonRaidCombatCommand(deadline), "combat deadline", ct).ConfigureAwait(false);
        foreach (var departure in scan.Departures)
            await round.ItemAsync(new DepartAntonRaidMemberCommand(departure), "depart raid member", ct).ConfigureAwait(false);
        foreach (var run in scan.Returns)
            await round.ItemAsync(new ReturnAntonRaidRunCommand(new DungeonRunId(run)), "end raid run", ct).ConfigureAwait(false);
        foreach (var expiry in scan.Expired)
            await round.ItemAsync(new ExpireAntonRaidRejoinCommand(expiry), "rejoin expiry", ct).ConfigureAwait(false);
        if (scan.RestoreSquads)
            await round.ItemAsync(new RestoreAntonRaidSquadsCommand(), "restore rejoined raid squads", ct).ConfigureAwait(false);
        if (scan.CleanTerminal)
            await round.ItemAsync(new CleanTerminalAntonRaidsCommand(), "clean terminal raids", ct).ConfigureAwait(false);
        return (await round.SubmitAsync(new MaintainAntonRaidCommand(), ct).ConfigureAwait(false)).Next;
    }
}
