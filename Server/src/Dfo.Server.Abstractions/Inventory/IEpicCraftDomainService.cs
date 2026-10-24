using Dfo.Server.Abstractions.Effects;
using System.Collections.Immutable;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

public sealed record EpicCraftRecipe(int EquipmentId, int PieceId, int PieceCount, ImmutableArray<RecipeAmount> Materials);
public sealed record EpicCraftReceipt(OpeningInventoryReceipt Inventory, EpicPieceBalance Pieces);
public interface IEpicCraftDomainService
{
    ValueTask<EpicCraftReceipt> CraftAsync(SelectedSessionIdentity session, int equipmentId, DateTimeOffset now, IEffectFootprintRecorder footprint, CancellationToken ct);
}
