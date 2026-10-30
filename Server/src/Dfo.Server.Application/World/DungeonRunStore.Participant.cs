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
    internal sealed partial class ParticipantService
    {
        public bool HasUsedDungeonBuff(CharacterId characterId, DungeonRunId runId)
        {
            lock (this.store.gate)
                return !this.store.runsById.TryGetValue(runId, out var run)
                    || !run.IsMember(characterId) || run.BuffParticipants.Contains(characterId);
        }

        public bool StageDungeonBuff(CharacterId characterId, DungeonRunId runId)
        {
            GameplayWriteAssertion.Writing(nameof(ParticipantService) + "." + nameof(StageDungeonBuff));
            lock (this.store.gate)
            {
                if (HasUsedDungeonBuff(characterId, runId)) return false;
                var transaction = this.transactions.Current
                    ?? throw new InvalidOperationException("Dungeon buff consumption requires a transaction.");
                transaction.Enlist((this, runId, characterId, "premium-buff"),
                    () => new BuffCommitParticipant(this, this.store.runsById[runId], characterId));
                return true;
            }
        }

        private sealed class BuffCommitParticipant(ParticipantService owner,
            DungeonRunAggregate run, CharacterId characterId) : ITransactionParticipant
        {
            public bool CommitCanFail => false;
            public ValueTask PrepareAsync(CancellationToken token)
            {
                if (owner.HasUsedDungeonBuff(characterId, run.RunId))
                    throw new InvalidOperationException("Dungeon buff participant changed before commit.");
                return ValueTask.CompletedTask;
            }
            public ValueTask CommitAsync(CancellationToken token)
            {
                lock (owner.store.gate) run.BuffParticipants.Add(characterId);
                return ValueTask.CompletedTask;
            }
            public ValueTask RollbackAsync(CancellationToken token) => ValueTask.CompletedTask;
        }

        public bool StageRevival(CharacterId payer, CharacterId target, DungeonRunId runId)
        {
            GameplayWriteAssertion.Writing(nameof(ParticipantService) + "." + nameof(StageRevival));
            lock (this.store.gate)
            {
                if (!this.store.runsById.TryGetValue(runId, out var aggregate)
                    || !CanRevive(aggregate, payer, target)) return false;
                var transaction = this.transactions.Current
                    ?? throw new InvalidOperationException("Revival requires a transaction.");
                transaction.Enlist((this, runId, target, "revival"),
                    () => new RevivalCommitParticipant(this, aggregate, payer, target));
                return true;
            }
        }

        private static bool CanRevive(DungeonRunAggregate aggregate, CharacterId payer, CharacterId target) =>
            aggregate.IsMember(payer) && aggregate.IsMember(target)
            && aggregate.ReadyParticipants.Contains(payer) && aggregate.ReadyParticipants.Contains(target)
            && aggregate.DeadParticipants.Contains(target)
            && !aggregate.Cells[aggregate.CurrentCell].ClearOpen
            && !aggregate.ProgressFor(target).IsSettled;

        private sealed class RevivalCommitParticipant(ParticipantService owner,
            DungeonRunAggregate aggregate, CharacterId payer, CharacterId target) : ITransactionParticipant
        {
            public bool CommitCanFail => false;
            public ValueTask PrepareAsync(CancellationToken cancellationToken)
            {
                lock (owner.store.gate)
                {
                    if (!owner.store.runsById.TryGetValue(aggregate.RunId, out var current)
                        || current != aggregate || !CanRevive(aggregate, payer, target))
                        throw new InvalidOperationException("Revival target changed before commit.");
                }
                return ValueTask.CompletedTask;
            }
            public ValueTask CommitAsync(CancellationToken cancellationToken)
            {
                // Caller retains instance and admitted-session leases across the durable commit.
                lock (owner.store.gate)
                {
                    aggregate.DeadParticipants.Remove(target);
                    aggregate.RevivalCounts[target] = checked(aggregate.RevivalCounts.GetValueOrDefault(target) + 1);
                    aggregate.RevivalCoinPayments[payer] = checked(aggregate.RevivalCoinPayments.GetValueOrDefault(payer) + 1);
                    aggregate.Claims.WipeDeadline = null;
                    aggregate.RuntimeRevision++;
                }
                return ValueTask.CompletedTask;
            }
            public ValueTask RollbackAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        }

        public DungeonRunState? TryMarkParticipantDead(
            CharacterId characterId, DungeonRunId expectedRunId, long expectedRoomVersion)
        {
            GameplayWriteAssertion.Writing(nameof(ParticipantService) + "." + nameof(TryMarkParticipantDead));
            lock (this.store.gate)
            {
                if (this.store.RunOf(characterId) is not { } aggregate
                    || aggregate.RunId != expectedRunId
                    || aggregate.RoomVersion != expectedRoomVersion
                    || !aggregate.ReadyParticipants.Contains(characterId)
                    || !aggregate.DeadParticipants.Add(characterId))
                    return null;

                aggregate.RuntimeRevision++;
                return aggregate.ViewFor(characterId);
            }
        }
    }
}
