using Dfo.Server.Application.Commands;
using Dfo.Server.Data.OnlineUsers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Tests.Commands;

public sealed class CommandBusTests
{
    [Fact]
    public async Task SendAsync_ExecutesOneHandlerAndReturnsItsTypedResultInANewScope()
    {
        CountingHandler.ExecutionCount = 0;
        await using var provider = BuildProvider(services =>
            services.RegisterCommandHandler<CountingHandler>(
                ApplicationCommandType.EstablishChannelConnection,
                CommandExecutionKind.Direct));
        var bus = provider.GetRequiredService<ICommandBus>();

        var first = await bus.SendAsync(new TestCommand(), CancellationToken.None);
        var second = await bus.SendAsync(new TestCommand(), CancellationToken.None);

        Assert.Equal(2, CountingHandler.ExecutionCount);
        Assert.NotEqual(first.ScopeId, second.ScopeId);
    }

    [Fact]
    public async Task SendAsync_RejectsACommandWhoseClrTypeDoesNotMatchTheRegisteredHandler()
    {
        await using var provider = BuildProvider(services =>
            services.RegisterCommandHandler<CountingHandler>(
                ApplicationCommandType.EstablishChannelConnection,
                CommandExecutionKind.Direct));
        var bus = provider.GetRequiredService<ICommandBus>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await bus.SendAsync(new AlternativeCommand(), CancellationToken.None));

