using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.Characters;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Characters;

internal sealed class WeaknessDomainService(
    Func<ICharacterDomainService> characters, IWeaknessRuleService rules,
    ILogger<WeaknessDomainService>? logger = null) : IWeaknessDomainService
{
    private readonly object gate = new();
    private readonly Dictionary<CharacterId, Entry> entries = [];
    // A failed level read must survive connection cleanup until a later global tick.
    private readonly Dictionary<CharacterId, PendingPenalty> pending = [];

    public async ValueTask<IReadOnlyList<CharacterId>> RetryPendingWeakness(CancellationToken cancellationToken)
    {
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
            var progress = await characters().Get(id).ProgressAsync(token).ConfigureAwait(false);
            initial = progress is null ? null : rules.InitialValue(progress.Level);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger?.LogWarning(exception, "Weakness application deferred character={CharacterId}; retained for retry", id);
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
    public TimeSpan RecoveryInterval => rules.RecoveryInterval;

    public IReadOnlyList<CharacterId> AdvanceRecovery()
    {
        lock (gate)
        {
            var changed = new List<CharacterId>();
            foreach (var (id, entry) in entries.ToArray())
            {
                if (!entry.IsRecover) continue;
                var value = rules.RecoverStep(entry.Value);
                if (value == 100) entries.Remove(id);
                else entries[id] = entry with { Value = value };
                changed.Add(id);
            }
            return changed;
        }
    }

    private sealed record Entry(byte Value, bool IsRecover);

    private sealed class Weakness(WeaknessDomainService owner, CharacterId id) : IWeakness
    {
        public byte Value
        {
            get { lock (owner.gate) return owner.entries.TryGetValue(id, out var entry) ? entry.Value : (byte)100; }
        }

        public async ValueTask SetWeakness()
        {
            var penalty = new PendingPenalty();
            lock (owner.gate) owner.pending[id] = penalty;
            await owner.TryApplyAsync(id, penalty, CancellationToken.None).ConfigureAwait(false);
        }

        public void Recover()
        {
            lock (owner.gate) { owner.entries.Remove(id); owner.pending.Remove(id); }
        }
        public void PauseRecovery() => SetRecovery(false);
        public void ResumeRecovery() => SetRecovery(true);
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
