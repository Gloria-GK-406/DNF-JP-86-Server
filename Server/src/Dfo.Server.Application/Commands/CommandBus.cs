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
            var registration = registry.GetRegistration(command.CommandType);
            var handler = (ICommandHandler)scope.ServiceProvider.GetRequiredService(registration.HandlerType);
            if (!handler.CanHandle(command))
            {
                throw new InvalidOperationException(
                    $"Handler '{registration.HandlerType.Name}' cannot handle command CLR type '{command.GetType().Name}'.");
            }

            var context = new CommandExecutionContext(
                Guid.NewGuid(),
                command.CommandType,
                registration.HandlerType,
                registration.ExecutionKind,
                registration.OnlineUserAccessMode,
                registration.AccountAccessMode);
            CommandExecutionDelegate next = () => handler.HandleAsync(context, command, cancellationToken);

            var behaviors = scope.ServiceProvider
                .GetServices<ICommandPipelineBehavior>()
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

            var result = await next().ConfigureAwait(false);
            if (result is TResult typedResult)
            {
                return typedResult;
            }

            // TResult is constrained to notnull and the pipeline returns object, so a
            // mismatch here is a wiring defect rather than a representable absence.
            throw new InvalidOperationException(
                $"Command '{command.CommandType}' returned '{result.GetType().Name}', " +
                $"but '{typeof(TResult).Name}' was expected.");
        }
        finally
        {
            DispatchDepth.Value--;
        }
    }
}
