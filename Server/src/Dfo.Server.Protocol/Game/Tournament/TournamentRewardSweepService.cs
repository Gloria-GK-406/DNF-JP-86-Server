using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.Tournament;

public sealed record SweepTournamentRewardsCommand : ICommand<bool>;
public sealed record SweepTournamentCandidateCommand(TournamentDungeonState Candidate) : ICommand<bool>;
public sealed class SweepTournamentRewardsCommandHandler(ITournamentDungeonDomainService tournaments,
    ISelectedSessionDomainService sessions, IDungeonRunQueryDomainService runs,
    Dfo.Server.Abstractions.OnlineUsers.IOnlineUserQueryDomainService users,
    ICommandWorkUnits units, IGameCommandOutbox outbox, TimeProvider clock, ILogger<SweepTournamentRewardsCommandHandler> logger)
    : ICommandHandler<SweepTournamentRewardsCommand, bool>, ICommandHandler<SweepTournamentCandidateCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, SweepTournamentRewardsCommand command, CancellationToken token)
    {
        foreach (var candidate in tournaments.PendingRewards(clock.GetUtcNow())) await SweepAsync(context, candidate, token).ConfigureAwait(false);
        return true;
    }
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, SweepTournamentCandidateCommand command, CancellationToken token)
    { await SweepAsync(context, command.Candidate, token).ConfigureAwait(false); return true; }
    private async ValueTask SweepAsync(CommandExecutionContext context, TournamentDungeonState candidate, CancellationToken token)
    {
        if (!sessions.TryGetInWorld(candidate.CharacterId, out var session)) return;
        var current = tournaments.Find(candidate.CharacterId);
        if (!sessions.IsInWorld(session) || current?.RunId != candidate.RunId || runs.Find(candidate.CharacterId)?.RunId != candidate.RunId
            || current.RewardSelectionEndsAt is not {} deadline || deadline > clock.GetUtcNow()) return;
        for (byte row = 0; row < current.Rewards.Count; row++)
        {
            if (current.Rewards[row].Count == 0 || current.Selections[row].HasValue) continue;
            Exception? rejected = null;
            var write = await units.WriteResultAsync(context, new("tournament.auto-reward", TimerRecipientCapture.Capture([session], users)), async (unit, ct) =>
            {
                try { return new TournamentCommandResult(await tournaments.SelectRewardAsync(session, current.RunId, row, 0, ct).ConfigureAwait(false)); }
                catch (Exception error) when (error is not OperationCanceledException && !Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException.Contains(error))
                { rejected = error; unit.RequireRollback(error); return new TournamentCommandResult(null); }
            }, token).ConfigureAwait(false);
            if (write is CommandWriteResult<TournamentCommandResult>.CommitRolledBack rolledBack) rejected = rolledBack.Cause;
            if (rejected is not null) { logger.LogError(rejected, "Tournament automatic reward failed run={RunId}", candidate.RunId); return; }
            var result = ((CommandWriteResult<TournamentCommandResult>.Returned)write).Value;
            if (result.Action is not {} action) continue;
            current = action.State;
            var slot = runs.Find(candidate.CharacterId)?.Participant?.Slot ?? 0;
            await outbox.RegisterExplicitAsync(session.ConnectionId, new([.. TournamentCompletionFrames.CompletionFrames(action, clock),
                new TournamentSelectionResponse(current, slot)], []), token).ConfigureAwait(false);
            logger.LogInformation("Tournament automatic card selected character={CharacterId} run={RunId} row={Row} deadlineLagMs={Lag}",
                current.CharacterId, current.RunId, row, (clock.GetUtcNow() - deadline).TotalMilliseconds);
        }
    }
}
public sealed class TournamentRewardSweepService(ICommandBus commands, TimeProvider clock, ILogger<TournamentRewardSweepService> logger)
    : TypedGameplayTimerSource(commands, logger)
{
    protected override string SourceName => "timer:TournamentRewardSweepService";
    protected override async Task RunRoundAsync(GameplayCallChain root, CancellationToken token) =>
        _ = await SubmitAsync(new SweepTournamentRewardsCommand(), root, SourceName, token).ConfigureAwait(false);
    internal async ValueTask SweepAsync(TournamentDungeonState candidate, CancellationToken token) =>
        _ = await SubmitAsync(new SweepTournamentCandidateCommand(candidate), GameplayCallChain.NewRoot(GameplayCallSource.Timer), SourceName, token).ConfigureAwait(false);
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50), clock);
        try { while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false)) await HostedAsync(stoppingToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
