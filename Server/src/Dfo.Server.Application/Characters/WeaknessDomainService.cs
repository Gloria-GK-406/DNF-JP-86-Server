using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Characters;

internal sealed class WeaknessDomainService(
    ICharacterDomainService characters, IWeaknessRuleService rules,
    ILogger<WeaknessDomainService> logger,
    IDomainTransactionAccessor transactions) : IWeaknessDomainService
{
    private readonly ICharacterDomainService characterSource = characters;
    private readonly IWeaknessRuleService ruleSource = rules;
    private readonly IDomainTransactionAccessor? transactionSource = transactions;
    private readonly object gate = new();
    private readonly Dictionary<CharacterId, Entry> entries = [];
    // A failed level read must survive connection cleanup until a later global tick.
    private readonly Dictionary<CharacterId, PendingPenalty> pending = [];

    public async ValueTask<IReadOnlyList<CharacterId>> RetryPendingWeakness(CancellationToken cancellationToken)
    {
        GameplayWriteAssertion.Writing(nameof(WeaknessDomainService) + "." + nameof(RetryPendingWeakness));
        KeyValuePair<CharacterId, PendingPenalty>[] snapshot;
        lock (gate) snapshot = pending.ToArray();
        List<CharacterId> changed = [];
        foreach (var (id, penalty) in snapshot)
            if (await TryApplyAsync(id, penalty, cancellationToken).ConfigureAwait(false)) changed.Add(id);
        return changed;
    }

    private async ValueTask<bool> TryApplyAsync(CharacterId id, PendingPenalty penalty, CancellationToken token)
    {
        byte? initial;
        try
        {
            var progress = await characterSource.Get(id).ProgressAsync(token).ConfigureAwait(false);
            initial = progress is null ? null : ruleSource.InitialValue(progress.Level);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Weakness application deferred character={CharacterId}; retained for retry", id);
            return false;
        }
        lock (gate)
        {
            if (!pending.TryGetValue(id, out var current) || current != penalty) return false;
            pending.Remove(id);
            if (initial is { } value)
            {
                if (value == 100) entries.Remove(id);
                else entries[id] = new Entry(value, penalty.IsRecover);
            }
            return true;
        }
    }

    private sealed class PendingPenalty { public bool IsRecover { get; set; } = true; }

    public IWeakness For(CharacterId characterId) => new Weakness(this, characterId);
    public TimeSpan RecoveryInterval => ruleSource.RecoveryInterval;

    public IReadOnlyList<CharacterId> AdvanceRecovery()
    {
        GameplayWriteAssertion.Writing(nameof(WeaknessDomainService) + "." + nameof(AdvanceRecovery));
        lock (gate)
        {
            var changed = new List<CharacterId>();
            foreach (var (id, entry) in entries.ToArray())
            {
                if (!entry.IsRecover) continue;
                var value = ruleSource.RecoverStep(entry.Value);
                if (value == 100) entries.Remove(id);
                else entries[id] = entry with { Value = value };
                changed.Add(id);
            }
            return changed;
        }
    }

    private sealed record Entry(byte Value, bool IsRecover);

    private sealed class RecoveryCommit(WeaknessDomainService owner) : ITransactionParticipant
    {
        public HashSet<CharacterId> Characters { get; } = [];
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask RollbackAsync(CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask CommitAsync(CancellationToken token)
        {
            lock (owner.gate)
                foreach (var id in Characters) { owner.entries.Remove(id); owner.pending.Remove(id); }
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Weakness(WeaknessDomainService owner, CharacterId id) : IWeakness
    {
        public byte Value
        {
            get { lock (owner.gate) return owner.entries.TryGetValue(id, out var entry) ? entry.Value : (byte)100; }
        }

        public async ValueTask SetWeakness()
        {
            GameplayWriteAssertion.Writing(nameof(Weakness) + "." + nameof(SetWeakness));
            var penalty = new PendingPenalty();
            lock (owner.gate) owner.pending[id] = penalty;
            await owner.TryApplyAsync(id, penalty, CancellationToken.None).ConfigureAwait(false);
        }

        public async ValueTask<int> RecoveryGoldCost(CancellationToken cancellationToken = default)
        {
            if (Value == 100) return 0;
            // Free-recovery entitlement belongs here when the bonus-service domain is added.
            // Neither the caller nor the wire request can claim that entitlement.
            var progress = await owner.characterSource.Get(id).ProgressAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Cannot quote weakness recovery without character progression.");
            return owner.ruleSource.RecoveryGoldCost(progress.Level, Value);
        }

        public void Recover()
        {
            GameplayWriteAssertion.Writing(nameof(Weakness) + "." + nameof(Recover));
            if (owner.transactionSource?.Current is { } transaction)
            {
                transaction.Enlist(owner, () => new RecoveryCommit(owner)).Characters.Add(id);
                return;
            }
            lock (owner.gate) { owner.entries.Remove(id); owner.pending.Remove(id); }
        }
        public void PauseRecovery()
        {
            GameplayWriteAssertion.Writing(nameof(Weakness) + "." + nameof(PauseRecovery));
            SetRecovery(false);
        }
        public void ResumeRecovery()
        {
            GameplayWriteAssertion.Writing(nameof(Weakness) + "." + nameof(ResumeRecovery));
            SetRecovery(true);
        }
        private void SetRecovery(bool enabled)
        {
            lock (owner.gate)
            {
                if (owner.entries.TryGetValue(id, out var entry))
                    owner.entries[id] = entry with { IsRecover = enabled };
                if (owner.pending.TryGetValue(id, out var penalty)) penalty.IsRecover = enabled;
            }
        }
    }
}
