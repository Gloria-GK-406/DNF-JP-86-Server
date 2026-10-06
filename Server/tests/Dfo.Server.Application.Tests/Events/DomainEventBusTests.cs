using Dfo.Server.Abstractions.Events;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Events;
using Dfo.Server.Application.Transactions;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Events;

public sealed class DomainEventBusTests
{
    [Fact]
    public async Task A_failed_subscriber_does_not_repeat_or_suppress_other_subscribers()
    {
        var probe=new Probe();
        await using var provider=Services(probe).BuildServiceProvider();
        await provider.GetRequiredService<IDomainEventBus>().PublishAsync(new FactEvent(),default);
        Assert.Equal(1,probe.FailedAttempts);
        Assert.Equal(1,probe.Delivered);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Open_commands_and_read_only_queries_cannot_publish_events(bool readOnly)
    {
        var probe=new Probe();
        await using var provider=Services(probe).BuildServiceProvider();
        await using var transaction=provider.GetRequiredService<DomainTransactionScope>().Begin(readOnly);
        await Assert.ThrowsAsync<InvalidOperationException>(async()=>
            await provider.GetRequiredService<IDomainEventBus>().PublishAsync(new FactEvent(),default));
        Assert.Equal(0,probe.FailedAttempts);Assert.Equal(0,probe.Delivered);
    }
    private static IServiceCollection Services(Probe probe)=>new ServiceCollection().AddLogging().AddCommandDispatching()
        .AddSingleton(probe).AddSingleton<IDomainEventBus,DomainEventBus>()
        .AddScoped<IDomainEventHandler<FactEvent>,Failing>()
        .AddScoped<IDomainEventHandler<FactEvent>,Recording>();
    private sealed record FactEvent:IDomainEvent;
    private sealed class Probe{public int FailedAttempts;public int Delivered;}
    private sealed class Failing(Probe probe):IDomainEventHandler<FactEvent>
    {
        public ValueTask HandleAsync(FactEvent value,CancellationToken ct)
        {probe.FailedAttempts++;throw new IOException("notification transport failed");}
    }
    private sealed class Recording(Probe probe):IDomainEventHandler<FactEvent>
    {
        public ValueTask HandleAsync(FactEvent value,CancellationToken ct)
        {probe.Delivered++;return ValueTask.CompletedTask;}
    }
}
