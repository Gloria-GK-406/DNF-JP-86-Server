using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class InventoryLevelRuleService : IInventoryLevelRuleService
{
    public bool IsValid(InventoryExpansionKind kind, int level, AccountCargoDefinition account) => kind switch
    {
        InventoryExpansionKind.PersonalCargo => level is >= 8 and <= 200 && (level - 8) % 16 == 0,
        InventoryExpansionKind.AccountCargo => level == 0 || account.Tiers.Any(t => t.Capacity == level),
        InventoryExpansionKind.Main => level is 0 or 8 or 16 or 24,
        InventoryExpansionKind.Avatar => level is >= 0 and <= 105 && level % 7 == 0,
        _ => false,
    };
}

internal sealed class InventoryLevelDomainService(ICharacterContainerStateTable characters, IAccountCargoStateTable accounts,
    ICharacterTable owners, IAccountTable accountOwners, IInventoryExpansionStateTable writes,
    IInventoryExpansionRuleService expansionRules, IInventoryLevelRuleService levels,
    IInventoryCapacityRuleService capacities, IItemDomainStore items, IDomainTransactionAccessor transactions)
    : IInventoryLevelDomainService
{
    public async ValueTask<int> GetLevelAsync(ContainerId container, CancellationToken ct)
    {
        var (_, type) = Owner(container);
        if (container.TryGetAccountOwner(out var account, out _))
            return (await accounts.ReadByAccountAsync(account, ct).ConfigureAwait(false)).SingleOrDefault()?.SelectionKey ?? 0;
        container.TryGetCharacterOwner(out var character, out _);
        return (await characters.ReadByCharacterAsync(character, ct).ConfigureAwait(false)).SingleOrDefault(r => r.ListType == (int)type)?.Value
            ?? (type == InventoryContainerType.PersonalCargo ? 8 : 0);
    }
    public async ValueTask SetLevelAsync(ContainerId container, int level, CancellationToken ct, int? expectedLevel = null)
    {
        var transaction = transactions.Current ?? throw new InvalidOperationException("Inventory levels require a domain transaction.");
        var (kind, type) = Owner(container);
        var definition = kind == InventoryExpansionKind.AccountCargo ? await expansionRules.AccountCargoAsync(ct).ConfigureAwait(false)
            : new AccountCargoDefinition(0, []);
        if (!levels.IsValid(kind, level, definition)) throw new InventoryExpansionException(InventoryExpansionRejection.InvalidStage);
        var accountOwned = container.TryGetAccountOwner(out var account, out _);
        container.TryGetCharacterOwner(out var character, out _);
        if (accountOwned ? await accountOwners.FindWalletAsync(account, ct).ConfigureAwait(false) is null
            : await owners.FindOwnerAsync(character, ct).ConfigureAwait(false) is null)
            throw new KeyNotFoundException("Inventory owner is unavailable.");
        var current = await GetLevelAsync(container, ct).ConfigureAwait(false);
        if (expectedLevel is not null && current != expectedLevel) throw new InventoryExpansionException(InventoryExpansionRejection.StateChanged);
        if (current == level) return;
        if (level < current)
        {
            foreach (var item in await items.SessionIn(transaction).GetAllAsync(container, ct).ConfigureAwait(false))
            {
                if (type == InventoryContainerType.Main && item.StorageSlotIndex < 3) continue;
                var slot = LegacyInventoryStorageCoordinates.ToBusiness(new(type, item.StorageSlotIndex), item.Snapshot.LegacyKind);
                if (slot.Index >= capacities.OpenSlotCount(slot.Space, level))
                    throw new InventoryExpansionException(InventoryExpansionRejection.OccupiedSlots);
            }
            if (accountOwned)
            {
                var gold = (await accounts.ReadByAccountAsync(account, ct).ConfigureAwait(false)).Single().Value32;
                var limit = definition.Tiers.SingleOrDefault(t => t.Capacity == level)?.GoldLimit ?? 0;
                if (gold < 0 || gold > limit) throw new InventoryExpansionException(InventoryExpansionRejection.GoldLimit);
            }
        }
        var changed = accountOwned ? await writes.ChangeAccountAsync(account, current, level, ct).ConfigureAwait(false)
            : await writes.ChangeCharacterAsync(character, type, current, level, ct).ConfigureAwait(false);
        if (!changed) throw new InventoryExpansionException(InventoryExpansionRejection.StateChanged);
    }
    private static (InventoryExpansionKind Kind, InventoryContainerType Type) Owner(ContainerId container)
    {
        if (container.TryGetAccountOwner(out _, out var accountType) && accountType == InventoryContainerType.AccountCargo)
            return (InventoryExpansionKind.AccountCargo, accountType);
        if (!container.TryGetCharacterOwner(out _, out var type)) throw new ArgumentException("Unsupported capacity owner.", nameof(container));
        return (type switch
        {
            InventoryContainerType.PersonalCargo => InventoryExpansionKind.PersonalCargo,
            InventoryContainerType.Main => InventoryExpansionKind.Main,
            InventoryContainerType.Avatar => InventoryExpansionKind.Avatar,
            _ => throw new ArgumentException("Unsupported capacity container.", nameof(container)),
        }, type);
    }
}
