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
public sealed record MaintainDungeonReturnsCommand : ICommand<bool>;
public sealed record ReturnDungeonRunCommand(DungeonRunId RunId, bool Cleared) : ICommand<bool>;
internal sealed partial class TowerReturnCommandHandler
{
    private readonly IDungeonRunDomainService wipeRuns;
    private readonly IDungeonRunProjectionDomainService wipeRegistry;
    private readonly IDungeonCardPresentationControl wipeCards;
    private readonly IAltarDungeonDomainService wipeAltars;
    private readonly ITournamentDungeonDomainService wipeTournaments;
    private readonly IInventorySlotProjectionDomainService wipeSlots;
    private readonly IAltarSchedulerWakeSignal wipeWake;
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, MaintainDungeonReturnsCommand command, CancellationToken token)
    {
        foreach (var run in wipeRuns.FindExpiredClearReturns(clock.GetUtcNow())) await ReturnDungeonAsync(context, run, true, token).ConfigureAwait(false);
        foreach (var run in wipeRuns.FindExpiredPartyWipes(clock.GetUtcNow())) await ReturnDungeonAsync(context, run, false, token).ConfigureAwait(false);
        return true;
    }
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, ReturnDungeonRunCommand command, CancellationToken token)
    { await ReturnDungeonAsync(context, command.RunId, command.Cleared, token).ConfigureAwait(false); return true; }
    private async ValueTask ReturnDungeonAsync(CommandExecutionContext context, DungeonRunId runId, bool cleared, CancellationToken token)
    {
        var frozen = wipeRegistry.Find(runId);
        if (!cleared && wipeRuns.Find(runId)?.Layout.Altar is not null)
        {
            if (wipeAltars.Find(runId) is null) return;
            var cohort = AltarTransitionProjection.Participants(wipeRuns, sessions, wipeRegistry, runId);
            if (cohort.Count == 0) return;
            Exception? rejected = null;
            var write = await workUnits!.WriteResultAsync(context, new("altar.wipe", ProtectedParticipants(cohort, cohort[0])), async (unit, ct) =>
            {
                try { return new Altar.AltarCommandResult(await wipeAltars.FailAsync(runId, ct).ConfigureAwait(false)); }
                catch (Exception error) when (error is not OperationCanceledException && !TransactionOutcomeUncertainException.Contains(error))
                { rejected = error; unit.RequireRollback(error); return new Altar.AltarCommandResult(null); }
            }, token).ConfigureAwait(false);
            if (write is CommandWriteResult<Altar.AltarCommandResult>.CommitRolledBack rolledBack) rejected = rolledBack.Cause;
            if (rejected is not null) { logger.LogError(rejected, "Party wipe return failed run={RunId}", runId); return; }
            var action = ((CommandWriteResult<Altar.AltarCommandResult>.Returned)write).Value.Action;
            if (action is not null)
                await AltarTransitionProjection.ProjectAsync(projections, action,
                    AltarTransitionProjection.Participants(wipeRuns, sessions, wipeRegistry, runId), clock, wipeWake, logger, token).ConfigureAwait(false);
            return;
        }
        if (!cleared && wipeRuns.Find(runId)?.Layout.Tournament is not null)
        {
            var run = wipeRuns.Find(runId);
            if (run?.Layout.Tournament is null) return;
            var session = sessions.FindByCharacter(run.Participants[0].CharacterId);
            if (session is null || wipeTournaments.Find(session.CharacterId) is not { IsFinished: false }) return;
            Exception? rejected = null;
            var write = await workUnits!.WriteResultAsync(context, new("tournament.wipe", [CaptureParticipant(session)]), async (unit, ct) =>
            {
                try { return new TournamentCommandResult(await wipeTournaments.EliminateAsync(session, runId, ct).ConfigureAwait(false)); }
                catch (Exception error) when (error is not OperationCanceledException && !TransactionOutcomeUncertainException.Contains(error))
                { rejected = error; unit.RequireRollback(error); return new TournamentCommandResult(null); }
            }, token).ConfigureAwait(false);
            if (write is CommandWriteResult<TournamentCommandResult>.CommitRolledBack rolledBack) rejected = rolledBack.Cause;
            if (rejected is not null) { logger.LogError(rejected, "Party wipe return failed run={RunId}", runId); return; }
            var action = ((CommandWriteResult<TournamentCommandResult>.Returned)write).Value.Action;
            if (action is not null && sessions.IsInWorld(session))
                await register(session.ConnectionId, new(TournamentCompletionFrames.CompletionFrames(action, wipeSlots, clock), []), token).ConfigureAwait(false);
            return;
        }
        var entries = new List<TownAreaEntry>();
        DungeonRunState? ended;
        try
        {
            foreach (var session in frozen)
            {
                if (!sessions.IsInWorld(session) || wipeRuns.Find(session.CharacterId)?.RunId != runId) continue;
                var view = await currentViews.TryLoadAsync(session, token).ConfigureAwait(false);
                var position = onlineCharacters.PositionOf(session);
                if (view is null || position is null) throw new InvalidOperationException($"Cannot prepare party wipe return for {session.CharacterId}.");
                entries.Add(new(session, view, position));
            }
            ended = cleared ? await wipeRuns.TryEndClearReturnAsync(runId, clock.GetUtcNow()).ConfigureAwait(false)
                : await wipeRuns.TryEndPartyWipeAsync(runId, clock.GetUtcNow()).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException && !TransactionOutcomeUncertainException.Contains(error))
        {
            context.Request!.DemandCurrent();
            logger.LogError(error, cleared ? "Clear timeout return failed run={RunId}" : "Party wipe return failed run={RunId}", runId);
            return;
        }
        if (ended is null) return;
        foreach (var session in frozen) { wipeCards.Cancel(session.CharacterId, runId); wipeRegistry.ParticipantDeparted(runId, session.CharacterId); }
        foreach (var entry in entries.Where(entry => ended.Participants.Any(participant => participant.CharacterId == entry.Session.CharacterId)))
        {
            await TownEnterAsync([entry], token).ConfigureAwait(false);
            logger.LogInformation("Dungeon returned character={CharacterId} run={RunId} reason={Reason}", entry.Session.CharacterId, runId, cleared ? "clear-timeout" : "party-wipe");
        }
    }
}
