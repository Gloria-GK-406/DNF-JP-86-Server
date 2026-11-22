using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Application.Events;
using Dfo.Server.Application.Raids;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Events;

namespace Dfo.Server.Application.Tests.Events;

[Trait("Category", "Unit")]
public sealed class EventInformationIdentityTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);
    private static EventInfo Dependency(DateTimeOffset until) => new(69, "", "", "", "", "", At, until, "") { HasDetail = false };

    [Fact]
    public async Task Shared_identity_is_sent_once_even_when_owners_have_different_periods()
    {
        var service = new EventManagementDomainService([new Owner(2215, Dependency(At.AddDays(1))),
            new Owner(2330, Dependency(At.AddDays(2)))], new UnusedLifecycle(), []);
        var list = await service.GetEventInfoListAsync(new(1), At, default);
        Assert.Single(list, x => x.EventId == 69);
    }

    [Fact]
    public async Task Conflicting_wire_content_is_not_silently_hidden_by_deduplication()
    {
        var service = new EventManagementDomainService([new Owner(2215, Dependency(At.AddDays(1))),
            new Owner(2330, Dependency(At.AddDays(1)) with { StartNotice = "different" })], new UnusedLifecycle(), []);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await service.GetEventInfoListAsync(new(1), At, default));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(23, true)]
    public async Task Raid_channel_catalog_controls_event_info_entry(byte environment, bool raidChannelEnabled)
    {
        var raid = new AntonRaidEventInfoDomainService(new Catalog(environment));
        var service = new EventManagementDomainService([], new UnusedLifecycle(), [raid]);

        var list = await service.GetEventInfoListAsync(new(1), At, default);

        Assert.Empty((await service.ListAsync(At, default)).Entries);
        Assert.False(await service.IsOpenAsync(181, At, default));

        if (raidChannelEnabled)
        {
            var entry = Assert.Single(list);
            Assert.Equal(181, entry.EventId);
            Assert.False(entry.HasDetail);
            Assert.Empty(entry.StartNotice);
            Assert.Empty(entry.EndNotice);
        }
        else
        {
            Assert.Empty(list);
        }
    }

    private sealed class Catalog(byte environment) : IChannelContentRuleService
    {
        public string ScriptVersion => throw new NotSupportedException();
        public ValueTask<byte[]> ReadScriptAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<ChannelServerInfo>> ChannelsAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<ChannelServerInfo>>([new(200, "#ch.200", 250, 0, "127.0.0.1", 10200, environment)]);
    }

    private sealed class UnusedLifecycle : IManagedEventLifecycleDomainService
    {
        public IReadOnlyList<int> EventIds => [];
        public ValueTask<ManagedEventActivationState?> AppliedAsync(int eventId, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<bool> ReconcileAsync(int eventId, DateTimeOffset now, CancellationToken ct, bool notifyClients = true) => throw new NotSupportedException();
    }

    private sealed class Owner(int id, EventInfo information) : IManagedEventDomainService
    {
        public int EventId => id;
        public ValueTask<EventInfo[]> GetEventInfoEntriesAsync(AccountId account, DateTimeOffset now, CancellationToken ct)
            => ValueTask.FromResult(new[] { information });
        public ValueTask<ManagedEventEntry> DescribeAsync(DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<bool> IsActiveAsync(DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask ChangeAsync(EventChange change, string actor, DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();
    }
}
