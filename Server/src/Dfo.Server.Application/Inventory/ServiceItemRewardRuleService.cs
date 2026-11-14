using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class ServiceItemRewardRuleService(IInventoryExpansionDefinitionSource expansions) : IServiceItemRewardRuleService
{
    public async ValueTask<bool> GrantsAsItemAsync(int itemId, CancellationToken ct) =>
        await expansions.ItemAsync(itemId, ct).ConfigureAwait(false) is not null;
}
