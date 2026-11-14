using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Pvp;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.PvpBattle;

/// <summary>Reads which duel rooms and ended battles are due; reads committed state only.</summary>
public sealed record ScanPvpDeadlinesCommand : ICommand<PvpDeadlineScan>;

/// <summary>Advances one duel room whose deadline passed, settling its battle when nobody is left to wait on.</summary>
public sealed record AdvancePvpRoomCommand(PvpRoomId Room) : ICommand<bool>;

/// <summary>Settles one ended duel battle.</summary>
public sealed record SettlePvpMatchCommand(PvpMatchId Match) : ICommand<bool>;

/// <summary>Lists every ended duel battle still waiting for settlement, for the shutdown round.</summary>
public sealed record ListPendingPvpSettlementsCommand : ICommand<IReadOnlyList<PvpMatchId>>;

/// <summary>Cancels every duel battle that has not ended, and stops new ones starting, for the shutdown round.</summary>
public sealed record CancelPvpBattlesForShutdownCommand : ICommand<bool>;

/// <summary>Drops an ended duel battle whose settlement kept failing past the give-up time.</summary>
public sealed record AbandonPvpSettlementCommand(PvpMatchId Match) : ICommand<bool>;

/// <summary>What one deadline advance did: the room's change, and the settlement it asked for.</summary>
public sealed record PvpRoomAdvance(PvpRoomOutcome.Changed? Room, PvpSettlementOutcome? Settlement);

/// <summary>
/// The duel deadline and shutdown commands. Each one is a single domain step in one write unit; what the
/// fighters see of it is projected through <see cref="PvpBattleProjection"/> after the unit commits.
/// </summary>
public sealed class PvpBattleDeadlineCommandHandler(
    IPvpRoomDomainService rooms,
    IPvpSettlementDomainService settlements,
    ICommandWorkUnits units,
    TimeProvider clock,
    ILogger<PvpBattleDeadlineCommandHandler> logger)
    : ICommandHandler<ScanPvpDeadlinesCommand, PvpDeadlineScan>,
        ICommandHandler<AdvancePvpRoomCommand, bool>,
        ICommandHandler<SettlePvpMatchCommand, bool>,
        ICommandHandler<ListPendingPvpSettlementsCommand, IReadOnlyList<PvpMatchId>>,
        ICommandHandler<CancelPvpBattlesForShutdownCommand, bool>,
        ICommandHandler<AbandonPvpSettlementCommand, bool>
{
    public ValueTask<PvpDeadlineScan> HandleAsync(
        CommandExecutionContext context, ScanPvpDeadlinesCommand command, CancellationToken token) =>
        ValueTask.FromResult(rooms.Deadlines(clock.GetUtcNow()));

    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, AdvancePvpRoomCommand command, CancellationToken token)
    {
        var advance = await units.WriteAsync(context, new CommandWorkUnitDeclaration("pvp.deadline", []), async (_, ct) =>
        {
            var changed = await rooms.AdvanceDueAsync(command.Room, clock.GetUtcNow(), ct).ConfigureAwait(false);
            var settled = changed?.SettlementDue is { } match
                ? await settlements.SettleAsync(match, ct).ConfigureAwait(false)
                : null;
            return new PvpRoomAdvance(changed, settled);
        }, token).ConfigureAwait(false);
        if (advance.Room is { } room)
            logger.LogInformation("Duel room {Room} advanced past its deadline: {Before} -> {After}",
                command.Room.Value, room.Before?.Phase, room.After?.Phase);
        await PvpBattleProjection.DeadlinePassedAsync(advance, token).ConfigureAwait(false);
        return true;
    }

    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, SettlePvpMatchCommand command, CancellationToken token)
    {
        var settled = await units.WriteAsync(context, new CommandWorkUnitDeclaration("pvp.settlement", []),
            (_, ct) => settlements.SettleAsync(command.Match, ct), token).ConfigureAwait(false);
        await PvpBattleProjection.SettledAsync(settled, token).ConfigureAwait(false);
        return true;
    }

    public ValueTask<IReadOnlyList<PvpMatchId>> HandleAsync(
        CommandExecutionContext context, ListPendingPvpSettlementsCommand command, CancellationToken token) =>
        ValueTask.FromResult<IReadOnlyList<PvpMatchId>>([.. rooms.PendingConclusions().Select(conclusion => conclusion.MatchId)]);

    public async ValueTask<bool> HandleAsync(
        CommandExecutionContext context, CancelPvpBattlesForShutdownCommand command, CancellationToken token)
    {
        var cancelled = await units.WriteAsync(context, new CommandWorkUnitDeclaration("pvp.shutdown", []),
            (_, ct) => rooms.CancelForShutdownAsync(ct), token).ConfigureAwait(false);
        if (cancelled.Count > 0) logger.LogInformation("Cancelled {Count} duel battles for shutdown", cancelled.Count);
        await PvpBattleProjection.CancelledForShutdownAsync(cancelled, token).ConfigureAwait(false);
        return true;
    }

    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, AbandonPvpSettlementCommand command, CancellationToken token)
    {
        var abandoned = await units.WriteAsync(context, new CommandWorkUnitDeclaration("pvp.settlement-abandon", []),
            async (_, ct) => new PvpRoomAdvance(await rooms.AbandonSettlementAsync(command.Match, ct).ConfigureAwait(false), null),
            token).ConfigureAwait(false);
        await PvpBattleProjection.DeadlinePassedAsync(abandoned, token).ConfigureAwait(false);
        return true;
    }
}

