using System.Reflection;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Scheduling;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

/// <summary>
/// The online ticket refill and the first-login refill batch must agree on which tickets are online.
/// </summary>
/// <remarks>
/// Before one registration declared both, the batch kept its own list of ticket ids. A mode added to
/// one side only would have had its ticket granted twice a day, or left to a period nobody refilled.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class OnlineTicketRefillRegistrationTests
{
    private static readonly DailyRefillRule Tau = Rule(690001556);
    private static readonly DailyRefillRule SouthernDale = Rule(2680738);
    private static readonly DailyRefillRule BossTower = Rule(2749110);

    [Fact]
    public async Task Production_wiring_schedules_each_mode_and_declares_exactly_its_ticket()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        new ApplicationModule().ConfigureServices(services, new ConfigurationBuilder().Build());
        services.AddSingleton<Dfo.Server.Abstractions.Configuration.IDailyResetRuleService>(new Scheduling.OnlineExpiredItemScheduleTests.SixOClock());
        services.AddSingleton(Unused<IBossTowerContentSource>());
        services.AddSingleton(Unused<ISouthernDaleContentSource>());
        services.AddSingleton(Unused<ITauKingdomContentSource>());
        await using var provider = services.BuildServiceProvider();

        var jobs = provider.GetServices<ScheduledTaskRegistration>()
            .Where(x => x.HandlerType.IsGenericType && x.HandlerType.GetGenericTypeDefinition() == typeof(OnlineTicketRefillJob<>))
            .ToDictionary(x => x.Id, x => x.HandlerType.GetGenericArguments()[0]);
        Assert.Equal(new Dictionary<string, Type>
        {
            ["boss-tower.ticket-refill.online"] = typeof(IBossTowerTicketDomainService),
            ["southern-dale.ticket-refill.online"] = typeof(ISouthernDaleDomainService),
            ["tau-kingdom.ticket-refill.online"] = typeof(ITauKingdomDomainService),
        }, jobs);

        // The same three ids the batch used to name itself; every other item keeps the ordinary period.
        var rules = provider.GetRequiredService<IDailyRefillRuleService>();
        Assert.True(rules.UsesIndependentPeriod(Tau));
        Assert.True(rules.UsesIndependentPeriod(SouthernDale));
        Assert.True(rules.UsesIndependentPeriod(BossTower));
        Assert.False(rules.UsesIndependentPeriod(Rule(3037)));
        Assert.Equal(3, provider.GetServices<IOnlineRefilledTicketRule>().Count());
    }

    /// <summary>A ticket nobody declared is an ordinary refill, even one of the known ticket ids.</summary>
    [Fact]
    public void Only_declared_tickets_use_the_independent_period()
    {
        var reset = new Scheduling.OnlineExpiredItemScheduleTests.SixOClock();
        var undeclared = new DailyRefillRuleService(reset, []);
        Assert.False(undeclared.UsesIndependentPeriod(BossTower));

        var declared = new DailyRefillRuleService(reset, [new Ticket(9001)]);
        Assert.True(declared.UsesIndependentPeriod(Rule(9001)));
        Assert.False(declared.UsesIndependentPeriod(BossTower));
    }

    private static DailyRefillRule Rule(int itemId) =>
        new(itemId, 1, DateTimeOffset.MaxValue, DailyRefillMode.Add, 1, 1,
            new(BusinessInventorySpace.Consumable, 0, 56));

    private sealed record Ticket(int TicketItemId) : IOnlineRefilledTicketRule;

    /// <summary>The ticket id is a constant of each rule service; its content is never read here.</summary>
    private static T Unused<T>() where T : class => DispatchProxy.Create<T, UnusedProxy>();

    public class UnusedProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException($"{targetMethod?.Name} is outside this test.");
    }
}
