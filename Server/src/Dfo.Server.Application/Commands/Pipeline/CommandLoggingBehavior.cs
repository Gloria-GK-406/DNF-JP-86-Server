using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Commands.Pipeline;

internal sealed class CommandLoggingBehavior(ILogger<CommandLoggingBehavior> logger)
    : ICommandPipelineBehavior
{
    public int Order => 0;

    public async ValueTask<object?> ExecuteAsync(
        CommandExecutionContext context,
        ICommand command,
        CommandExecutionDelegate next,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await next().ConfigureAwait(false);
            logger.LogInformation(
                "Command {CommandType} handled by {HandlerType} as {ExecutionKind} " +
                "with execution {ExecutionId} in {ElapsedMilliseconds} ms",
                context.CommandType,
                context.HandlerType.Name,
                context.ExecutionKind,
                context.ExecutionId,
                stopwatch.Elapsed.TotalMilliseconds);
            return result;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Command {CommandType} failed in handler {HandlerType} as {ExecutionKind} " +
                "with execution {ExecutionId} after {ElapsedMilliseconds} ms",
                context.CommandType,
                context.HandlerType.Name,
                context.ExecutionKind,
                context.ExecutionId,
                stopwatch.Elapsed.TotalMilliseconds);
            throw;
        }
    }
}
