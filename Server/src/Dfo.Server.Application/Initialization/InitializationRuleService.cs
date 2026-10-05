using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.Initialization;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Administration;
using Dfo.Server.Data.Initialization;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Initialization;

internal sealed class InitializationRuleService(IInventoryExpansionRuleService expansion, IInventoryLevelRuleService levels,
    IExperienceRuleService experience, IGmMailRuleService itemRules, IItemPlacementRuleService placements,
    IInventoryCapacityRuleService capacities, IItemArrivalRuleService arrivals) : IInitializationRuleService
{
    private static bool Supports(BusinessInventorySpace space) => space is BusinessInventorySpace.CarriedEquipment or BusinessInventorySpace.Consumable
        or BusinessInventorySpace.Material or BusinessInventorySpace.Quest or BusinessInventorySpace.ExpertMaterial or BusinessInventorySpace.AvatarEmblem;
    public async ValueTask<GmMailCatalogPage> SearchAsync(GmMailItemFilter filter, CancellationToken ct)
    {
        var page = await itemRules.SearchItemsAsync(filter, ct);
        var supported = new List<GmMailCatalogEntry>();
        foreach (var item in page.Items)
            if (await placements.PlacementForAsync(item.ItemId, ct) is { } p && Supports(p.Destination.Space)) supported.Add(item);
        return page with { Items = supported };
    }
    public async ValueTask<GmMailItemDetails> ItemAsync(int itemId, CancellationToken ct)
    {
        var details = await itemRules.ItemDetailsAsync(itemId, ct);
        var placement = await placements.PlacementForAsync(itemId, ct);
        if (placement is null || !Supports(placement.Destination.Space)) throw new ArgumentException("此物品不是可初始化的普通背包物品。");
        return details;
    }
    private static readonly InitializationChoice[] Contracts = [new(22, "霸王之契约"), new(27, "达人之契约"), new(84, "成长之契约"), new(97, "晶之契约")];
    public static AccountInitialization DefaultAccount() => new(0, 0, 0, 0, []);
    public static CharacterInitialization DefaultCharacter() => new(1, 8, []);
    public async ValueTask<InitializationOptions> OptionsAsync(CancellationToken ct) => new(
        Math.Min(86, experience.Progression().LevelThresholds.Count + 1), [.. Contracts],
        Enumerable.Range(0, 13).Select(i => new InitializationChoice(8 + i * 16, $"{8 + i * 16} 格")).ToArray(),
        [new(0, "未开通"), .. (await expansion.AccountCargoAsync(ct)).Tiers.Select(t => new InitializationChoice(t.Capacity, $"{t.Capacity} 格"))]);
    public async ValueTask ValidateAsync(AccountInitialization input, CancellationToken ct)
    {
        if (input is null || input.Cera < 0 || input.TokenCera < 0 || input.HappyTokenCera < 0 || input.Contracts is null
            || input.Contracts.Length > Contracts.Length || input.Contracts.Any(c => c is null || c.Days is < 0 or > 3650 || !Contracts.Any(t => t.Value == c.Type))
            || input.Contracts.Select(c => c.Type).Distinct().Count() != input.Contracts.Length)
            throw new ArgumentException("点券必须为非负整数；契约不能重复，天数范围为 0—3650。");
        if (!levels.IsValid(InventoryExpansionKind.AccountCargo, input.AccountCargoLevel, await expansion.AccountCargoAsync(ct)))
            throw new ArgumentException("账号仓库档位无效。");
    }
    public async ValueTask ValidateAsync(CharacterInitialization input, CancellationToken ct)
    {
        if (input is null || input.Level < 1 || input.Level > Math.Min(86, experience.Progression().LevelThresholds.Count + 1)
            || !levels.IsValid(InventoryExpansionKind.PersonalCargo, input.PersonalCargoLevel, new(0, [])) || input.Items is null
            || input.Items.Length > 10 || input.Items.Any(i => i is null || i.ItemId <= 0 || i.Count <= 0)
            || input.Items.Select(i => i.ItemId).Distinct().Count() != input.Items.Length)
            throw new ArgumentException("角色等级、仓库档位或初始物品无效；最多 10 种物品，同种物品请合并数量。");
        // Validate each permanent template independently of mail attachment-count limits.
        var occupied = new HashSet<BusinessSlot>();
        foreach (var item in input.Items)
        {
            try { await ItemAsync(item.ItemId, ct); }
            catch (GmMailException e) { throw new ArgumentException(e.Message, e); }
            var p = await placements.PlacementForAsync(item.ItemId, ct) ?? throw new ArgumentException("物品没有可投递的位置。");
            if (!Supports(p.Destination.Space)) throw new ArgumentException("初始物品仅支持普通背包物品；账号共有材料请通过对应资产功能管理。");
            var open = Math.Min(capacities.OpenSlotCount(p.Destination.Space, 24), p.Destination.LastIndex + 1);
            if (open <= p.Destination.FirstIndex) throw new ArgumentException("初始物品没有可用背包位置。");
            p = p with { Destination = new(p.Destination.Space, p.Destination.FirstIndex, open - 1) };
            var piles = new List<ItemPile>();
            var remaining = item.Count;
            while (remaining > 0)
            {
                switch (arrivals.ForGrant(piles, remaining, p, occupied))
                {
                    case ItemArrival.IntoEmptySlot v:
                        occupied.Add(v.Slot); piles.Add(new(v.Slot, v.Count)); remaining -= v.Count; break;
                    case ItemArrival.OntoExistingPile v:
                        piles.RemoveAll(pile => pile.Slot == v.Slot); piles.Add(new(v.Slot, v.Total)); remaining -= v.Added; break;
                    default: throw new ArgumentException("初始物品超过新角色背包容量，请减少数量。");
                }
            }
        }
    }
}
