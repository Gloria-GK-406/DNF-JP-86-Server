using System.Collections.Immutable;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World;

internal sealed partial class DungeonRunStore
{
    internal sealed partial class SettlementService
    {
        public bool TryStageSecretShopPurchase(CharacterId characterId, DungeonRunId runId, int itemId, int count)
        {
            GameplayWriteAssertion.Writing(nameof(SettlementService) + "." + nameof(TryStageSecretShopPurchase));
            var transaction = this.transactions.Current
                ?? throw new InvalidOperationException("Secret shop stock requires a purchase transaction.");
            lock (this.store.gate)
            {
                if (count <= 0 || this.store.RunOf(characterId) is not { } run || run.RunId != runId
                    || run.ClearedAt is null || run.ProgressFor(characterId) is not { Released: false } progress)
                    return false;

                var items = progress.SecretShopItems;
                var item = items.FirstOrDefault(entry => entry.ItemId == itemId && entry.RemainingCount >= count);
                if (item is null)
                    return false;

                var updated = items.Replace(item, item with { RemainingCount = item.RemainingCount - count });
                var change = new SecretShopStockCommit(this.store, run, characterId, progress, items, updated);
                return ReferenceEquals(change, transaction.Enlist((this, runId, characterId), () => change));
            }
        }

        private sealed class SecretShopStockCommit(
            DungeonRunStore store, DungeonRunAggregate run, CharacterId characterId, ParticipantProgress progress,
            ImmutableArray<DungeonSecretShopItem> expected,
            ImmutableArray<DungeonSecretShopItem> updated) : ITransactionParticipant
        {
            public bool CommitCanFail => false;

            public ValueTask PrepareAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lock (store.gate)
                {
                    if (store.RunOf(characterId) != run || progress.Released || progress.SecretShopItems != expected)
                        throw new InvalidOperationException("Secret shop stock changed before purchase prepared.");
                }
                return ValueTask.CompletedTask;
            }

            public ValueTask CommitAsync(CancellationToken cancellationToken)
            {
                lock (store.gate)
                    progress.SecretShopItems = updated;
                return ValueTask.CompletedTask;
            }

            public ValueTask RollbackAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        }
    }
}
