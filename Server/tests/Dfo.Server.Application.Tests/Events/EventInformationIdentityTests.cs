using Dfo.Server.Abstractions.Events;
using Dfo.Server.Application.Events;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Events;

namespace Dfo.Server.Application.Tests.Events;

public sealed class EventInformationIdentityTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);
    private static EventInfo Dependency(DateTimeOffset until) => new(69, "", "", "", "", "", At, until, "") { HasDetail = false };

    [Fact]
    public async Task Shared_identity_is_sent_once_even_when_owners_have_different_periods()
    {
        var service = new EventManagementDomainService([new Owner(2215, Dependency(At.AddDays(1))),
            new Owner(2330, Dependency(At.AddDays(2)))]);
        var list = await service.GetEventInfoListAsync(new(1), At, default);
        Assert.Single(list, x => x.EventId == 69);
    }

    [Fact]
    public async Task Conflicting_wire_content_is_not_silently_hidden_by_deduplication()
    {
        var service = new EventManagementDomainService([new Owner(2215, Dependency(At.AddDays(1))),
            new Owner(2330, Dependency(At.AddDays(1)) with { StartNotice = "different" })]);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await service.GetEventInfoListAsync(new(1), At, default));
    }

    private sealed class Owner(int id, EventInfo information) : IManagedEventDomainService
    {
        public int EventId => id;
        public ValueTask<EventInfo[]> GetEventInfoEntriesAsync(AccountId account, DateTimeOffset now, CancellationToken ct)
            => ValueTask.FromResult(new[] { information });
        public ValueTask<ManagedEventEntry> DescribeAsync(DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask ChangeAsync(EventChange change, string actor, DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();
    }
}
