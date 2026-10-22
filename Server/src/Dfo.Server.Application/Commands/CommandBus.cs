using Dfo.Server.Abstractions.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Commands;

/// <summary>
/// Plan transition rule 1: off the executor a command is enqueued on this container's executor and
/// awaited; on a job of any executor it runs inline (that branch goes with T-008). There is no
/// "executor missing, run inline" path: a stopped executor refuses the command.
/// </summary>
internal sealed class CommandBus(
    IServiceScopeFactory scopeFactory,
    CommandHandlerRegistry registry,
    IGameplayQueue queue) : ICommandBus
{
    private static readonly AsyncLocal<int> DispatchDepth = new();

    public ValueTask<TResult> SendAsync<TResult>(
        ICommand<TResult> command,
        CancellationToken cancellationToken = default)
        where TResult : notnull
    {
        ArgumentNullException.ThrowIfNull(command);
        // Before the executor check: a flow that left its handler (RunDetached, or a continuation that
        // outlived its job) still carries the depth and must not become a fresh root job.
        if (DispatchDepth.Value != 0) return ValueTask.FromException<TResult>(NestedDispatch());
        if (GameplayJob.IsOnExecutor) return InlineAsync(command, cancellationToken);
        // Production has no off-executor caller (entry reconciliation §1.3); only tests reach this.
        return queue.RunAsync(new GameplayWork<TResult>(
            GameplayShardKey.World,
            GameplayCallChain.NewRoot(GameplayCallSource.Connection),
            "bus:" + command.GetType().Name,
            token => InlineAsync(command, token)), cancellationToken);
    }

    private async ValueTask<TResult> InlineAsync<TResult>(
        ICommand<TResult> command,
        CancellationToken cancellationToken)
        where TResult : notnull
    {
        if (DispatchDepth.Value != 0) throw NestedDispatch();

        DispatchDepth.Value++;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var registration = registry.GetRegistration(command.GetType());

            // Registration binds the command, its result and its handler together, and
            // ICommand is invariant in its result, so TResult here is the command's own
            // declared result and this invoker necessarily produces it.
            var invoker = (ICommandInvoker<TResult>)scope.ServiceProvider
                .GetRequiredService(registration.InvokerType);

            var context = new CommandExecutionContext(
                Guid.NewGuid(),
                registration.CommandType,
                registration.HandlerType,
                registration.ExecutionKind,
                registration.OnlineUserAccessMode,
                registration.AccountAccessMode);
            CommandExecutionDelegate<TResult> next = () =>
                invoker.InvokeAsync(context, command, cancellationToken);

            var behaviors = scope.ServiceProvider
                .GetServices<ICommandPipelineBehavior<TResult>>()
                .OrderBy(behavior => behavior.Order)
                .ToArray();
            foreach (var behavior in behaviors.Reverse())
            {
                var capturedBehavior = behavior;
                var capturedNext = next;
                next = () => capturedBehavior.ExecuteAsync(
                    context,
                    command,
                    capturedNext,
                    cancellationToken);
            }

            return await next().ConfigureAwait(false);
        }
        finally
        {
            DispatchDepth.Value--;
        }
    }

    private static InvalidOperationException NestedDispatch() => new(
        "Nested command dispatch is not allowed. Extract shared execution logic into an application service.");
}
