using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Scheduling;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dfo.Server.Application.Tests.Scheduling;

[Collection("Scheduler")]
public sealed class OnlineExpiredItemScheduleTests
{
    [Fact]
    public async Task Registered_cleanup_waits_for_six_and_dispatches_only_selected_characters()
    {
        var clock = new SchedulerClock(); // 05:59 at UTC+8.
        var services = new ServiceCollection();
        services.AddLogging();
        new ApplicationModule().ConfigureServices(services, new ConfigurationBuilder().Build());
        foreach (var hostedRegistration in services.Where(x => x.ServiceType == typeof(IHostedService)
            && x.ImplementationType?.Name != "ScheduledTaskHostedService").ToArray()) services.Remove(hostedRegistration);
        var sessions = new SelectedSessionDomainService();
        var session = SelectedSessionIdentity.Create(new(41), new(57), new("Game:expiry"), 1, 10011);
        sessions.Select(session);
        var commands = new RecordingCommands();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IDailyResetRuleService>(new SixOClock());
        services.AddSingleton<ISelectedSessionSnapshotQuery>(sessions);
        services.AddSingleton<ISelectedSessionDomainService>(sessions);
        services.AddSingleton<ICommandBus>(commands);
        await using var provider = services.BuildServiceProvider();
        var registrations = provider.GetServices<ScheduledTaskRegistration>().ToArray();
        var registration = Assert.Single(registrations, x => x.Id == "inventory.expiration.online");
        Assert.Contains(registrations, x => x.Id == "tau-kingdom.ticket-refill.online");
        Assert.Contains(registrations, x => x.Id == "southern-dale.ticket-refill.online");
        Assert.Contains(registrations, x => x.Id == "boss-tower.ticket-refill.online");
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 22, 0, 0, TimeSpan.Zero),
            registration.Schedule.NextOccurrence(clock.GetUtcNow()));
        // Run only the actual registered scheduler; other host services are outside this fixture.
        var scheduler = provider.GetServices<IHostedService>().Single(x => x.GetType().Name == "ScheduledTaskHostedService");
        await scheduler.StartAsync(default);
        try
        {
            await Until(() => clock.ActiveTimers > 0);
            Assert.Empty(commands.Accounts);
            clock.Advance(TimeSpan.FromMinutes(1));
            await Until(() => commands.Accounts.Count == 1 && commands.TauAccounts.Count == 1 && commands.SouthernAccounts.Count == 1 && commands.BossAccounts.Count == 1);
            Assert.Equal(41, Assert.Single(commands.SouthernAccounts));
            Assert.Equal(41, Assert.Single(commands.BossAccounts));
            Assert.Equal(41, Assert.Single(commands.TauAccounts));
            Assert.Equal(41, Assert.Single(commands.Accounts));
            sessions.ReleaseExact(session);
            clock.Advance(TimeSpan.FromDays(1));
            await Until(() => clock.ActiveTimers > 0);
            Assert.Single(commands.Accounts);
            Assert.Single(commands.TauAccounts);
            Assert.Single(commands.SouthernAccounts);
            Assert.Single(commands.BossAccounts);
        }
        finally { await scheduler.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(10)); }
    }

    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition()) await Task.Delay(1, timeout.Token);
    }

    internal sealed class SixOClock : IDailyResetRuleService
    {
        public DateTimeOffset PeriodStart(DateTimeOffset now)
        {
            var local = now.ToOffset(TimeSpan.FromHours(8));
            var boundary = new DateTimeOffset(local.Date.AddHours(6), local.Offset);
            return (boundary > now ? boundary.AddDays(-1) : boundary).ToUniversalTime();
        }
        public bool IsCurrentPeriod(DateTimeOffset recorded, DateTimeOffset now) => recorded >= PeriodStart(now) && recorded <= now;
    }

    private sealed class RecordingCommands : ICommandBus
    {
        public System.Collections.Concurrent.ConcurrentQueue<int> Accounts { get; } = new();
        public System.Collections.Concurrent.ConcurrentQueue<int> TauAccounts { get; } = new();
        public System.Collections.Concurrent.ConcurrentQueue<int> SouthernAccounts { get; } = new();
        public System.Collections.Concurrent.ConcurrentQueue<int> BossAccounts { get; } = new();
        public ValueTask<T> SendAsync<T>(ICommand<T> command, CancellationToken ct = default) where T : notnull
        {
            if (command is Dfo.Server.Application.World.RefillOnlineTauTicketCommand)
                TauAccounts.Enqueue(((IAccountCommand)command).AccountId.Value);
            else if (command is Dfo.Server.Application.World.RefillOnlineSouthernTicketCommand)
                SouthernAccounts.Enqueue(((IAccountCommand)command).AccountId.Value);
            else if (command is Dfo.Server.Application.World.BossTower.RefillOnlineBossTowerTicketCommand)
                BossAccounts.Enqueue(((IAccountCommand)command).AccountId.Value);
            else if (command is Dfo.Server.Application.Inventory.CleanupOnlineExpiredItemsCommand)
                Accounts.Enqueue(((IAccountCommand)command).AccountId.Value);
            return ValueTask.FromResult((T)(object)true);
        }
    }
}
