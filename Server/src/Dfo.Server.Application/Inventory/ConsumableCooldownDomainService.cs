using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Inventory;

internal sealed class ConsumableCooldownDomainService(IConsumableCooldownTable table,
    IOnlineCharacterRuntimeDomainService runtime, IDomainTransactionAccessor transactions)
{
    public ValueTask<bool> IsActiveAsync(CharacterId character, ItemCooldownPolicy? policy, DateTimeOffset at, CancellationToken ct) =>
        policy?.Scope switch
        {
            ItemCooldownScope.Persistent => table.IsActiveAsync(character, policy.Value.Key, at, ct),
            ItemCooldownScope.SelectedSession => ValueTask.FromResult(runtime.IsCoolingDown(character, policy.Value.Key)),
            _ => ValueTask.FromResult(false),
        };

    public async ValueTask StartAsync(SelectedSessionIdentity session, int itemId, ItemCooldownPolicy policy,
        DateTimeOffset at, CancellationToken ct)
    {
        var transaction = transactions.Current ?? throw new InvalidOperationException("Item cooldown requires a transaction.");
        var expiresAt = at.Add(policy.Duration);
        if (policy.Scope == ItemCooldownScope.SelectedSession)
        {
            transaction.Enlist(new object(), () => new SessionCooldown(runtime, session, policy.Key, policy.Duration));
            return;
        }

        if (!await table.TryStartAsync(session.CharacterId, new(policy.Key, itemId, expiresAt), at, ct).ConfigureAwait(false))
            throw new ItemUsageRejectedException(ItemUsageRejection.CoolingDown);
    }

    private sealed class SessionCooldown(IOnlineCharacterRuntimeDomainService runtime, SelectedSessionIdentity session,
        ItemCooldownKey key, TimeSpan duration) : ITransactionParticipant
    {
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask RollbackAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask CommitAsync(CancellationToken ct)
        {
            runtime.StartCooldown(session, key, duration);
            return ValueTask.CompletedTask;
        }
    }
}
