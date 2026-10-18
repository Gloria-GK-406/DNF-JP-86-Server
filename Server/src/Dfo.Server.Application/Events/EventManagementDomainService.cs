using Dfo.Server.Abstractions.Events;
using Dfo.Server.Data.Events;
using Dfo.Server.Data.Accounts;
namespace Dfo.Server.Application.Events;

internal sealed class EventManagementDomainService(IEnumerable<IManagedEventDomainService> registered) : IEventManagementDomainService
{
    private readonly IManagedEventDomainService[] events = Registered(registered);
    private static IManagedEventDomainService[] Registered(IEnumerable<IManagedEventDomainService> all)
    {
        var result = all.OrderBy(e => e.EventId).ToArray();
        if (result.GroupBy(e => e.EventId).Any(g => g.Count()>1)) throw new InvalidOperationException("Duplicate registered event identity.");
        return result;
    }
    public async ValueTask<ManagedEventList> ListAsync(DateTimeOffset now,CancellationToken ct)
    {
        var entries = new List<ManagedEventEntry>();
        foreach (var e in events) entries.Add(await e.DescribeAsync(now,ct));
        return new([..entries]);
    }
    public async ValueTask<EventInfo[]> GetEventInfoListAsync(AccountId account,DateTimeOffset now,CancellationToken ct)
    {
        var entries = new List<EventInfo>();
        foreach (var e in events) entries.AddRange(await e.GetEventInfoEntriesAsync(account,now,ct));
        return [..entries];
    }
    public async ValueTask<ManagedEventList> ChangeAsync(EventChange change,string actor,DateTimeOffset now,CancellationToken ct)
    {
        var e = events.SingleOrDefault(e => e.EventId==change.EventId) ?? throw new ArgumentException("活动未注册。");
        await e.ChangeAsync(change,actor,now,ct);
        return await ListAsync(now,ct);
    }
}
