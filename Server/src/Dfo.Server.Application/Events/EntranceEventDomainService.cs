using System.Text;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Initialization;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Events;

namespace Dfo.Server.Application.Events;

/// <summary>
/// The GM switch of one dungeon entrance activity: configuration, open period and its EVENT_INFO projection.
/// An entrance only differs in its identity, its initial configuration and what it projects while open.
/// </summary>
internal abstract class EntranceEventDomainService(IEventPersistence store) : IManagedEventDomainService
{
    public abstract int EventId { get; }
    /// <summary>The GM page key of this entrance.</summary>
    protected abstract string Key { get; }
    protected abstract EventConfiguration Initial { get; }
    /// <summary>The EVENT_INFO entries while the activity is open; the configured period is complete then.</summary>
    protected abstract EventInfo[] Project(EventConfiguration c);

    /// <summary>The ordinary detailed entry with this entrance's detail link.</summary>
    protected EventInfo Detailed(EventConfiguration c, string link) => new(EventId, c.Title, c.ShortName, c.Description,
        c.StartNotice, c.EndNotice, c.StartsAt!.Value, c.EndsAt!.Value, link);

    private async ValueTask<EventConfiguration> Configuration(CancellationToken ct) => await store.ConfigurationAsync(EventId, ct) ?? Initial;
    private static bool Active(EventConfiguration c, DateTimeOffset now) => c.Enabled && c.StartsAt <= now && c.EndsAt > now;
    public async ValueTask<bool> IsActiveAsync(DateTimeOffset now, CancellationToken ct) => Active(await Configuration(ct), now);

    public async ValueTask<ManagedEventEntry> DescribeAsync(DateTimeOffset now, CancellationToken ct)
    {
        var c = await Configuration(ct);
        var status = !c.Enabled ? "disabled" : c.StartsAt > now ? "scheduled" : c.EndsAt <= now ? "expired" : "active";
        return new(EventId, Key, c.Revision, c.SeasonId, c.Enabled, status, c.Title, c.ShortName,
            c.Description, c.StartNotice, c.EndNotice, c.StartsAt, c.EndsAt, true, null, c.UpdatedAt, c.UpdatedBy);
    }

    public async ValueTask<EventInfo[]> GetEventInfoEntriesAsync(AccountId account, DateTimeOffset now, CancellationToken ct)
    {
        var c = await Configuration(ct);
        return account.Value > 0 && Active(c, now) ? Project(c) : [];
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
