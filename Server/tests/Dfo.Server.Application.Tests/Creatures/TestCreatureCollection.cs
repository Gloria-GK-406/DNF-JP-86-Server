using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Creatures;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Tests.Creatures;

internal static class TestCreatureCollection
{
    public static ICreatureCollection Create(CharacterId owner, IDomainTransaction tx, ICreatureStore store,
        Action<CreatureState> write, ICreatureRuleService rules) =>
        new CreatureCollection(() => new CreatureInstanceContext(owner, tx,
            new Session(store, write), store, rules, TimeProvider.System));

    private sealed class Session(ICreatureStore store, Action<CreatureState> write) : ICreatureDomainStoreSession
    {
        public ValueTask<CreatureState?> ReadAsync(CharacterId owner, int uid, CancellationToken ct) => store.FindAsync(uid, ct);
        public ValueTask<IReadOnlyList<CreatureState>> ReadAllAsync(CharacterId owner, CancellationToken ct) => store.ReadAllAsync(ct);
        public ValueTask<CreatureState?> ReadCurrentAsync(CharacterId owner, CancellationToken ct) => store.CurrentAsync(ct);
        public void Write(CharacterId owner, CreatureState state) => write(state);
        public void ReviveIfEmpty(CharacterId owner, int uid) => throw new NotSupportedException();
    }
}
