using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.Initialization;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Administration;
using Dfo.Server.Data.Inventory;
namespace Dfo.Server.Application.Administration;

internal sealed class GmDailyRefillDomainService(IGmAccessDomainService access, IDailyRefillConfigurationDomainService settings,
    IDailyRefillSettingsPersistence persistence, IGmAuditDomainService audit, IInitializationRuleService catalog,
    IItemPlacementRuleService placements, TimeProvider clock) : IGmDailyRefillDomainService
{
    private async ValueTask<GmIdentity> AuthorizeAsync(string secret, CancellationToken ct)
    {
        var actor = await access.IdentifyAsync(secret, ct) ?? throw new GmMailException(GmMailRefusal.Unauthorized, "会话已失效。");
        if (!actor.Permissions.HasFlag(GmPermission.All)) throw new GmMailException(GmMailRefusal.Forbidden, "仅全权限管理员可管理每日补给。");
        return actor;
    }
    public async ValueTask<DailyRefillList> ListAsync(string secret, CancellationToken ct)
    { await AuthorizeAsync(secret, ct); return await settings.ListAsync(clock.GetUtcNow(), ct); }
    public async ValueTask<DailyRefillList> ChangeAsync(string secret, DailyRefillChange change, CancellationToken ct)
    {
        await persistence.AcquireWriteAsync(ct);
        var actor = await AuthorizeAsync(secret, ct); var at = clock.GetUtcNow();
        var result = await settings.ChangeAsync(change, actor.LoginName, at, ct);
        await audit.AppendAsync(new(Guid.NewGuid().ToString("N"), at, "ChangeDailyRefill", actor.Id, actor.LoginName,
            $"每日补给：物品 {change.ItemId}", "Succeeded", $"操作 {change.Action}；版本 {result.Revision}；当天处理记录保留。"), ct);
        return result;
    }
    public async ValueTask<GmMailCatalogPage> SearchAsync(string secret, GmMailItemFilter filter, CancellationToken ct)
    {
        await AuthorizeAsync(secret, ct); var page = await catalog.SearchAsync(filter with { StackableOnly = true }, ct);
        var items = new List<GmMailCatalogEntry>();
        foreach (var item in page.Items) if (await placements.ClassifyAsync(item.ItemId, ct) == ItemWorldClass.Interchangeable) items.Add(item);
        return page with { Items = items };
    }
    public async ValueTask<GmMailItemDetails> ItemAsync(string secret, int id, CancellationToken ct)
    {
        await AuthorizeAsync(secret, ct); var details = await catalog.ItemAsync(id, ct);
        if (details.Kind != "stackable") throw new ArgumentException("每日补给仅支持永久普通背包堆叠物品。");
        return details;
    }
}
