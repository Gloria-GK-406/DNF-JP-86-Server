using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Pvp;

internal sealed class PvpVictoryEmblems(IDomainTransactionAccessor transactions, IItemDomainStore inventory)
{
    private const int Slot = 2;
    internal const int ItemId = 2;

    public async ValueTask<int?> ChangeAsync(CharacterId character, int amount, CancellationToken ct)
    {
        var transaction = transactions.Current ?? throw new InvalidOperationException("Duel currencies require a transaction.");
        var container = ContainerId.ForCharacter(character, InventoryContainerType.Main);
        var session = inventory.SessionIn(transaction);
        var current = await session.GetAsync(container, Slot, ct).ConfigureAwait(false);
        if (current is not null && current.Snapshot is not { ItemId: ItemId, IsPile: true })
            throw new InvalidDataException($"Character {character.Value} has an invalid victory emblem counter.");

        var balance = checked((current?.Count ?? 0) + amount);
        if (balance < 0) return null;
        if (current is null)
        {
            if (balance == 0) return 0;
            var item = await session.CreateAsync(new ItemSnapshot
            {
                ItemId = ItemId, LegacyKind = 0, Type = ItemTraits.None, IsPile = true, Count = balance,
            }, ct).ConfigureAwait(false);
            await session.PutAsync(container, Slot, item, ct).ConfigureAwait(false);
        }
        else if (balance == 0)
        {
            var empty = await session.TakeAsync(current, null, ct).ConfigureAwait(false);
            await session.DeleteItemAsync(empty, ct).ConfigureAwait(false);
        }
        else
        {
            await session.UpdateAsync(current, snapshot => snapshot with { Count = balance }, ct).ConfigureAwait(false);
        }
        return balance;
    }
}
