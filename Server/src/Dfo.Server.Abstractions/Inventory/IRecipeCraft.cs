using System.Collections.Immutable;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

public sealed record RecipeAmount(int ItemId, int Count);
public sealed record RecipePlan(ImmutableArray<RecipeAmount> Required, ImmutableArray<RecipeAmount> Produced);
public interface IRecipeContentSource
{
    ValueTask<RecipePlan?> GetAsync(int drawingId, CancellationToken cancellationToken);
}
/// <summary>Only returns the drawing's requirements and outputs; never reads or writes a player's assets.</summary>
public interface IRecipeCraftRuleService
{
    ValueTask<RecipePlan> GetResultAsync(int drawingId, CancellationToken cancellationToken);
}
public sealed record RecipeCraftReceipt(OpeningInventoryReceipt Inventory,
    ImmutableArray<OpeningSlotConsumption> Consumed);
public interface IRecipeCraftDomainService
{
    ValueTask<RecipeCraftReceipt> RefreshAsync(RecipeCraftReceipt receipt, CancellationToken cancellationToken);
    ValueTask<RecipeCraftReceipt> CraftAsync(SelectedSessionIdentity session, BusinessSlot drawing,
        int quantity, DateTimeOffset now, CancellationToken cancellationToken);
}
