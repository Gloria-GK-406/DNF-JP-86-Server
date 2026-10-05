using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Administration;
using Dfo.Server.Data.Inventory;
namespace Dfo.Server.Application.Administration;

internal sealed class GmDailyRefillDomainService(IGmAccessDomainService access, IDailyRefillConfigurationDomainService settings,
    IDailyRefillSettingsPersistence persistence, IGmAuditDomainService audit, IItemCatalogDomainService catalog,
    IDailyRefillConfigurationRuleService itemRules, TimeProvider clock) : IGmDailyRefillDomainService
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
    public async ValueTask<ItemCatalogPage> SearchAsync(string secret, ItemCatalogFilter filter, CancellationToken ct)
    { await AuthorizeAsync(secret, ct); return await catalog.SearchAsync(filter, itemRules.SelectionKey, ct); }
    public async ValueTask<ItemCatalogDetails> ItemAsync(string secret, int id, CancellationToken ct)
    { await AuthorizeAsync(secret, ct); return await catalog.ItemAsync(id, itemRules.SelectionKey, ct); }
}
