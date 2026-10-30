using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Immutable;

namespace Dfo.Server.Application.World;

internal sealed partial class DungeonRunStore
{
    internal sealed partial class TimerService
    {
        public void ScheduleClearReturn(CharacterId characterId, DungeonRunId runId, DateTimeOffset now)
        {
            GameplayWriteAssertion.Writing(nameof(TimerService) + "." + nameof(ScheduleClearReturn));
            lock (this.store.gate)
            {
                if (!this.store.runsById.TryGetValue(runId, out var run)
                    || !run.IsMember(characterId) || run.Participants.Length < 2
                    || !run.Cells[run.CurrentCell].ClearOpen
                    || !run.ProgressFor(characterId).CardLayoutOpen) return;
                // A21 ordinary party result UI starts a 60 second return countdown.
                run.Claims.ClearReturnDeadline ??= now.AddSeconds(60);
            }
        }

        public IReadOnlyList<DungeonRunId> FindExpiredClearReturns(DateTimeOffset now)
        {
            lock (this.store.gate)
                return this.store.runsById.Values.Where(run => run.Claims.ClearReturnDeadline <= now)
                    .Select(run => run.RunId).ToArray();
        }

        public DungeonRunState? TryEndClearReturn(DungeonRunId runId, DateTimeOffset now)
        {
            GameplayWriteAssertion.Writing(nameof(TimerService) + "." + nameof(TryEndClearReturn));
            lock (this.store.gate)
            {
                if (!this.store.runsById.TryGetValue(runId, out var run)
                    || run.Claims.ClearReturnDeadline is not { } deadline
                    || now < deadline || !run.Cells[run.CurrentCell].ClearOpen) return null;
                var members = run.Participants;
                if (members.Length == 0) return null;
                var snapshot = run.ViewFor(members[0].CharacterId) with { Participants = members };
                _ = this.store.EndRun(run);
                return snapshot;
            }
        }

        public IReadOnlyList<DungeonRunId> FindExpiredPartyWipes(DateTimeOffset now)
        {
            GameplayWriteAssertion.Writing(nameof(TimerService) + "." + nameof(FindExpiredPartyWipes));
            lock (this.store.gate)
            {
                foreach (var aggregate in this.store.runsById.Values)
                {
                    if (!IsPartyWiped(aggregate))
                        aggregate.Claims.WipeDeadline = null;
                    else
                        aggregate.Claims.WipeDeadline ??= now.AddSeconds(10);
                }
                return this.store.runsById.Values.Where(run => run.Claims.WipeDeadline <= now).Select(run => run.RunId).ToArray();
            }
        }

        public DungeonRunState? TryEndPartyWipe(DungeonRunId runId, DateTimeOffset now)
        {
            GameplayWriteAssertion.Writing(nameof(TimerService) + "." + nameof(TryEndPartyWipe));
            lock (this.store.gate)
            {
                if (!this.store.runsById.TryGetValue(runId, out var aggregate)
                    || aggregate.Claims.WipeDeadline is not { } deadline
                    || now < deadline)
                    return null;
                if (!IsPartyWiped(aggregate))
                {
                    aggregate.Claims.WipeDeadline = null;
                    return null;
                }
                var members = aggregate.Participants;
                var snapshot = aggregate.ViewFor(members[0].CharacterId) with { Participants = members };
                _ = this.store.EndRun(aggregate);
                return snapshot;
            }
        }

        private static bool IsPartyWiped(DungeonRunAggregate aggregate)
        {
            var members = aggregate.Participants;
            return !aggregate.Cells[aggregate.CurrentCell].ClearOpen
                && members.Length > 0
                && members.All(member => !aggregate.ProgressFor(member.CharacterId).IsSettled
                    && aggregate.DeadParticipants.Contains(member.CharacterId));
        }

        public async ValueTask<DungeonRunState?> TryEndPartyWipeAsync(DungeonRunId runId, DateTimeOffset now)
        {
            GameplayWriteAssertion.Writing(nameof(TimerService) + "." + nameof(TryEndPartyWipeAsync));
            var ended = TryEndPartyWipe(runId, now);
            if (ended is not null)
                foreach (var member in ended.Participants)
                    await this.lifecycle.RecordDepartureAsync(new(member.CharacterId, ended.RunId, ended.Layout.DungeonId)).ConfigureAwait(false);
            if (ended is not null && ended.Layout.AppliesFailureWeakness)
                foreach (var member in ended.Participants)
                    await this.weakness.For(member.CharacterId).SetWeakness().ConfigureAwait(false);
            return ended;
        }

