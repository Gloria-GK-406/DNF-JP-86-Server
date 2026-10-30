using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

internal sealed partial class DungeonRunDomainService
{
    public DungeonRunState? TryMoveByLostLandItem(Dfo.Server.Data.Characters.CharacterId character,
        DungeonRunId id, long version, Dfo.Server.Abstractions.Content.MazeCell target, DungeonCellSeed? preparedCell)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(TryMoveByLostLandItem));
        return TryMoveCore(character, id, version, target, preparedCell, lostLandItem: true);
    }

    public IReadOnlyList<LostLandCompletion> FindDueLostLandCompletions(DateTimeOffset now) =>
        this.runsById.Values.Where(run => run.LostLandExploration is { NoticeRegistered: false } state
                && state.EndsAt <= now && (!state.IsEnding || run.ReadyParticipants.Count > 0))
            .Select(run => new LostLandCompletion(run.RunId, run.LostLandExploration!.EndsAt)).ToArray();

    public bool StageLostLandCompletion(LostLandCompletion completion, DateTimeOffset now, bool notifyReady)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(StageLostLandCompletion));
        if (!this.runsById.TryGetValue(completion.RunId, out var run)
            || run.LostLandExploration is not { NoticeRegistered: false } state
            || state.EndsAt != completion.EndsAt || now < state.EndsAt
            || notifyReady && run.ReadyParticipants.Count == 0 || state.IsEnding && !notifyReady) return false;
        var next = state with { IsEnding = true, NoticeRegistered = notifyReady };
        if (this.transactions.Current is { } transaction)
            transaction.Enlist((this, completion, "lost-land-completion"), () => new CompletionParticipant(this, run, state, next));
        else { run.LostLandExploration = next; run.RuntimeRevision++; }
        return true;
    }

    private sealed class CompletionParticipant(DungeonRunDomainService owner, DungeonRunAggregate run,
        LostLandExplorationState expected, LostLandExplorationState next) : ITransactionParticipant
    {
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken token)
        {
            if (owner.runsById.GetValueOrDefault(run.RunId) != run || run.LostLandExploration != expected)
                throw new InvalidOperationException("LostLand completion reservation is stale.");
            return ValueTask.CompletedTask;
        }
        public ValueTask CommitAsync(CancellationToken token)
        {
            run.LostLandExploration = next;
            run.RuntimeRevision++;
            return ValueTask.CompletedTask;
        }
        public ValueTask RollbackAsync(CancellationToken token) => ValueTask.CompletedTask;
    }

    public IReadOnlyList<LostLandFeverExpiry> FindExpiredLostLandFevers(DateTimeOffset now) =>
        this.runsById.Values.Where(run => run.LostLandExploration?.IsEnding != true && run.LostLandFever?.ActiveUntil <= now)
            .Select(run => new LostLandFeverExpiry(run.RunId, run.LostLandFever!.ActiveUntil!.Value)).ToArray();

    public bool StageLostLandFeverExpiry(LostLandFeverExpiry expiry, DateTimeOffset now)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(StageLostLandFeverExpiry));
        if (expiry.ActiveUntil > now || !IsCurrentFeverExpiry(expiry)) return false;
        if (this.transactions.Current is { } transaction)
            transaction.Enlist((this, expiry, "lost-land-fever-expiry"), () => new FeverExpiryParticipant(this, expiry));
        else CommitFeverExpiry(expiry);
        return true;
    }

    private bool IsCurrentFeverExpiry(LostLandFeverExpiry expiry) =>
        this.runsById.TryGetValue(expiry.RunId, out var run)
            && run.LostLandFever?.ActiveUntil == expiry.ActiveUntil;

    private void CommitFeverExpiry(LostLandFeverExpiry expiry)
    {
        if (!IsCurrentFeverExpiry(expiry))
            throw new InvalidOperationException("Prepared LostLand fever expiry changed before publication.");
        var run = this.runsById[expiry.RunId];
        run.LostLandFever = new();
        run.RuntimeRevision++;
    }

    private sealed class FeverExpiryParticipant(DungeonRunDomainService owner, LostLandFeverExpiry expiry)
        : ITransactionParticipant
    {
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken cancellationToken)
        {
            if (!owner.IsCurrentFeverExpiry(expiry))
                throw new InvalidOperationException("LostLand fever expiry reservation is stale.");
            return ValueTask.CompletedTask;
        }
        public ValueTask CommitAsync(CancellationToken cancellationToken)
        {
            owner.CommitFeverExpiry(expiry);
            return ValueTask.CompletedTask;
        }
        public ValueTask RollbackAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
