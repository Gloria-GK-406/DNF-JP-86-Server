using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.Initialization;
using Dfo.Server.Data.Administration;
using Dfo.Server.Data.Initialization;
namespace Dfo.Server.Application.Administration;
internal sealed class GmInitializationDomainService(IGmAccessDomainService access, IInitializationDomainService initialization,
    IGmAuditDomainService audit, IInitializationRuleService items, IItemCatalogDomainService catalog, IInitializationPersistence persistence, TimeProvider clock) : IGmInitializationDomainService
{
    private async ValueTask<GmIdentity> AuthorizeAsync(string secret, CancellationToken ct)
    {
        var actor = await access.IdentifyAsync(secret, ct) ?? throw new GmMailException(GmMailRefusal.Unauthorized, "会话已失效。");
        if (!actor.Permissions.HasFlag(GmPermission.All)) throw new GmMailException(GmMailRefusal.Forbidden, "仅全权限管理员可管理初始化配置。");
        return actor;
    }
    public async ValueTask<InitializationView> ReadAsync(string secret, CancellationToken ct)
    {
        await AuthorizeAsync(secret, ct);
        return new(await initialization.AccountAsync(ct), await initialization.CharacterAsync(ct), await initialization.OptionsAsync(ct));
    }
    public async ValueTask<object> SaveAsync(string secret, SaveInitializationRequest<AccountInitialization>? account,
        SaveInitializationRequest<CharacterInitialization>? character, CancellationToken ct)
    {
        await persistence.AcquireWriteAsync(ct);
        var actor = await AuthorizeAsync(secret, ct);
        if ((account is null) == (character is null)) throw new ArgumentException("请选择一种初始化配置。");
        object result = account is not null ? await initialization.SaveAsync(account, actor.LoginName, ct)
            : await initialization.SaveAsync(character!, actor.LoginName, ct);
        await audit.AppendAsync(new(Guid.NewGuid().ToString("N"), clock.GetUtcNow(), "SaveInitialization", actor.Id, actor.LoginName,
            account is not null ? "账号初始化" : "角色初始化", "Succeeded", $"保存并生效，版本 {(account?.ExpectedRevision ?? character!.ExpectedRevision) + 1}；仅影响后续创建。"), ct);
        return result;
    }
    public async ValueTask<ItemCatalogPage> SearchAsync(string secret, ItemCatalogFilter filter, CancellationToken ct)
    { await AuthorizeAsync(secret, ct); return await catalog.SearchAsync(filter, items.SelectionKey, ct); }
    public async ValueTask<ItemCatalogDetails> ItemAsync(string secret, int itemId, CancellationToken ct)
    { await AuthorizeAsync(secret, ct); return await catalog.ItemAsync(itemId, items.SelectionKey, ct); }
}
