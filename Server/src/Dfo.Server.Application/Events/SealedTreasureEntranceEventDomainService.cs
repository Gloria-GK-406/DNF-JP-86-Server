using System.Text;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Initialization;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Events;

namespace Dfo.Server.Application.Events;

// Server configuration uses schedule 2331; A21 scheduled-dungeon projection uses dungeon 3557.
// Configuration remains owned by the existing activity subsystem, including explicit GM disable.
internal sealed class SealedTreasureEntranceEventDomainService(IEventPersistence store, ISealedTreasureRuleService rules) : IManagedEventDomainService
{
    public int EventId => rules.EventId;
    private EventConfiguration Initial => rules.InitialActivity;
    private async ValueTask<EventConfiguration> Configuration(CancellationToken ct) => await store.ConfigurationAsync(EventId, ct) ?? Initial;
    private static bool Active(EventConfiguration c, DateTimeOffset now) => c.Enabled && c.StartsAt <= now && c.EndsAt > now;

    public async ValueTask<ManagedEventEntry> DescribeAsync(DateTimeOffset now, CancellationToken ct)
    {
        var c = await Configuration(ct);
        var status = !c.Enabled ? "disabled" : c.StartsAt > now ? "scheduled" : c.EndsAt <= now ? "expired" : "active";
        return new(EventId, "sealed-treasure-entrance", c.Revision, c.SeasonId, c.Enabled, status, c.Title, c.ShortName,
            c.Description, c.StartNotice, c.EndNotice, c.StartsAt, c.EndsAt, true, null, c.UpdatedAt, c.UpdatedBy);
    }

    public async ValueTask<EventInfo[]> GetEventInfoEntriesAsync(AccountId account, DateTimeOffset now, CancellationToken ct)
    {
        var c = await Configuration(ct);
        if (account.Value <= 0 || !Active(c, now)) return [];
        return [new(EventId, c.Title, c.ShortName, c.Description, c.StartNotice, c.EndNotice,
            c.StartsAt!.Value, c.EndsAt!.Value, "")];
    }

    public async ValueTask ChangeAsync(EventChange change, string actor, DateTimeOffset now, CancellationToken ct)
    {
        if (change.EventId != EventId) throw new ArgumentException("活动身份不匹配。");
        await store.AcquireWriteAsync(ct);
        var c = await Configuration(ct);
        if (change.ExpectedRevision != c.Revision) throw new InitializationConflictException();
        if (change.Action == "configure")
        {
            if (change.StartsAt is not { } start || change.EndsAt is not { } end || start >= end
                || start.ToUnixTimeSeconds() < 0 || end.ToUnixTimeSeconds() > uint.MaxValue)
                throw new ArgumentException("活动有效期必须为完整且递增的时间范围。");
            c = c with { Title = Text(change.Title, 1, 80), ShortName = Text(change.ShortName, 1, 40),
                Description = Text(change.Description, 0, 1000), StartNotice = Text(change.StartNotice, 0, 200),
                EndNotice = Text(change.EndNotice, 0, 200), StartsAt = start, EndsAt = end };
        }
        else if (change.Action is "enable" or "disable")
        {
            if (change.Title is not null || change.ShortName is not null || change.Description is not null
                || change.StartNotice is not null || change.EndNotice is not null || change.StartsAt is not null || change.EndsAt is not null)
                throw new ArgumentException("启停操作不能携带配置字段。");
            if (change.Action == "enable" && (c.StartsAt is null || c.EndsAt is null))
                throw new ArgumentException("请先配置活动有效期。");
            c = c with { Enabled = change.Action == "enable" };
        }
        else throw new ArgumentException("入口活动仅支持配置与启停。");
        c = c with { Revision = checked(c.Revision + 1), UpdatedAt = now, UpdatedBy = actor };
        if (!await store.SaveAsync(EventId, change.ExpectedRevision, c, ct)) throw new InitializationConflictException();
    }

    private static string Text(string? value, int min, int max)
    {
        if (value is null || value.Trim().Length < min || value.Trim().Length > max || value.Contains('\0'))
            throw new ArgumentException("活动展示字段缺失或长度无效。");
        try { _ = new UTF8Encoding(false, true).GetByteCount(value); }
        catch (EncoderFallbackException e) { throw new ArgumentException("活动文案包含无效字符。", e); }
        return value.Trim();
    }
}
