using Dfo.Server.Abstractions.Execution;
using Microsoft.Extensions.Logging.Abstractions;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.World;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.World;

internal sealed partial class DungeonRunDomainService
{
    public bool HasUsedDungeonBuff(CharacterId characterId, DungeonRunId runId)
    {
        lock (this.gate)
            return !this.runsById.TryGetValue(runId, out var run)
                || !run.IsMember(characterId) || run.BuffParticipants.Contains(characterId);
    }

    public bool StageDungeonBuff(CharacterId characterId, DungeonRunId runId)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(StageDungeonBuff));
        lock (this.gate)
        {
            if (HasUsedDungeonBuff(characterId, runId)) return false;
            var transaction = this.transactions.Current
                ?? throw new InvalidOperationException("Dungeon buff consumption requires a transaction.");
            transaction.Enlist((this, runId, characterId, "premium-buff"),
                () => new BuffCommitParticipant(this, this.runsById[runId], characterId));
            return true;
        }
    }

    private sealed class BuffCommitParticipant(DungeonRunDomainService owner,
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
            lock (owner.gate) run.BuffParticipants.Add(characterId);
            return ValueTask.CompletedTask;
        }
        public ValueTask RollbackAsync(CancellationToken token) => ValueTask.CompletedTask;
    }

    public bool StageRevival(CharacterId payer, CharacterId target, DungeonRunId runId)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(StageRevival));
        lock (this.gate)
        {
            if (!this.runsById.TryGetValue(runId, out var aggregate)
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

    private sealed class RevivalCommitParticipant(DungeonRunDomainService owner,
        DungeonRunAggregate aggregate, CharacterId payer, CharacterId target) : ITransactionParticipant
    {
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken cancellationToken)
        {
            lock (owner.gate)
            {
                if (!owner.runsById.TryGetValue(aggregate.RunId, out var current)
                    || current != aggregate || !CanRevive(aggregate, payer, target))
                    throw new InvalidOperationException("Revival target changed before commit.");
            }
            return ValueTask.CompletedTask;
        }
        public ValueTask CommitAsync(CancellationToken cancellationToken)
        {
            // Caller retains instance and admitted-session leases across the durable commit.
            lock (owner.gate)
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
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(TryMarkParticipantDead));
        lock (this.gate)
        {
            if (this.RunOf(characterId) is not { } aggregate
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
