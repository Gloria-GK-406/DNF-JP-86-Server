using Dfo.Server.Testing;
using Dfo.Server.Application.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Tests.Commands;

[Trait("Category", "Unit")]
public sealed class CommandBusTests
{
    [Fact]
    public async Task SendAsync_AllowsSequentialTopLevelDispatchesAndCreatesANewScopeForEach()
    {
        CountingHandler.ExecutionCount = 0;
        await using var provider = BuildProvider(services =>
            services.AddFixtureCommand<TestCommand, TestResult, CountingHandler>(
                FixtureUnitKind.Direct));
        var bus = DomainCommandFixture.Bus(provider);

        var first = await bus.SendAsync(new TestCommand(), CancellationToken.None);
        var second = await bus.SendAsync(new TestCommand(), CancellationToken.None);

        Assert.Equal(2, CountingHandler.ExecutionCount);
        Assert.NotEqual(first.ScopeId, second.ScopeId);
    }

    [Fact]
    public async Task SendAsync_RejectsACommandThatHasNoRegisteredHandler()
    {
        // Replaces the former CanHandle test. A handler can no longer be paired with the
        // wrong command -- registration binds the two -- so the remaining failure is a
        // command nobody registered.
        await using var provider = BuildProvider(services =>
            services.AddFixtureCommand<TestCommand, TestResult, CountingHandler>(
                FixtureUnitKind.Direct));
        var bus = DomainCommandFixture.Bus(provider);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await bus.SendAsync(new UnregisteredCommand(), CancellationToken.None));

        Assert.Contains(nameof(UnregisteredCommand), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Registry_RejectsDuplicateCommandTypeRegistrations()
    {
        var services = CreateServices();
        services.AddFixtureCommand<TestCommand, TestResult, CountingHandler>(
            FixtureUnitKind.Direct);
        services.AddFixtureCommand<TestCommand, TestResult, OtherHandler>(
            FixtureUnitKind.Workflow);
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(
            () => provider.GetRequiredService<CommandHandlerRegistry>());

        Assert.Contains("more than once", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendAsync_RejectsNestedCommandDispatch()
    {
        await using var provider = BuildProvider(services =>
            services.AddFixtureCommand<TestCommand, TestResult, NestedDispatchHandler>(
                FixtureUnitKind.Direct));
        var bus = DomainCommandFixture.Bus(provider);

        var exception = await Assert.ThrowsAsync<Dfo.Server.Abstractions.Execution.GameplaySelfWaitException>(async () =>
            await bus.SendAsync(new TestCommand(), CancellationToken.None));

        Assert.Contains("Nested command dispatch", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Replaces the former test that asserted a transactional command failed for want of a
    /// unit-of-work policy. That policy is now registered, so the condition its name described
    /// no longer holds; what remains worth asserting here is that the kind reaches its handler.
    /// What the transaction then guarantees is asserted in <c>UnitOfWorkBehaviorTests</c>.
    /// </summary>
    [Fact]
    public async Task TransactionalCommand_ReachesItsHandler()
    {
        CountingHandler.ExecutionCount = 0;
        await using var provider = BuildProvider(services =>
            services.AddFixtureCommand<TestCommand, TestResult, CountingHandler>(
                FixtureUnitKind.Transactional));
        var bus = DomainCommandFixture.Bus(provider);

        var result = await bus.SendAsync(new TestCommand(), CancellationToken.None);

        Assert.Equal(1, CountingHandler.ExecutionCount);
        Assert.NotEqual(Guid.Empty, result.ScopeId);
    }

    [Fact]
    public async Task LoggingBehavior_RecordsSuccessfulAndFailedExecutions()
    {
        var logs = new RecordingLoggerProvider();
        await using var directProvider = BuildProvider(services =>
        {
            services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug).AddProvider(logs));
            services.AddFixtureCommand<TestCommand, TestResult, CountingHandler>(
                FixtureUnitKind.Direct);
        });

        await DomainCommandFixture.Bus(directProvider)
            .SendAsync(new TestCommand(), CancellationToken.None);

        // The failing side used to be produced by the transactional kind itself, which threw for
        // want of a unit-of-work policy. That policy now exists, so the failure comes from the
        // handler instead; the kind still has to appear in the error the behaviour logs.
        await using var transactionalProvider = BuildProvider(services =>
        {
            services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug).AddProvider(logs));
            services.AddSingleton(new FailOnceProbe(new InvalidOperationException("handler failure")));
            services.AddFixtureCommand<TestCommand, TestResult, FailOnceHandler>(
                FixtureUnitKind.Transactional);
        });
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await DomainCommandFixture.Bus(transactionalProvider)
                .SendAsync(new TestCommand(), CancellationToken.None));

        Assert.Contains(logs.Entries, entry =>
            entry.Level == LogLevel.Debug &&
            entry.Message.Contains(nameof(TestCommand), StringComparison.Ordinal) &&
            entry.Message.Contains("completed", StringComparison.Ordinal));
        Assert.Contains(logs.Entries, entry =>
            entry.Level == LogLevel.Error &&
            entry.Message.Contains("failed", StringComparison.Ordinal));
    }

