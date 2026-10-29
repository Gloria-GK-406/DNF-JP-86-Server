using Dfo.Server.Data.World;
using System.Collections.Immutable;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Protocol.Game.Tournament;
using Microsoft.Extensions.Logging;
namespace Dfo.Server.Protocol.Game;
public sealed record MaintainDungeonReturnsCommand : ICommand<ImmutableArray<ReturnDungeonRunCommand>>;
public sealed record ReturnDungeonRunCommand(DungeonRunId RunId, bool Cleared) : ICommand<ImmutableArray<SelectedSessionIdentity>>;
public sealed record ProjectDungeonReturnCommand(SelectedSessionIdentity Session, DungeonRunId RunId, bool Cleared) : ICommand<bool>;
internal sealed partial class TowerReturnCommandHandler
{
    private readonly IDungeonRunDomainService wipeRuns;
    private readonly IDungeonRunProjectionDomainService wipeRegistry;
    private readonly IDungeonCardPresentationControl wipeCards;
    private readonly IAltarDungeonDomainService wipeAltars;
    private readonly ITournamentDungeonDomainService wipeTournaments;
    private readonly IInventorySlotProjectionDomainService wipeSlots;
    private readonly IAltarSchedulerWakeSignal wipeWake;
    public ValueTask<ImmutableArray<ReturnDungeonRunCommand>> HandleAsync(CommandExecutionContext context, MaintainDungeonReturnsCommand command, CancellationToken token)
    {
        var now = clock.GetUtcNow();
        return ValueTask.FromResult(wipeRuns.FindExpiredClearReturns(now).Select(run => new ReturnDungeonRunCommand(run, true))
            .Concat(wipeRuns.FindExpiredPartyWipes(now).Select(run => new ReturnDungeonRunCommand(run, false))).ToImmutableArray());
    }
    public ValueTask<ImmutableArray<SelectedSessionIdentity>> HandleAsync(CommandExecutionContext context, ReturnDungeonRunCommand command, CancellationToken token) =>
        ReturnDungeonAsync(context, command.RunId, command.Cleared, token);

    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, ProjectDungeonReturnCommand command, CancellationToken token)
    {
        var session = command.Session;
        // A captured return never admits a replacement session or a character that entered another run.
        if (!sessions.IsInWorld(session) || wipeRuns.Find(session.CharacterId) is not null) return false;
        var view = await currentViews.TryLoadAsync(session, token).ConfigureAwait(false);
        var position = onlineCharacters.PositionOf(session);
        if (view is null || position is null) throw new InvalidOperationException($"Cannot prepare dungeon return for {session.CharacterId}.");
        await TownArrivalProjection.EnterAsync(sessions, towns, housing, townAreas, onlineCharacters, currentViews, outbox,
            weaknessState, true, projections,
            new PartyTownProjectionDelivery(parties, townAreas, sessions, projections, projectionState),
            [new TownAreaEntry(session, view, position)], token).ConfigureAwait(false);
        logger.LogInformation("Dungeon returned character={CharacterId} run={RunId} reason={Reason}",
            session.CharacterId, command.RunId, command.Cleared ? "clear-timeout" : "party-wipe");
        return true;
    }

    private async ValueTask<ImmutableArray<SelectedSessionIdentity>> ReturnDungeonAsync(CommandExecutionContext context, DungeonRunId runId, bool cleared, CancellationToken token)
    {
        var frozen = wipeRegistry.Find(runId);
        switch (wipeRuns.Find(runId)?.Layout.Mode.Capabilities.PartyWipe)
        {
            // Guardian altar owns its result screen and the 50-second return deadline.
            case DungeonPartyWipePolicy.Guardian:
                return [];
            case DungeonPartyWipePolicy.AltarFailure when !cleared:
                return await FailAltarAsync(context, runId, token).ConfigureAwait(false);
            case DungeonPartyWipePolicy.TournamentElimination when !cleared:
                return await EliminateTournamentAsync(context, runId, token).ConfigureAwait(false);
        }
        var ended = cleared ? await wipeRuns.TryEndClearReturnAsync(runId, clock.GetUtcNow()).ConfigureAwait(false)
            : await wipeRuns.TryEndPartyWipeAsync(runId, clock.GetUtcNow()).ConfigureAwait(false);
        if (ended is null) return [];
        foreach (var session in frozen)
        {
            wipeCards.Cancel(session.CharacterId, runId);
            wipeRegistry.ParticipantDeparted(runId, session.CharacterId);
        }
        // Only identity crosses the producer boundary; each recipient loads its current view in its own CMD.
        return frozen.Where(session => ended.Participants.Any(participant => participant.CharacterId == session.CharacterId))
            .ToImmutableArray();
    }

    private async ValueTask<ImmutableArray<SelectedSessionIdentity>> FailAltarAsync(CommandExecutionContext context, DungeonRunId runId, CancellationToken token)
    {
        if (wipeAltars.Find(runId) is null) return [];
        var cohort = AltarTransitionProjection.Participants(wipeRuns, sessions, wipeRegistry, runId);
        if (cohort.Count == 0) return [];
        Exception? rejected = null;
        var write = await workUnits.WriteResultAsync(context, new("altar.wipe", ProtectedParticipants(cohort, cohort[0])), async (unit, ct) =>
        {
            try { return new Altar.AltarCommandResult(await wipeAltars.FailAsync(runId, ct).ConfigureAwait(false)); }
            catch (Exception error) when (error is not OperationCanceledException && !TransactionOutcomeUncertainException.Contains(error))
            { rejected = error; unit.RequireRollback(error); return new Altar.AltarCommandResult(null); }
        }, token).ConfigureAwait(false);
        if (write is CommandWriteResult<Altar.AltarCommandResult>.CommitRolledBack rolledBack) rejected = rolledBack.Cause;
        if (rejected is not null) { logger.LogError(rejected, "Party wipe return failed run={RunId}", runId); return []; }
        var action = ((CommandWriteResult<Altar.AltarCommandResult>.Returned)write).Value.Action;
        if (action is not null)
            await AltarTransitionProjection.ProjectAsync(projections, action,
                AltarTransitionProjection.Participants(wipeRuns, sessions, wipeRegistry, runId), clock, wipeWake, logger, token).ConfigureAwait(false);
        return [];
    }

    private async ValueTask<ImmutableArray<SelectedSessionIdentity>> EliminateTournamentAsync(CommandExecutionContext context, DungeonRunId runId, CancellationToken token)
    {
        var run = wipeRuns.Find(runId);
        if (run?.Layout.Mode is not DungeonRunMode.Tournament) return [];
        var session = sessions.FindByCharacter(run.Participants[0].CharacterId);
        if (session is null || wipeTournaments.Find(session.CharacterId) is not { IsFinished: false }) return [];
        Exception? rejected = null;
        var write = await workUnits.WriteResultAsync(context, new("tournament.wipe", [CaptureParticipant(session)]), async (unit, ct) =>
        {
            try { return new TournamentCommandResult(await wipeTournaments.EliminateAsync(session, runId, ct).ConfigureAwait(false)); }
            catch (Exception error) when (error is not OperationCanceledException && !TransactionOutcomeUncertainException.Contains(error))
            { rejected = error; unit.RequireRollback(error); return new TournamentCommandResult(null); }
        }, token).ConfigureAwait(false);
        if (write is CommandWriteResult<TournamentCommandResult>.CommitRolledBack rolledBack) rejected = rolledBack.Cause;
        if (rejected is not null) { logger.LogError(rejected, "Party wipe return failed run={RunId}", runId); return []; }
        var action = ((CommandWriteResult<TournamentCommandResult>.Returned)write).Value.Action;
        if (action is not null && sessions.IsInWorld(session))
            await outbox.RegisterExplicitAsync(session.ConnectionId, new(TournamentCompletionFrames.CompletionFrames(action, wipeSlots, clock), []), token).ConfigureAwait(false);
        return [];
    }
}
