using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

public interface IAccountMaterialRuleService
{
    IReadOnlyList<AccountMaterialDefinition> Definitions { get; }

    bool TryGetByItemId(int itemId, out AccountMaterialDefinition definition);

    AccountMaterialDefinition Get(AccountMaterialKind kind);
}
