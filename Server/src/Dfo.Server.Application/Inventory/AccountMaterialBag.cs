using System.Collections.Concurrent;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class AccountMaterialRuleService : IAccountMaterialRuleService
{
    private static readonly IReadOnlyDictionary<int, AccountMaterialDefinition> ByItemId =
        AccountMaterialCatalog.Definitions.ToDictionary(definition => definition.ItemId);

    private static readonly IReadOnlyDictionary<AccountMaterialKind, AccountMaterialDefinition> ByKind =
        AccountMaterialCatalog.Definitions.ToDictionary(definition => definition.Kind);

    public IReadOnlyList<AccountMaterialDefinition> Definitions => AccountMaterialCatalog.Definitions;

    public bool TryGetByItemId(int itemId, out AccountMaterialDefinition definition) =>
        ByItemId.TryGetValue(itemId, out definition!);

    public AccountMaterialDefinition Get(AccountMaterialKind kind) =>
        ByKind.TryGetValue(kind, out var definition)
            ? definition
            : throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown account material kind.");
}

internal sealed class AccountMaterialBag(
    AccountId accountId,
    IAccountMaterialBagStore store,
    IAccountMaterialRuleService rules,
    AmbientStoreAccess stores) : IAccountMaterialBag
{
    public AccountId AccountId { get; } = accountId.Value > 0
        ? accountId
        : throw new ArgumentOutOfRangeException(nameof(accountId));

    public async ValueTask<AccountMaterialBagState> SnapshotAsync(
        CancellationToken cancellationToken) =>
        await stores.ReadAsync(store, session => session.ReadAsync(this.AccountId, cancellationToken))
            .ConfigureAwait(false)
        ?? throw new InvalidDataException($"Account {this.AccountId.Value} has no material bag.");

    public async ValueTask<IReadOnlyList<AccountMaterialChange>> AddAsync(
        IReadOnlyDictionary<AccountMaterialKind, int> additions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(additions);
        if (additions.Count == 0)
            return [];

        List<AccountMaterialChange>? changes = null;
        await stores.ChangeAsync(store, async session =>
        {
            var current = await session.ReadAsync(this.AccountId, cancellationToken)
                    .ConfigureAwait(false)
                ?? throw new InvalidDataException(
                    $"Account {this.AccountId.Value} has no material bag.");
            var next = current;
            changes = [];
            foreach (var (kind, added) in additions.OrderBy(entry => entry.Key))
            {
                if (added <= 0)
                    throw new ArgumentOutOfRangeException(nameof(additions),
                        "Account material additions must be positive.");
                var definition = rules.Get(kind);
                var final = checked(current.Count(kind) + added);
                next = Set(next, kind, final);
                changes.Add(new(kind, definition.ItemId, definition.VirtualSlotIndex, added, final));
            }
            session.Write(next);
        }, cancellationToken).ConfigureAwait(false);
        return changes!;
    }

    public async ValueTask<AccountMaterialChange?> ConsumeAsync(AccountMaterialKind kind, int count, CancellationToken ct)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        var definition = rules.Get(kind);
        AccountMaterialChange? change = null;
        await stores.ChangeAsync(store, async session =>
        {
            var current = await session.ReadAsync(AccountId, ct)
                ?? throw new InvalidDataException($"Account {AccountId.Value} has no material bag.");
            if (current.Count(kind) < count) return;
            var final = current.Count(kind) - count;
            session.Write(Set(current, kind, final));
            change = new(kind, definition.ItemId, definition.VirtualSlotIndex, -count, final);
        }, ct);
        return change;
    }

    private static AccountMaterialBagState Set(
        AccountMaterialBagState state,
        AccountMaterialKind kind,
        int value) => kind switch
    {
        AccountMaterialKind.BlackCube => state with { BlackCube = value },
        AccountMaterialKind.WhiteCube => state with { WhiteCube = value },
        AccountMaterialKind.RedCube => state with { RedCube = value },
        AccountMaterialKind.BlueCube => state with { BlueCube = value },
        AccountMaterialKind.ClearCube => state with { ClearCube = value },
        AccountMaterialKind.GoldCube => state with { GoldCube = value },
        AccountMaterialKind.CommonSoul => state with { CommonSoul = value },
        AccountMaterialKind.AdvancedSoul => state with { AdvancedSoul = value },
        AccountMaterialKind.RareSoul => state with { RareSoul = value },
        AccountMaterialKind.UniqueSoul => state with { UniqueSoul = value },
        AccountMaterialKind.LegendarySoul => state with { LegendarySoul = value },
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown account material kind."),
    };
}

internal sealed class AccountMaterialBagDomainService(
    IAccountMaterialBagStore store,
    IAccountMaterialRuleService rules,
    AmbientStoreAccess stores) : IAccountMaterialBagDomainService
{
    private readonly ConcurrentDictionary<AccountId, IAccountMaterialBag> bags = [];

    public IAccountMaterialBag Get(AccountId accountId) => this.bags.GetOrAdd(
        accountId,
        id => new AccountMaterialBag(id, store, rules, stores));
}

internal sealed class ItemGrantDomainService(
    IAccountMaterialBagDomainService accountMaterials,
    IAccountMaterialRuleService accountMaterialRules) : IItemGrantDomainService
{
    public async ValueTask<ItemGrantOutcome> GrantAsync(
        AccountId accountId,
        IItemContainer characterContainer,
        int itemId,
        int count,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(characterContainer);
        if (accountMaterialRules.TryGetByItemId(itemId, out var definition))
        {
            var changes = await accountMaterials.Get(accountId).AddAsync(
                new Dictionary<AccountMaterialKind, int> { [definition.Kind] = count },
                cancellationToken).ConfigureAwait(false);
            return new ItemGrantOutcome.AccountMaterial(changes.Single());
        }

        return new ItemGrantOutcome.CharacterItem(
            await characterContainer.AddAsync(itemId, count, cancellationToken)
                .ConfigureAwait(false));
    }
}
