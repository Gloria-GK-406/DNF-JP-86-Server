using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.Fatigue;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
namespace Dfo.Server.Application.Tests.Execution;
public sealed class ImmediateEventScopeFailureTests
{
    [Theory]
    [InlineData("direct", true)]
    [InlineData("project", false)]
    [InlineData("composer", true)]
    public async Task Actual_immediate_scopes_identify_cleanup_separately_and_keep_a_simultaneous_invocation_cause(string target, bool invokeFails)
    {
        using var state = new State(target, invokeFails);
        using var shop = new ShopFixture(services =>
        {
            services.AddSingleton(state); services.AddScoped<Resource>();
            services.AddRuntimeEventSubscriber<Fact, Subscriber>();
            services.AddScoped<Subscriber>(p => new(state, target == "direct" ? p.GetRequiredService<Resource>() : null));
            services.AddNotificationHandler<Notice, Composer>();
            services.AddScoped<INotificationHandler<Notice>>(p => new Composer(state, target == "composer" ? p.GetRequiredService<Resource>() : null));
            services.AddScoped<NotificationProjectionSubscriber<Notice>>(p =>
            {
                if (target == "project") _ = p.GetRequiredService<Resource>();
                return new(p.GetRequiredService<INotificationBus>());
            });
        });
        var events = shop.Services.GetRequiredService<IEventService>();
        var error = await Record.ExceptionAsync(() => TestGameplay.RunAsync(shop.Services, async () =>
        {
            if (target == "direct") await events.DirectAsync(new Fact(), state.Cancellation.Token);
            else await events.ProjectAsync(new NotificationAudience.TheseSessions([shop.Session]), new Notice(), state.Cancellation.Token);
        }));
        Assert.NotNull(error); Assert.True(DomainEventScopeCleanupException.Contains(error));
        Assert.True(Contains(error, state.Cleanup)); Assert.Equal(1, state.Disposed);
        if (invokeFails) Assert.True(Contains(error, state.Body));
    }
    private static bool Contains(Exception error, Exception cause) => ReferenceEquals(error, cause)
        || error is AggregateException aggregate && aggregate.InnerExceptions.Any(item => Contains(item, cause))
        || error.InnerException is {} inner && Contains(inner, cause);
    private sealed record Fact : IDomainEvent;
    private sealed record Notice : INotification<Notice>;
    private sealed class State(string target, bool invokeFails) : IDisposable
    {
        public string Target => target; public bool InvokeFails => invokeFails;
        public CancellationTokenSource Cancellation { get; } = new();
        public Exception Body { get; } = target == "composer" ? new OperationCanceledException("actual composer cancellation") : new IOException("actual subscriber invocation");
        public IOException Cleanup { get; } = new("actual scope DisposeAsync");
        public int Disposed;
        public void Dispose() => Cancellation.Dispose();
    }
    private sealed class Resource(State state) : IAsyncDisposable
    { public ValueTask DisposeAsync() { Interlocked.Increment(ref state.Disposed); return ValueTask.FromException(state.Cleanup); } }
    private sealed class Subscriber(State state, Resource? resource) : IDomainEventSubscriber<Fact>
    {
        public ValueTask HandleAsync(DomainEventEnvelope<Fact> value, CancellationToken token)
        { _ = resource; if (state.InvokeFails) throw state.Body; return ValueTask.CompletedTask; }
    }
    private sealed class Composer(State state, Resource? resource) : INotificationHandler<Notice>
    {
        public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient, Notice notice, CancellationToken token)
        {
            _ = resource;
            if (state.InvokeFails) { state.Cancellation.Cancel(); throw state.Body; }
            return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([new FatigueNotification(new(156, 1))]);
        }
    }
}
