using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class EpicCraftDomainService(IEpicBookRuleService rules, IEpicBookDomainService book,
    ISelectedSessionDomainService sessions, ICharacterTable characters,
    IDomainTransactionAccessor transactions, ICharacterBusinessInventoryDomainService bags,
    IOpeningEffectPlanRuleService plans, IPackageOpeningRuleService rewards, IOpeningInventoryDomainService inventory)
    : IEpicCraftDomainService
{
    public async ValueTask<EpicCraftReceipt> CraftAsync(SelectedSessionIdentity session, int equipmentId, DateTimeOffset now, CancellationToken ct)
    {
        _ = transactions.Current ?? throw new InvalidOperationException("Epic crafting requires a transaction.");
        if (!sessions.IsInWorld(session)
            || !await characters.BelongsToAsync(session.CharacterId, session.AccountId, ct).ConfigureAwait(false))
            throw new OpeningRejectedException(OpeningRejection.NoSession, "No current owned selection.");
        var recipe = rules.FindRecipe(equipmentId)
            ?? throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Unknown epic recipe.");
        var held = new List<OpeningHeldItem>();
        foreach (var space in new[] { BusinessInventorySpace.QuickSlot, BusinessInventorySpace.Consumable,
            BusinessInventorySpace.Material, BusinessInventorySpace.ExpertMaterial, BusinessInventorySpace.SharedMaterial })
        {
            var bag = bags.GetStore(session.CharacterId, BusinessInventoryStoreKind.Item).FindContainer(space);
            if (bag is not null) held.AddRange((await bag.GetAllAsync(ct).ConfigureAwait(false))
                .Select(x => new OpeningHeldItem(new(space, x.SlotIndex), x)));
        }
        var costs = plans.MaterialCosts(recipe.Materials, 1, held, now);
        var grants = await rewards.PrepareAsync([new(equipmentId, 1)], now, ct).ConfigureAwait(false);
        if (grants.Length != 1 || grants[0].Item.PremiumType != 0
            || grants[0].Item.Placement.WorldClass != ItemWorldClass.Equipment
            || grants[0].Item.Placement.Destination.Space != BusinessInventorySpace.CarriedEquipment)
            throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Epic output is not ordinary equipment.");
        var balance = await book.TrySpendPiecesAsync(session.AccountId, recipe.PieceId, recipe.PieceCount, ct).ConfigureAwait(false)
            ?? throw new OpeningRejectedException(OpeningRejection.MissingMaterial, "Insufficient epic pieces.");
        var effects = plans.MaterialCraftEffects(costs, grants[0]);
        var applied = await inventory.ApplyAsync(session, effects, now, ct).ConfigureAwait(false);
        return new(applied.Inventory, balance);
    }
}