/// <summary>
/// What the duel rooms show the fighters after a deadline, a settlement or the shutdown cancel committed.
/// </summary>
/// <remarks>
/// The projection seam of these internal commands: the domain results carry everything the frames need
/// (phases before and after, the frozen end, every fighter's settled result, the seats taken away). The wire
/// frames belong to the duel protocol slice, which fills these in and passes the handler's registration seams
/// (<c>IGameCommandOutbox</c>, <c>ICommandProjections</c>) along; until then nothing is projected.
/// </remarks>
internal static class PvpBattleProjection
{
    public static ValueTask DeadlinePassedAsync(PvpRoomAdvance advance, CancellationToken token) => ValueTask.CompletedTask;

    public static ValueTask SettledAsync(PvpSettlementOutcome settled, CancellationToken token) => ValueTask.CompletedTask;

    public static ValueTask CancelledForShutdownAsync(IReadOnlyList<PvpRoomOutcome.Changed> cancelled, CancellationToken token) =>
        ValueTask.CompletedTask;
}

/// <summary>
/// The duel deadline source: scans the room owner, advances each due room, settles each due battle and drops each
/// battle past its settlement give-up time as an item of its own, then reports when to look again. On shutdown it
/// cancels what still runs and settles what has ended.
/// </summary>
internal sealed class PvpBattleDeadlineSource : PvpDeadlineSource
{
    public override string Name => "rooms";

    public override async ValueTask<DateTimeOffset?> RunAsync(PvpDeadlineRound round, CancellationToken token)
    {
        var scan = await round.SubmitAsync(new ScanPvpDeadlinesCommand(), token).ConfigureAwait(false);
        foreach (var room in scan.DueRooms)
            await round.ItemAsync(new AdvancePvpRoomCommand(room), "advance", token).ConfigureAwait(false);
        foreach (var match in scan.DueSettlements)
            await round.ItemAsync(new SettlePvpMatchCommand(match), "settle", token).ConfigureAwait(false);
        foreach (var match in scan.Abandoned)
            await round.ItemAsync(new AbandonPvpSettlementCommand(match), "abandon", token).ConfigureAwait(false);
        return (await round.SubmitAsync(new ScanPvpDeadlinesCommand(), token).ConfigureAwait(false)).Next;
    }

    /// <summary>
    /// Spec RQ-042: whatever still runs is cancelled, which also stops new battles starting, and then every ended
    /// battle is settled. Cancelling first leaves no battle that could end between the two steps unsettled.
    /// </summary>
    public override async ValueTask ShutdownAsync(PvpDeadlineRound round, CancellationToken token)
    {
        await round.ItemAsync(new CancelPvpBattlesForShutdownCommand(), "cancel", token).ConfigureAwait(false);
        foreach (var match in await round.SubmitAsync(new ListPendingPvpSettlementsCommand(), token).ConfigureAwait(false))
            await round.ItemAsync(new SettlePvpMatchCommand(match), "settle", token).ConfigureAwait(false);
    }
}
