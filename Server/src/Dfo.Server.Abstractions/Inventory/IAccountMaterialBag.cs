using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

public interface IAccountMaterialBagStore : IDomainStore<IAccountMaterialBagStoreSession>;

public interface IAccountMaterialBagStoreSession
{
    ValueTask<AccountMaterialBagState?> ReadAsync(
        AccountId accountId,
        CancellationToken cancellationToken);

    void Write(AccountMaterialBagState state);
}

public interface IAccountMaterialBag
{
    AccountId AccountId { get; }

    ValueTask<AccountMaterialChange?> ConsumeAsync(AccountMaterialKind kind, int count, CancellationToken cancellationToken);

    ValueTask<AccountMaterialBagState> SnapshotAsync(CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<AccountMaterialChange>> AddAsync(
        IReadOnlyDictionary<AccountMaterialKind, int> additions,
        CancellationToken cancellationToken);
}

public interface IAccountMaterialBagDomainService
{
    IAccountMaterialBag Get(AccountId accountId);
}

public interface IAccountMaterialRuleService
{
    IReadOnlyList<AccountMaterialDefinition> Definitions { get; }

    bool TryGetByItemId(int itemId, out AccountMaterialDefinition definition);

    AccountMaterialDefinition Get(AccountMaterialKind kind);
}

public abstract record ItemGrantOutcome
{
    private ItemGrantOutcome()
    {
    }

    public sealed record CharacterItem(ItemPlacementOutcome Placement) : ItemGrantOutcome;

    public sealed record AccountMaterial(AccountMaterialChange Change) : ItemGrantOutcome;
}

public interface IItemGrantDomainService
{
    ValueTask<ItemGrantOutcome> GrantAsync(
        AccountId accountId,
        IItemContainer characterContainer,
        int itemId,
        int count,
        CancellationToken cancellationToken);
}
