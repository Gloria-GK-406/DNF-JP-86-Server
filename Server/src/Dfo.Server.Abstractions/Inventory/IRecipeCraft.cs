using Dfo.Server.Abstractions.Effects;
using System.Collections.Immutable;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

public sealed record RecipeAmount(int ItemId, int Count);
public sealed record RecipePlan(ImmutableArray<RecipeAmount> Required, ImmutableArray<RecipeAmount> Produced)
{
    /// <summary>Probabilistic recipes (<c>[success rate]</c>); null for ordinary recipes, which always succeed.</summary>
    public RecipeChance? Chance { get; init; }
}
/// <param name="SuccessPerTenThousand"><c>[success rate]</c>, read as parts per ten thousand (user decision 2026-11-07).</param>
/// <param name="FailOutputs"><c>[fail output]</c>: what a failed attempt yields instead of the products.</param>
/// <param name="MaintainAbility"><c>[maintain ability] normal</c>: an equipment input passes its upgrades to the product.</param>
public sealed record RecipeChance(int SuccessPerTenThousand, ImmutableArray<RecipeAmount> FailOutputs, bool MaintainAbility);

/// <summary>
/// A probabilistic recipe that upgrades one equipment piece into another template (requiem → liberated drawings):
/// the piece is an input, changes template in place on success and is handed back unchanged on failure.
/// </summary>
/// <param name="FailGrants">The fail outputs other than the returned piece.</param>
public sealed record RecipeEquipmentUpgrade(int SourceItemId, int OutputItemId, bool OutputSealed, ushort? OutputMaxDurability,
    ushort? SourceMaxDurability, int SuccessPerTenThousand, ImmutableArray<RecipeAmount> FailGrants);

/// <summary>Draws a value in 0..9999 for one probabilistic recipe attempt.</summary>
public interface IRecipeChanceRollSource
{
    int Next();
}
public interface IRecipeContentSource
{
    ValueTask<RecipePlan?> GetAsync(int drawingId, CancellationToken cancellationToken);
}
/// <summary>Only returns the drawing's requirements and outputs; never reads or writes a player's assets.</summary>
public interface IRecipeCraftRuleService
{
    ValueTask<RecipePlan> GetResultAsync(int drawingId, CancellationToken cancellationToken);
    /// <summary>The equipment-upgrade reading of a probabilistic plan; throws when the plan is not one this server supports.</summary>
    ValueTask<RecipeEquipmentUpgrade> EquipmentUpgradeAsync(RecipePlan plan, CancellationToken cancellationToken);
    /// <summary>Whether this piece may be the equipment input; <see cref="OpeningRejection.None"/> when it may.</summary>
    OpeningRejection AdmitUpgradeInput(RecipeEquipmentUpgrade upgrade, ItemSnapshot piece);
}
public sealed record RecipeCraftReceipt(OpeningInventoryReceipt Inventory,
    ImmutableArray<OpeningSlotConsumption> Consumed)
{
    /// <summary>The equipment piece a successful upgrade recipe converted in place; its product row is display-only.</summary>
    public BusinessSlot? Upgraded { get; init; }
}
public interface IRecipeCraftDomainService
{
    ValueTask<RecipeCraftReceipt> RefreshAsync(RecipeCraftReceipt receipt, CancellationToken cancellationToken);
    ValueTask<RecipeCraftReceipt> CraftAsync(SelectedSessionIdentity session, BusinessSlot drawing,
        int quantity, DateTimeOffset now, IEffectFootprintRecorder footprint, CancellationToken cancellationToken);
}