        public async ValueTask<DungeonRunState?> TryEndClearReturnAsync(DungeonRunId runId, DateTimeOffset now)
        {
            GameplayWriteAssertion.Writing(nameof(TimerService) + "." + nameof(TryEndClearReturnAsync));
            var ended = TryEndClearReturn(runId, now);
            if (ended is not null)
                foreach (var member in ended.Participants)
                    await this.lifecycle.RecordDepartureAsync(new(member.CharacterId, ended.RunId, ended.Layout.DungeonId)).ConfigureAwait(false);
            return ended;
        }
        public IReadOnlyList<DungeonRoomMechanismView> AdvanceRoomMechanisms(DateTimeOffset now)
        {
            GameplayWriteAssertion.Writing(nameof(TimerService) + "." + nameof(AdvanceRoomMechanisms));
            lock (this.store.gate)
            {
                var views = new List<DungeonRoomMechanismView>();
                foreach (var run in this.store.runsById.Values)
                {
                    var cell = run.Cells[run.CurrentCell];
                    if (cell.Mechanisms.Count == 0 || cell.Mechanisms.Any(state => state.StartedAt is null)) continue;
                    cell.Mechanisms = cell.Mechanisms.Select(state => this.mechanismRules.Advance(state, now)).ToArray();
                    var recipients = run.Participants.Where(member => run.ReadyParticipants.Contains(member.CharacterId)).Select(member => member.CharacterId).ToArray();
                    if (recipients.Length > 0)
                        views.Add(new(run.RunId, run.CurrentCell, run.RoomVersion, cell.Mechanisms.ToArray(), recipients));
                }
                return views;
            }
        }
        public IReadOnlyList<LostLandCompletion> FindDueLostLandCompletions(DateTimeOffset now) =>
            this.store.runsById.Values.Where(run => run.LostLandExploration is { NoticeRegistered: false } state
                    && state.EndsAt <= now && (!state.IsEnding || run.ReadyParticipants.Count > 0))
                .Select(run => new LostLandCompletion(run.RunId, run.LostLandExploration!.EndsAt)).ToArray();

        public bool StageLostLandCompletion(LostLandCompletion completion, DateTimeOffset now, bool notifyReady)
        {
            GameplayWriteAssertion.Writing(nameof(TimerService) + "." + nameof(StageLostLandCompletion));
            if (!this.store.runsById.TryGetValue(completion.RunId, out var run)
                || run.LostLandExploration is not { NoticeRegistered: false } state
                || state.EndsAt != completion.EndsAt || now < state.EndsAt
                || notifyReady && run.ReadyParticipants.Count == 0 || state.IsEnding && !notifyReady) return false;
            var next = state with { IsEnding = true, NoticeRegistered = notifyReady };
            if (this.transactions.Current is { } transaction)
                transaction.Enlist((this, completion, "lost-land-completion"), () => new CompletionParticipant(this, run, state, next));
            else { run.LostLandExploration = next; run.RuntimeRevision++; }
            return true;
        }

        private sealed class CompletionParticipant(TimerService owner, DungeonRunAggregate run,
            LostLandExplorationState expected, LostLandExplorationState next) : ITransactionParticipant
        {
            public bool CommitCanFail => false;
            public ValueTask PrepareAsync(CancellationToken token)
            {
                if (owner.store.runsById.GetValueOrDefault(run.RunId) != run || run.LostLandExploration != expected)
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
            this.store.runsById.Values.Where(run => run.LostLandExploration?.IsEnding != true && run.LostLandFever?.ActiveUntil <= now)
                .Select(run => new LostLandFeverExpiry(run.RunId, run.LostLandFever!.ActiveUntil!.Value)).ToArray();

        public bool StageLostLandFeverExpiry(LostLandFeverExpiry expiry, DateTimeOffset now)
        {
            GameplayWriteAssertion.Writing(nameof(TimerService) + "." + nameof(StageLostLandFeverExpiry));
            if (expiry.ActiveUntil > now || !IsCurrentFeverExpiry(expiry)) return false;
            if (this.transactions.Current is { } transaction)
                transaction.Enlist((this, expiry, "lost-land-fever-expiry"), () => new FeverExpiryParticipant(this, expiry));
            else CommitFeverExpiry(expiry);
            return true;
        }

        private bool IsCurrentFeverExpiry(LostLandFeverExpiry expiry) =>
            this.store.runsById.TryGetValue(expiry.RunId, out var run)
                && run.LostLandFever?.ActiveUntil == expiry.ActiveUntil;

        private void CommitFeverExpiry(LostLandFeverExpiry expiry)
        {
            if (!IsCurrentFeverExpiry(expiry))
                throw new InvalidOperationException("Prepared LostLand fever expiry changed before publication.");
            var run = this.store.runsById[expiry.RunId];
            run.LostLandFever = new();
            run.RuntimeRevision++;
        }

        private sealed class FeverExpiryParticipant(TimerService owner, LostLandFeverExpiry expiry)
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
}
