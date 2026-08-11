using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Commands;

internal sealed class CommandBus(
    IServiceScopeFactory scopeFactory,
    CommandHandlerRegistry registry) : ICommandBus
{
    private static readonly AsyncLocal<int> DispatchDepth = new();

    public async ValueTask<TResult> SendAsync<TResult>(
        ICommand<TResult> command,
        CancellationToken cancellationToken = default)
        where TResult : notnull
    {
        ArgumentNullException.ThrowIfNull(command);
        if (DispatchDepth.Value != 0)
        {
            throw new InvalidOperationException(
                "Nested command dispatch is not allowed. Extract shared execution logic into an application service.");
        }

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
}
