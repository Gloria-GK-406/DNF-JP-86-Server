using Dfo.Server.Abstractions.Events;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Infrastructure;
using Dfo.Server.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Events;

public sealed class SaoTowerEntranceEventTests
{
    [Fact]
    public async Task Configured_sao_activity_is_included_in_login_event_information()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var fixture = new Fixture();
            await fixture.Enable();
            var events = await fixture.Events.GetEventInfoListAsync(new AccountId(1), fixture.Now, default);
            var sao = Assert.Single(events, e => e.EventId == 214);
            Assert.Equal("sao aincrad event/0/0", sao.LinkKey);
        });
    }

    [Fact]
    public async Task Disabled_activity_stays_disabled_after_reopening_the_database()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var fixture = new Fixture();
            await fixture.Enable();
            await using (var tx = fixture.Services.GetRequiredService<DomainTransactionScope>().Begin())
            {
                await fixture.Events.ChangeAsync(new(214, 2, "disable"), "test", fixture.Now, default);
                await tx.CommitAsync(default);
            }
            using var reopened = fixture.Reopen();
            Assert.DoesNotContain(await reopened.Events.GetEventInfoListAsync(new(1), reopened.Now, default), e => e.EventId == 214);
            var state = Assert.Single((await reopened.Events.ListAsync(reopened.Now, default)).Entries, e => e.EventId == 214);
            Assert.False(state.Enabled);
            Assert.Equal(3, state.Revision);
        });
    }

    [Fact]
    public async Task Activity_projection_observes_account_and_start_inclusive_end_exclusive_boundaries()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var fixture = new Fixture();
            await fixture.Enable();
            Assert.DoesNotContain(await fixture.Events.GetEventInfoListAsync(new(0), fixture.Now, default), e => e.EventId == 214);
            Assert.DoesNotContain(await fixture.Events.GetEventInfoListAsync(new(1), fixture.Now.AddDays(-1).AddTicks(-1), default), e => e.EventId == 214);
            Assert.Contains(await fixture.Events.GetEventInfoListAsync(new(1), fixture.Now.AddDays(-1), default), e => e.EventId == 214);
            Assert.DoesNotContain(await fixture.Events.GetEventInfoListAsync(new(1), fixture.Now.AddDays(30), default), e => e.EventId == 214);
        });
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string directory;
        private readonly bool owns;
        public readonly ServiceProvider Services;
        public DateTimeOffset Now => DateTimeOffset.Parse("2026-10-14T10:00:00+08:00");
        public IEventManagementDomainService Events => Services.GetRequiredService<IEventManagementDomainService>();
        public Fixture() : this(Path.Combine(Path.GetTempPath(), "dfo-sao-event-" + Guid.NewGuid().ToString("N")), true) { }
        private Fixture(string directory, bool owns)
        {
            this.directory = directory;
            this.owns = owns;
            Directory.CreateDirectory(directory);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Database:Path"] = Path.Combine(directory, "test.db") }).Build();
            var services = new ServiceCollection().AddLogging();
            new InfrastructureModule().ConfigureServices(services, configuration);
            new PersistenceModule().ConfigureServices(services, configuration);
            new ApplicationModule().ConfigureServices(services, configuration);
            Services = services.BuildServiceProvider();
        }
        public async Task Enable()
        {
            await using var tx = Services.GetRequiredService<DomainTransactionScope>().Begin();
            await Events.ChangeAsync(new(214, 0, "configure", "艾恩葛朗特", "SAO塔", "入口测试", "", "", Now.AddDays(-1), Now.AddDays(30)), "test", Now, default);
            await Events.ChangeAsync(new(214, 1, "enable"), "test", Now, default);
            await tx.CommitAsync(default);
        }
        public Fixture Reopen() => new(directory, false);
        public void Dispose() { Services.Dispose(); if (!owns) return; SqliteTestPools.ClearOwned(Path.Combine(directory, "test.db")); Directory.Delete(directory, true); }
    }
}
