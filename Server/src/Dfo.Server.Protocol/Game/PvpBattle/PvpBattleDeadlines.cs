using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Pvp;
using Dfo.Server.Protocol.Game.PvpDailyMission;
using Dfo.Server.Protocol.Game.PvpRoom;
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

/// <summary>Lists the committed room departures waiting for their projection.</summary>
public sealed record CapturePvpDeparturesCommand : ICommand<ImmutableArray<PvpDepartureEntryId>>;

/// <summary>Projects one committed room departure, and the settlement it led to.</summary>
public sealed record ProjectPvpDepartureCommand(PvpDepartureEntryId Entry) : ICommand<bool>;

/// <summary>What one deadline advance did: the room's change, and the settlement it asked for.</summary>
public sealed record PvpRoomAdvance(PvpRoomOutcome.Changed? Room, PvpSettlementOutcome? Settlement);

/// <summary>
/// The duel deadline, departure and shutdown commands. Each one is a single domain step in one write unit; what the
/// fighters see of it is projected through <see cref="PvpBattleProjector"/> after the unit returns.
/// </summary>
public sealed class PvpBattleDeadlineCommandHandler(
    IPvpRoomDomainService rooms,
    IPvpSettlementDomainService settlements,
    IPvpRecordDomainService records,
    IPvpDepartureProjectionDomainService departures,
    IPvpResultScreenDeferralDomainService deferrals,
    ISelectedSessionDomainService sessions,
    IPvpLobbyDomainService lobby,
    ISelectedCharacterCurrentViewDomainService currentViews,
    IOnlineCharacterRuntimeDomainService online,
    IOnlineUserQueryDomainService onlineUsers,
    IGameSessionEndpointDomainService endpoints,
    ITownAreaDomainService townAreas,
    IPartyService parties,
    IPartyProjectionStateDomainService projectionState,
    IWeaknessDomainService weakness,
    ICommandWorkUnits units,
    ICommandProjections projections,
    IGameCommandOutbox outbox,
    TimeProvider clock,
    IPvpDailyMissionQueryDomainService dailyMissions,
    IPvpColosseumDomainService colosseum,
    ILogger<PvpBattleDeadlineCommandHandler> logger)
    : ICommandHandler<ScanPvpDeadlinesCommand, PvpDeadlineScan>,
        ICommandHandler<AdvancePvpRoomCommand, bool>,
        ICommandHandler<SettlePvpMatchCommand, bool>,
        ICommandHandler<ListPendingPvpSettlementsCommand, IReadOnlyList<PvpMatchId>>,
        ICommandHandler<CancelPvpBattlesForShutdownCommand, bool>,
        ICommandHandler<AbandonPvpSettlementCommand, bool>,
        ICommandHandler<CapturePvpDeparturesCommand, ImmutableArray<PvpDepartureEntryId>>,
        ICommandHandler<ProjectPvpDepartureCommand, bool>
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
        await ProjectAsync(context, advance.Room, advance.Settlement, token).ConfigureAwait(false);
        return true;
    }

    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, SettlePvpMatchCommand command, CancellationToken token)
    {
        var settled = await units.WriteAsync(context, new CommandWorkUnitDeclaration("pvp.settlement", []),
            (_, ct) => settlements.SettleAsync(command.Match, ct), token).ConfigureAwait(false);
        await ProjectAsync(context, null, settled, token).ConfigureAwait(false);
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
        // The connections close next; telling the fighters is a courtesy that must not stop the shutdown round.
        var projector = Projector();
        foreach (var change in cancelled)
        {
            try
            {
                await projector.ChangedAsync(change, token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logger.LogWarning(error, "Duel room {Room} cancelled for shutdown could not be projected",
                    (change.After ?? change.Before)?.Number);
            }
        }
        return true;
    }

    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, AbandonPvpSettlementCommand command, CancellationToken token)
    {
        var abandoned = await units.WriteAsync(context, new CommandWorkUnitDeclaration("pvp.settlement-abandon", []),
            async (_, ct) => new PvpRoomAdvance(await rooms.AbandonSettlementAsync(command.Match, ct).ConfigureAwait(false), null),
            token).ConfigureAwait(false);
        // The room waits again: its members and the lobby see it as after any return to waiting.
        await ProjectAsync(context, abandoned.Room, null, token).ConfigureAwait(false);
        return true;
    }

    public ValueTask<ImmutableArray<PvpDepartureEntryId>> HandleAsync(
        CommandExecutionContext context, CapturePvpDeparturesCommand command, CancellationToken token) =>
        ValueTask.FromResult(departures.SnapshotPending());

    /// <summary>
    /// A departure selected-session cleanup committed: the members left behind see it as any other room change of
    /// the battle (a fighter who fled, the end of the fight, the seat), and the settlement it led to, when the cleanup
    /// could make one.
    /// </summary>
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, ProjectPvpDepartureCommand command, CancellationToken token)
    {
        if (!departures.TryClaim(command.Entry, out var claimed)) return false;
        await ProjectAsync(context, claimed!.Departure, claimed.Settled, token).ConfigureAwait(false);
        return true;
    }

    private async ValueTask ProjectAsync(CommandExecutionContext context, PvpRoomOutcome.Changed? change,
        PvpSettlementOutcome? settled, CancellationToken token)
    {
        var projector = Projector();
        if (change is not null) await projector.ChangedAsync(change, token).ConfigureAwait(false);
        if (settled is PvpSettlementOutcome.Settled written)
            await projector.SettledAsync(written, await PvpBattleRecords.ReadAsync(units, records, context, written, token)
                .ConfigureAwait(false), token).ConfigureAwait(false);
    }

    private PvpBattleProjector Projector() => new(new PvpRoomProjector(sessions, lobby, currentViews, online, onlineUsers, endpoints,
        townAreas, parties, projectionState, weakness, projections, outbox, logger, deferrals, dailyMissions: new PvpDailyMissionProjection(dailyMissions, outbox, colosseum)), logger);
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
        // Departures first: what a departure showed the members comes before what the deadlines after it show.
        await ProjectDeparturesAsync(round, token).ConfigureAwait(false);
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
        await ProjectDeparturesAsync(round, token).ConfigureAwait(false);
        await round.ItemAsync(new CancelPvpBattlesForShutdownCommand(), "cancel", token).ConfigureAwait(false);
        foreach (var match in await round.SubmitAsync(new ListPendingPvpSettlementsCommand(), token).ConfigureAwait(false))
            await round.ItemAsync(new SettlePvpMatchCommand(match), "settle", token).ConfigureAwait(false);
    }

    private static async ValueTask ProjectDeparturesAsync(PvpDeadlineRound round, CancellationToken token)
    {
        foreach (var entry in await round.SubmitAsync(new CapturePvpDeparturesCommand(), token).ConfigureAwait(false))
            await round.ItemAsync(new ProjectPvpDepartureCommand(entry), "departure", token).ConfigureAwait(false);
    }
}
