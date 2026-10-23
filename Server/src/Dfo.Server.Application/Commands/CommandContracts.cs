namespace Dfo.Server.Application.Commands;

using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.World;

public enum CommandExecutionKind
{
    Direct = 0,
    Transactional = 1,
    Workflow = 2,
    ReadOnly = 3,
}

/// <summary>A dispatchable command. Its CLR type is its identity.</summary>
public interface ICommand;

/// <summary>A command and the result its handler produces.</summary>
/// <remarks>
/// Deliberately invariant in <typeparamref name="TResult"/>. Covariance would let a
/// command be widened to <c>ICommand&lt;object&gt;</c> and dispatched under a result type
/// no handler is registered for, which the bus could only discover at runtime.
/// </remarks>
public interface ICommand<TResult> : ICommand
    where TResult : notnull;

/// <summary>Effects owned by one exact selected generation; null is trusted offline work.</summary>
public interface ISelectedSessionCommand : ICommand
{
    SelectedSessionIdentity? Session { get; }
}

/// <summary>The single primary settlement belonging to an effect network request. XP and reads are separate.</summary>
public interface IRequestSettlementCommand : ISelectedSessionCommand;

public interface IMultiSelectedSessionCommand : ISelectedSessionCommand
{
    IReadOnlyList<SelectedSessionIdentity> Sessions { get; }
}

public interface IAccountCommand : ICommand
{
    AccountId AccountId { get; }
}

public interface ICommandBus
{
    ValueTask<TResult> SendAsync<TResult>(
        ICommand<TResult> command,
        CancellationToken cancellationToken = default)
        where TResult : notnull;
}

public sealed record CommandExecutionContext(
    Guid ExecutionId,
    Type CommandType,
    Type HandlerType,
    CommandExecutionKind ExecutionKind);

/// <summary>The remainder of the pipeline, carrying the command's own result type.</summary>
public delegate ValueTask<TResult> CommandExecutionDelegate<TResult>()
    where TResult : notnull;

public interface ICommandPipelineBehavior<TResult>
    where TResult : notnull
{
    int Order { get; }

    ValueTask<TResult> ExecuteAsync(
        CommandExecutionContext context,
        ICommand command,
        CommandExecutionDelegate<TResult> next,
        CancellationToken cancellationToken);
}

/// <summary>
/// Handles one command. The pairing is declared here, in the type arguments, and is
/// checked where the handler is registered.
/// </summary>
public interface ICommandHandler<TCommand, TResult>
    where TCommand : class, ICommand<TResult>
    where TResult : notnull
{
    ValueTask<TResult> HandleAsync(
        CommandExecutionContext context,
        TCommand command,
        CancellationToken cancellationToken);
}

/// <summary>
/// Lets the bus reach a handler while knowing only the result type.
/// </summary>
/// <remarks>
/// <see cref="ICommandBus.SendAsync{TResult}"/> receives its argument as
/// <c>ICommand&lt;TResult&gt;</c>, so the command's own type is not a static type argument
/// at the call site and the handler cannot be resolved directly. This adapter closes over
/// the command type at registration, where it is known, and exposes only the result type.
/// </remarks>
internal interface ICommandInvoker<TResult>
    where TResult : notnull
{
    ValueTask<TResult> InvokeAsync(
        CommandExecutionContext context,
        ICommand<TResult> command,
        CancellationToken cancellationToken);
}

internal sealed class CommandInvoker<TCommand, TResult>(ICommandHandler<TCommand, TResult> handler)
    : ICommandInvoker<TResult>
    where TCommand : class, ICommand<TResult>
    where TResult : notnull
{
    public ValueTask<TResult> InvokeAsync(
        CommandExecutionContext context,
        ICommand<TResult> command,
        CancellationToken cancellationToken) =>
        // The registry keys on the exact command type, so the only command that reaches
        // this invoker is the one it was registered for. A command type that were
        // subclassed would miss the lookup and fail there, never arriving here.
        handler.HandleAsync(context, (TCommand)command, cancellationToken);
}