    private static ServiceProvider BuildProvider(Action<IServiceCollection> configure)
    {
        var services = CreateServices();
        configure(services);
        return services.BuildServiceProvider();
    }

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ScopedProbe>();
        services.AddCommandDispatching();
        return services;
    }

    private sealed record TestResult(Guid ScopeId);

    private sealed record TestCommand : ICommand<TestResult>;

    private sealed record UnregisteredCommand : ICommand<TestResult>;

    private sealed class ScopedProbe
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    private sealed class CountingHandler(ScopedProbe probe) : ICommandHandler<TestCommand, TestResult>
    {
        public static int ExecutionCount { get; set; }

        public ValueTask<TestResult> HandleAsync(
            CommandExecutionContext context,
            TestCommand command,
            CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return ValueTask.FromResult(new TestResult(probe.Id));
        }
    }

    private sealed class FailOnceHandler(ScopedProbe probe, FailOnceProbe failure)
        : ICommandHandler<TestCommand, TestResult>
    {
        public ValueTask<TestResult> HandleAsync(
            CommandExecutionContext context,
            TestCommand command,
            CancellationToken cancellationToken)
        {
            failure.ThrowIfPending();
            return ValueTask.FromResult(new TestResult(probe.Id));
        }
    }

    private sealed class FailOnceProbe(Exception failure)
    {
        private Exception? failure = failure;

        public void ThrowIfPending()
        {
            var pending = Interlocked.Exchange(ref failure, null);
            if (pending is not null)
            {
                throw pending;
            }
        }
    }

    private sealed class OtherHandler : ICommandHandler<TestCommand, TestResult>
    {
        public ValueTask<TestResult> HandleAsync(
            CommandExecutionContext context,
            TestCommand command,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new TestResult(Guid.Empty));
    }

    private sealed class RecordingHandler(List<string> events) : ICommandHandler<TestCommand, TestResult>
    {
        public ValueTask<TestResult> HandleAsync(
            CommandExecutionContext context,
            TestCommand command,
            CancellationToken cancellationToken)
        {
            events.Add("handle");
            return ValueTask.FromResult(new TestResult(Guid.Empty));
        }
    }

    private sealed class NestedDispatchHandler(ICommandBus bus) : ICommandHandler<TestCommand, TestResult>
    {
        public ValueTask<TestResult> HandleAsync(
            CommandExecutionContext context,
            TestCommand command,
            CancellationToken cancellationToken) =>
            bus.SendAsync(new TestCommand(), cancellationToken);
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public List<LogEntry> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(Entries);

        public void Dispose()
        {
        }

        private sealed class RecordingLogger(List<LogEntry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }
    }
}
