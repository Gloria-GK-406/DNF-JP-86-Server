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
