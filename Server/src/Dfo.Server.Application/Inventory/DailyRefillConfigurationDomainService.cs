using System.Text.Json;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Initialization;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Inventory;
namespace Dfo.Server.Application.Inventory;

internal sealed class DailyRefillConfigurationDomainService(IDailyRefillSettingsPersistence persistence,
    IDailyRefillConfigurationRuleService rules,
    IDomainTransactionAccessor transactions) : IDailyRefillConfigurationDomainService
{
    private async ValueTask<(long Revision, DailyRefillSettings Input, DateTimeOffset? At, string? Actor)> ReadAsync(CancellationToken ct)
    {
        var row = await persistence.ReadAsync(ct);
        return row is null ? (0, new([], []), null, null) : (row.Revision,
            JsonSerializer.Deserialize<DailyRefillSettings>(row.Input) ?? throw new InvalidDataException("Invalid daily refill settings."), row.UpdatedAt, row.UpdatedBy);
    }
    private static string Status(int quantity, DateTimeOffset? expiry, DateTimeOffset at) =>
        expiry <= at ? "expired" : quantity <= 0 ? "inactive" : "active";
    public async ValueTask<DailyRefillList> ListAsync(DateTimeOffset at, CancellationToken ct)
    {
        var doc = await ReadAsync(ct);
        var entries = new List<DailyRefillEntry>();
        foreach (var rule in await rules.DeclaredPvfAsync(at, ct))
        {
            var enabled = !doc.Input.DisabledPvf.Contains(rule.ItemId);
            var status = !enabled ? "disabled" : doc.Input.Custom.Any(c => c.ItemId == rule.ItemId) ? "overridden" : Status(rule.Quantity, rule.ExpiresAt, at);
            entries.Add(new(rule.ItemId, await rules.ItemNameAsync(rule.ItemId, ct), "pvf", rule.Quantity, (int)rule.Mode, rule.ExpiresAt, status, enabled));
        }
        foreach (var rule in doc.Input.Custom)
        {
            var status = Status(rule.Quantity, rule.ExpiresAt, at);
            if (status == "active" && await rules.TryResolveCustomAsync(rule, ct) is null) status = "unsupported";
            entries.Add(new(rule.ItemId, await rules.ItemNameAsync(rule.ItemId, ct), "custom", rule.Quantity, rule.Mode, rule.ExpiresAt, status, true));
        }
        return new(doc.Revision, [.. entries.OrderBy(e => e.ItemId).ThenBy(e => e.Source)], doc.At, doc.Actor);
    }
    public async ValueTask<DailyRefillList> ChangeAsync(DailyRefillChange change, string actor, DateTimeOffset at, CancellationToken ct)
    {
        if (transactions.Current is null) throw new InvalidOperationException("Daily refill settings require a domain transaction.");
        if (change is null || change.ItemId <= 0 || change.ExpectedRevision < 0) throw new ArgumentException("补给参数无效。");
        await persistence.AcquireWriteAsync(ct);
        var doc = await ReadAsync(ct);
        if (doc.Revision != change.ExpectedRevision) throw new InitializationConflictException();
        var custom = doc.Input.Custom.ToList(); var disabled = doc.Input.DisabledPvf.ToHashSet();
        var index = custom.FindIndex(r => r.ItemId == change.ItemId);
        switch (change.Action)
        {
            case "add":
            case "update":
                if (change.Action == "add" && index >= 0) throw new ArgumentException("此物品已有自定义补给，请编辑已有规则。");
                if (change.Action == "update" && index < 0) throw new ArgumentException("自定义补给已不存在，请刷新列表。");
                if (change.Quantity is null or <= 0 || change.Mode is not (0 or 1) || change.ExpiresAt <= at)
                    throw new ArgumentException("数量须为正整数，类型只能为补足或添加，截止时间须晚于当前时间。");
                if (index < 0 && custom.Count >= 100) throw new ArgumentException("最多配置 100 条自定义补给。");
                var rule = new CustomDailyRefill(change.ItemId, change.Quantity.Value, change.Mode.Value, change.ExpiresAt);
                await rules.ResolveCustomAsync(rule, ct);
                if (index < 0) custom.Add(rule); else custom[index] = rule;
                break;
            case "delete":
                if (index < 0) throw new ArgumentException("自定义补给已不存在，请刷新列表。");
                custom.RemoveAt(index); break;
            case "pvf":
                if (change.Enabled is null || !(await rules.DeclaredPvfAsync(at, ct)).Any(r => r.ItemId == change.ItemId))
                    throw new ArgumentException("PVF 补给不存在或开关无效。");
                if (change.Enabled.Value) disabled.Remove(change.ItemId); else disabled.Add(change.ItemId);
                break;
            default: throw new ArgumentException("未知补给操作。");
        }
        if (!await persistence.SaveAsync(doc.Revision, JsonSerializer.Serialize(new DailyRefillSettings([.. custom.OrderBy(r => r.ItemId)], [.. disabled.Order()])), at, actor, ct))
            throw new InitializationConflictException();
        return await ListAsync(at, ct);
    }
    public async ValueTask<IReadOnlyList<DailyRefillRule>> EffectiveAsync(DateTimeOffset at, CancellationToken ct)
    {
        var doc = await ReadAsync(ct);
        var effective = (await rules.ActivePvfAsync(at, ct)).Where(r => !doc.Input.DisabledPvf.Contains(r.ItemId)
            && !doc.Input.Custom.Any(c => c.ItemId == r.ItemId)).ToList();
        foreach (var rule in doc.Input.Custom.Where(r => r.ExpiresAt is null || r.ExpiresAt > at))
            if (await rules.TryResolveCustomAsync(rule, ct) is { } resolved) effective.Add(resolved);
        // Stable order makes scarce-slot allocation predictable and prevents duplicate PVF declarations from granting twice.
        return effective.GroupBy(r => r.ItemId).Select(g => g.First()).OrderBy(r => r.ItemId).ToArray();
    }
}
