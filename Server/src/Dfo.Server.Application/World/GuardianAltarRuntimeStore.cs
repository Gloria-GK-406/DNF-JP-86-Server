using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World;

internal sealed record GuardianAltarRuntime(SelectedSessionIdentity Session, bool Selecting, GuardianAltarRunState? Run);

/// <summary>Exact selection ownership and transaction-local read-your-writes for this activity only.</summary>
internal sealed class GuardianAltarRuntimeStore(IDomainTransactionAccessor transactions, IDungeonDeadlineWake deadlines)
{
    private readonly Lock gate = new();
    private readonly IDungeonDeadlineWake wake = deadlines;
    private readonly Dictionary<CharacterId, GuardianAltarRuntime> states = [];
    private Changes? Current => transactions.Current is { } tx ? tx.Enlist(this, () => new Changes(this)) : null;
    public GuardianAltarRuntime? Find(CharacterId character)
    {
        if (Current is { } current) return current.Read(character);
        lock (gate) return states.GetValueOrDefault(character);
    }
    public IReadOnlyList<GuardianAltarRuntime> All() { lock (gate) return states.Values.ToArray(); }
    public void Put(CharacterId character, GuardianAltarRuntime? value)
    {
        GameplayWriteAssertion.Writing(nameof(GuardianAltarRuntimeStore) + "." + nameof(Put));
        if (transactions.Current is not { IsReadOnly: false }) throw new InvalidOperationException("Guardian altar requires a writable transaction.");
        Current!.Put(character, value);
    }
    private sealed class Changes(GuardianAltarRuntimeStore owner) : ITransactionParticipant
    {
        private readonly Dictionary<CharacterId, GuardianAltarRuntime?> before = [];
        private readonly Dictionary<CharacterId, GuardianAltarRuntime?> after = [];
        public bool SupportsReadOnly => true;
        public bool CommitCanFail => false;
        public GuardianAltarRuntime? Read(CharacterId id)
        {
            if (after.TryGetValue(id, out var value)) return value;
            lock (owner.gate) return owner.states.GetValueOrDefault(id);
        }
        public void Put(CharacterId id, GuardianAltarRuntime? value)
        {
            lock (owner.gate) before.TryAdd(id, owner.states.GetValueOrDefault(id));
            after[id] = value;
        }
        public ValueTask PrepareAsync(CancellationToken ct)
        {
            lock (owner.gate)
                if (before.Any(x => owner.states.GetValueOrDefault(x.Key) != x.Value))
                    throw new InvalidOperationException("Guardian altar ownership changed before commit.");
            return ValueTask.CompletedTask;
        }
        public ValueTask CommitAsync(CancellationToken ct)
        {
            lock (owner.gate) foreach (var (id, value) in after)
                if (value is null) owner.states.Remove(id); else owner.states[id] = value;
            if (after.Count > 0) owner.wake.Wake(DungeonDeadlineKind.GuardianAltar);
            return ValueTask.CompletedTask;
        }
        public ValueTask RollbackAsync(CancellationToken ct) => ValueTask.CompletedTask;
    }
}
