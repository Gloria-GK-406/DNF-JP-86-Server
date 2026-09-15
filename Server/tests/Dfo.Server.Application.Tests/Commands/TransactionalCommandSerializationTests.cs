using Dfo.Server.Application.Commands;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Commands;

public sealed class TransactionalCommandSerializationTests
{
    [Fact]
    public async Task TransactionalCommands_DoNotRunTheirHandlersConcurrently()
    {
        var probe = new ConcurrencyProbe();
        await using var provider = BuildProvider(probe, CommandExecutionKind.Transactional);
        var bus = provider.GetRequiredService<ICommandBus>();

        var first = bus.SendAsync(new ProbeCommand(Block: true), CancellationToken.None).AsTask();
        await probe.FirstBlockingCallEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var second = bus.SendAsync(new ProbeCommand(Block: false), CancellationToken.None).AsTask();
        await Task.Delay(100);
        probe.ReleaseBlockingCall.TrySetResult();

        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, probe.MaximumConcurrentHandlers);
    }

    [Fact]
    public async Task DirectCommands_RemainConcurrent()
    {
        var probe = new ConcurrencyProbe();
        await using var provider = BuildProvider(probe, CommandExecutionKind.Direct);
        var bus = provider.GetRequiredService<ICommandBus>();

        var first = bus.SendAsync(new ProbeCommand(Block: true), CancellationToken.None).AsTask();
        await probe.FirstBlockingCallEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var second = bus.SendAsync(new ProbeCommand(Block: false), CancellationToken.None).AsTask();
        await second.WaitAsync(TimeSpan.FromSeconds(2));
        probe.ReleaseBlockingCall.TrySetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(2, probe.MaximumConcurrentHandlers);
    }

    [Fact]
    public async Task CancelledWaiter_DoesNotEnterAndDoesNotKeepTheGate()
    {
        var probe = new ConcurrencyProbe();
        await using var provider = BuildProvider(probe, CommandExecutionKind.Transactional);
        var bus = provider.GetRequiredService<ICommandBus>();

        var first = bus.SendAsync(new ProbeCommand(Block: true), CancellationToken.None).AsTask();
        await probe.FirstBlockingCallEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        using var cancellation = new CancellationTokenSource();
        var cancelled = bus.SendAsync(
            new ProbeCommand(Block: false), cancellation.Token).AsTask();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);

        probe.ReleaseBlockingCall.TrySetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(2));
        await bus.SendAsync(new ProbeCommand(Block: false), CancellationToken.None);

        Assert.Equal(2, probe.EnteredHandlers);
        Assert.Equal(1, probe.MaximumConcurrentHandlers);
    }

    private static ServiceProvider BuildProvider(
        ConcurrencyProbe probe,
        CommandExecutionKind executionKind)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCommandDispatching();
        services.AddSingleton(probe);
        services.AddCommandHandler<ProbeCommand, ProbeResult, ProbeHandler>(executionKind);
        return services.BuildServiceProvider();
    }

    private sealed record ProbeCommand(bool Block) : ICommand<ProbeResult>;

    private sealed record ProbeResult;

    private sealed class ConcurrencyProbe
    {
        private int runningHandlers;
        private int maximumConcurrentHandlers;
        private int enteredHandlers;

        public TaskCompletionSource FirstBlockingCallEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseBlockingCall { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int MaximumConcurrentHandlers => Volatile.Read(ref this.maximumConcurrentHandlers);

        public int EnteredHandlers => Volatile.Read(ref this.enteredHandlers);

        public void Enter()
        {
            Interlocked.Increment(ref this.enteredHandlers);
            var running = Interlocked.Increment(ref this.runningHandlers);
            while (true)
            {
                var current = Volatile.Read(ref this.maximumConcurrentHandlers);
                if (current >= running
                    || Interlocked.CompareExchange(
                        ref this.maximumConcurrentHandlers, running, current) == current)
                {
                    return;
                }
            }
        }

        public void Leave() => Interlocked.Decrement(ref this.runningHandlers);
    }

    private sealed class ProbeHandler(ConcurrencyProbe probe)
        : ICommandHandler<ProbeCommand, ProbeResult>
    {
        public async ValueTask<ProbeResult> HandleAsync(
            CommandExecutionContext context,
            ProbeCommand command,
            CancellationToken cancellationToken)
        {
            probe.Enter();
            try
            {
                if (command.Block)
                {
                    probe.FirstBlockingCallEntered.TrySetResult();
                    await probe.ReleaseBlockingCall.Task.WaitAsync(cancellationToken);
                }

                return new ProbeResult();
            }
            finally
            {
                probe.Leave();
            }
        }
    }
}
