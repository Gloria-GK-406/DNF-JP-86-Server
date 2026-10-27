using System.Reflection;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Creatures;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
namespace Dfo.Server.Application.Tests.Execution;
[Collection("Scheduler")]
public sealed class CreatureTimerProjectionFailureTests
{
    [Fact]
    public async Task A_normal_projection_read_failure_warns_closes_only_its_exact_owner_and_continues_the_next_projection()
    {
        var creatures = DispatchProxy.Create<ICreatureRuntimeDomainService, CreatureProbe>();
        var probe = (CreatureProbe)creatures; var log = new LogProbe(); RecordedOutbound? outbound = null;
        await using var host = new TimerSourceHost(services =>
        {
            services.AddSingleton(creatures); services.AddLogging(builder => builder.AddProvider(log));
            outbound = RecordedOutbound.AddTo(services);
        });
        var sessions = host.Services.GetRequiredService<ISelectedSessionDomainService>();
        var users = host.Services.GetRequiredService<OnlineStateRegistry>();
        var owner = sessions.InWorld(users, new(101), new(101), "creature-failed-owner");
        var next = sessions.InWorld(users, new(102), new(102), "creature-next-owner");
        var pet = new CreatureState(500, 63000, 90, 2150, 19, [], 0, 0, 0, 0);
        probe.Pending = [new(owner, [new(pet, pet, true)]), new(next, [new(pet, pet, false)])];
        await host.StartAsync(host.Hosted(nameof(CreatureClockService)));
        await Until(() => host.Clock.ActiveTimers > 0); host.Clock.Advance(TimeSpan.FromSeconds(1));
        var actual = Assert.IsType<RecordedOutbound>(outbound);
        await Until(() => actual.Messages.Count == 1 && actual.Recorder.Entries.Any(entry => entry.IsClose));
        Assert.Equal(next.ConnectionId, Assert.Single(actual.Recorder.Entries, entry => !entry.IsClose).Connection.ConnectionId);
        Assert.Equal(owner.ConnectionId, Assert.Single(actual.Recorder.Entries, entry => entry.IsClose).Connection.ConnectionId);
        Assert.Single(log.Warnings, error => ReferenceEquals(error, probe.Cause));
    }
    private static async Task Until(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!predicate()) await Task.Delay(10, timeout.Token);
    }
    public class CreatureProbe : DispatchProxy
    {
        public IOException Cause { get; } = new("actual creature runtime IsEquipped read");
        public IReadOnlyList<CreaturePendingProjection> Pending = [];
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
        {
            nameof(ICreatureRuntimeDomainService.DueSessions) => Array.Empty<SelectedSessionIdentity>(),
            nameof(ICreatureRuntimeDomainService.DrainProjections) => Drain(),
            nameof(ICreatureRuntimeDomainService.IsEquipped) => throw Cause,
            _ => throw new NotSupportedException(method.Name),
        };
        private IReadOnlyList<CreaturePendingProjection> Drain() { var result = Pending; Pending = []; return result; }
    }
    private sealed class LogProbe : ILoggerProvider, ILogger
    {
        public System.Collections.Concurrent.ConcurrentQueue<Exception> Warnings { get; } = new();
        public ILogger CreateLogger(string categoryName) => this;
        public void Dispose() { }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter)
        { if (level == LogLevel.Warning && error is not null) Warnings.Enqueue(error); }
    }
}
