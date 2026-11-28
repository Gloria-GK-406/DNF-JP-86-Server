using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

/// <param name="TransferredAttribute">For a limited-transfer stack, the attribute of the portion after transfer.</param>
public readonly record struct PlayerTradeItemDecision(bool Allowed, byte? TransferredAttribute = null);

/// <summary>Decides whether a held item can change owners through direct player trade.</summary>
public interface IPlayerTradeItemRuleService
{
    ValueTask<PlayerTradeItemDecision> CanTradeAsync(ItemSnapshot item, BusinessInventorySpace space,
        DateTimeOffset now, CancellationToken cancellationToken);
}