        Assert.Contains(nameof(CountingHandler), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Registry_RejectsDuplicateCommandTypeRegistrations()
    {
        var services = CreateServices();
        services.RegisterCommandHandler<CountingHandler>(
            ApplicationCommandType.EstablishChannelConnection,
            CommandExecutionKind.Direct);
        services.RegisterCommandHandler<OtherHandler>(
            ApplicationCommandType.EstablishChannelConnection,
            CommandExecutionKind.Workflow);
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(
            () => provider.GetRequiredService<CommandHandlerRegistry>());

        Assert.Contains("more than once", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Registry_RejectsMissingCommandTypeRegistrations()
    {
        var services = CreateServices();
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(
            () => provider.GetRequiredService<CommandHandlerRegistry>());

        Assert.Contains(
            nameof(ApplicationCommandType.EstablishChannelConnection),
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_RejectsAPipelineResultOfTheWrongType()
    {
        // The pipeline seam is object, so a behavior can still short-circuit with a value
        // of the wrong type. Null can no longer reach here -- the contract is non-nullable
        // and a null literal does not compile -- so a mismatch is all that remains.
        await using var provider = BuildProvider(services =>
        {
            services.AddSingleton<ICommandPipelineBehavior, WrongTypeBehavior>();
            services.RegisterCommandHandler<CountingHandler>(
                ApplicationCommandType.EstablishChannelConnection,
                CommandExecutionKind.Direct);
        });
        var bus = provider.GetRequiredService<ICommandBus>();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await bus.SendAsync(new TestCommand(), CancellationToken.None));

        Assert.Contains(nameof(TestResult), error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(String), error.Message, StringComparison.Ordinal);
    }

    private sealed class WrongTypeBehavior : ICommandPipelineBehavior
    {
        public int Order => -1000;

        public ValueTask<object> ExecuteAsync(
            CommandExecutionContext context,
            ICommand command,
            CommandExecutionDelegate next,
            CancellationToken cancellationToken) => ValueTask.FromResult<object>("not a TestResult");
    }

    [Fact]
    public async Task Pipeline_ExecutesBehaviorsInOrderAroundTheTerminalHandler()
    {
        var events = new List<string>();
        await using var provider = BuildProvider(services =>
        {
            services.AddSingleton(events);
            services.AddScoped<ICommandPipelineBehavior, OuterRecordingBehavior>();
            services.AddScoped<ICommandPipelineBehavior, InnerRecordingBehavior>();
            services.RegisterCommandHandler<RecordingHandler>(
                ApplicationCommandType.EstablishChannelConnection,
                CommandExecutionKind.Direct);
        });
        var bus = provider.GetRequiredService<ICommandBus>();

        await bus.SendAsync(new TestCommand(), CancellationToken.None);

        Assert.Equal(
            new[] { "outer-before", "inner-before", "handle", "inner-after", "outer-after" },
            events);
    }

    [Fact]
    public async Task SendAsync_RejectsNestedCommandDispatch()
    {
        await using var provider = BuildProvider(services =>
            services.RegisterCommandHandler<NestedDispatchHandler>(
                ApplicationCommandType.EstablishChannelConnection,
                CommandExecutionKind.Direct));
        var bus = provider.GetRequiredService<ICommandBus>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await bus.SendAsync(new TestCommand(), CancellationToken.None));

        Assert.Contains("Nested command dispatch", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnlineUserScopedRegistration_RequiresAnOnlineUserCommand()
    {
        await using var provider = BuildProvider(services =>
            services.RegisterCommandHandler<CountingHandler>(
                ApplicationCommandType.EstablishChannelConnection,
                CommandExecutionKind.Direct,
                UserAccessMode.Shared));
        var bus = provider.GetRequiredService<ICommandBus>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await bus.SendAsync(new TestCommand(), CancellationToken.None));

        Assert.Contains(nameof(IOnlineUserCommand), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SharedOnlineUserLease_IsReleasedWhenHandlerThrows()
    {
        await AssertOnlineUserLeaseReleasedAfterFailureAsync(
            UserAccessMode.Shared,
            new InvalidOperationException("shared failure"));
    }

    [Fact]
    public async Task ExclusiveOnlineUserLease_IsReleasedWhenHandlerIsCancelled()
    {
        await AssertOnlineUserLeaseReleasedAfterFailureAsync(
            UserAccessMode.Exclusive,
            new OperationCanceledException("exclusive cancellation"));
    }

    [Fact]
    public async Task TransactionalCommand_FailsClearlyUntilAUnitOfWorkPolicyIsRegistered()
    {
        CountingHandler.ExecutionCount = 0;
        await using var provider = BuildProvider(services =>
            services.RegisterCommandHandler<CountingHandler>(
                ApplicationCommandType.EstablishChannelConnection,
                CommandExecutionKind.Transactional));
        var bus = provider.GetRequiredService<ICommandBus>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await bus.SendAsync(new TestCommand(), CancellationToken.None));

        Assert.Contains(nameof(CommandExecutionKind.Transactional), exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, CountingHandler.ExecutionCount);
    }

    [Fact]
    public async Task LoggingBehavior_RecordsSuccessfulAndFailedExecutions()
    {
        var logs = new RecordingLoggerProvider();
        await using var directProvider = BuildProvider(services =>
        {
            services.AddLogging(builder => builder.AddProvider(logs));
            services.RegisterCommandHandler<CountingHandler>(
                ApplicationCommandType.EstablishChannelConnection,
                CommandExecutionKind.Direct);
        });

        await directProvider.GetRequiredService<ICommandBus>()
            .SendAsync(new TestCommand(), CancellationToken.None);

        await using var transactionalProvider = BuildProvider(services =>
        {
            services.AddLogging(builder => builder.AddProvider(logs));
            services.RegisterCommandHandler<CountingHandler>(
                ApplicationCommandType.EstablishChannelConnection,
                CommandExecutionKind.Transactional);
        });
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await transactionalProvider.GetRequiredService<ICommandBus>()
                .SendAsync(new TestCommand(), CancellationToken.None));

        Assert.Contains(logs.Entries, entry =>
            entry.Level == LogLevel.Information &&
            entry.Message.Contains(nameof(ApplicationCommandType.EstablishChannelConnection), StringComparison.Ordinal) &&
            entry.Message.Contains(nameof(CommandExecutionKind.Direct), StringComparison.Ordinal));
        Assert.Contains(logs.Entries, entry =>
            entry.Level == LogLevel.Error &&
            entry.Message.Contains(nameof(CommandExecutionKind.Transactional), StringComparison.Ordinal));
    }

    private static ServiceProvider BuildProvider(Action<IServiceCollection> configure)
    {
        var services = CreateServices();
        configure(services);
        var registeredTypes = services
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<CommandHandlerRegistration>()
            .Select(registration => registration.CommandType)
            .ToHashSet();
        foreach (var commandType in Enum.GetValues<ApplicationCommandType>().Except(registeredTypes))
        {
            services.RegisterCommandHandler<OtherHandler>(commandType, CommandExecutionKind.Direct);
        }
        return services.BuildServiceProvider();
    }

    private static async Task AssertOnlineUserLeaseReleasedAfterFailureAsync(
        UserAccessMode accessMode,
        Exception failure)
    {
        var probe = new FailOnceProbe(failure);
        await using var provider = BuildProvider(services =>
        {
            services.AddSingleton(probe);
            services.RegisterCommandHandler<FailOnceOnlineHandler>(
                ApplicationCommandType.EstablishChannelConnection,
                CommandExecutionKind.Direct,
                accessMode);
        });
        var bus = provider.GetRequiredService<ICommandBus>();
        var command = new OnlineTestCommand(OnlineUserId.New());

        await Assert.ThrowsAsync(failure.GetType(), async () =>
            await bus.SendAsync(command, CancellationToken.None));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var result = await bus.SendAsync(command, timeout.Token);
        Assert.NotEqual(Guid.Empty, result.ScopeId);
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

    private sealed record TestCommand : ICommand<TestResult>
    {
        public ApplicationCommandType CommandType =>
            ApplicationCommandType.EstablishChannelConnection;
    }

    private sealed record AlternativeCommand : ICommand<TestResult>
    {
        public ApplicationCommandType CommandType =>
            ApplicationCommandType.EstablishChannelConnection;
    }

    private sealed record OnlineTestCommand(OnlineUserId OnlineUserId)
        : ICommand<TestResult>, IOnlineUserCommand
    {
        public ApplicationCommandType CommandType =>
            ApplicationCommandType.EstablishChannelConnection;
    }

    private sealed class ScopedProbe
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    private sealed class CountingHandler(ScopedProbe probe) : CommandHandler<TestCommand, TestResult>
    {
        public static int ExecutionCount { get; set; }

        protected override ValueTask<TestResult> HandleCommandAsync(
            CommandExecutionContext context,
            TestCommand command,
            CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return ValueTask.FromResult(new TestResult(probe.Id));
        }
    }

    private sealed class FailOnceOnlineHandler(ScopedProbe probe, FailOnceProbe failure)
        : CommandHandler<OnlineTestCommand, TestResult>
    {
        protected override ValueTask<TestResult> HandleCommandAsync(
            CommandExecutionContext context,
            OnlineTestCommand command,
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

    private sealed class OtherHandler : CommandHandler<TestCommand, TestResult>
    {
        protected override ValueTask<TestResult> HandleCommandAsync(
            CommandExecutionContext context,
            TestCommand command,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new TestResult(Guid.Empty));
    }

    private sealed class RecordingHandler(List<string> events) : CommandHandler<TestCommand, TestResult>
    {
        protected override ValueTask<TestResult> HandleCommandAsync(
            CommandExecutionContext context,
            TestCommand command,
            CancellationToken cancellationToken)
        {
            events.Add("handle");
            return ValueTask.FromResult(new TestResult(Guid.Empty));
        }
    }

    private sealed class NestedDispatchHandler(ICommandBus bus) : CommandHandler<TestCommand, TestResult>
    {
        protected override ValueTask<TestResult> HandleCommandAsync(
            CommandExecutionContext context,
            TestCommand command,
            CancellationToken cancellationToken) =>
            bus.SendAsync(new TestCommand(), cancellationToken);
    }

    private sealed class OuterRecordingBehavior(List<string> events) : ICommandPipelineBehavior
    {
        public int Order => -100;

        public async ValueTask<object> ExecuteAsync(
            CommandExecutionContext context,
            ICommand command,
            CommandExecutionDelegate next,
            CancellationToken cancellationToken)
        {
            events.Add("outer-before");
            var result = await next();
            events.Add("outer-after");
            return result;
        }
    }

    private sealed class InnerRecordingBehavior(List<string> events) : ICommandPipelineBehavior
    {
        public int Order => 50;

        public async ValueTask<object> ExecuteAsync(
            CommandExecutionContext context,
            ICommand command,
            CommandExecutionDelegate next,
            CancellationToken cancellationToken)
        {
            events.Add("inner-before");
            var result = await next();
            events.Add("inner-after");
            return result;
        }
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
