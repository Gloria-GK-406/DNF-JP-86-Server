namespace Dfo.Server.Application.Commands;

using Dfo.Server.Data.Accounts;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Application.Effects;
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

    ValueTask<TResult> SendInternalAsync<TResult>(ICommand<TResult> command, InternalCommandOrigin origin,
        CancellationToken cancellationToken = default) where TResult : notnull =>
        ValueTask.FromException<TResult>(new NotSupportedException("This legacy command-bus test adapter has no internal entry."));
}

public sealed record CommandExecutionContext(
    Guid ExecutionId,
    Type CommandType,
    Type HandlerType,
    CommandExecutionKind ExecutionKind)
{
    public CommandRequestContext? Request { get; init; }
    private IEffectFootprintRecorder? legacyFootprint;
    private LegacyAdmission? legacyAdmission;

    public IEffectFootprintRecorder LegacyFootprint
    {
        get
        {
            if (Request is not null)
            {
                var error = new InvalidOperationException("An explicit command cannot use the legacy footprint bridge.");
                Request.Failed(error);
                throw error;
            }
            var footprint = legacyFootprint
                ?? throw new InvalidOperationException("This command has no explicitly supplied legacy footprint.");
            if (footprint is LegacyEffectFootprint captured)
            {
                DemandLegacyInvocation();
                captured.DemandAvailable();
            }
            return footprint;
        }
    }

    internal void SupplyLegacyFootprint(IEffectFootprintRecorder footprint)
    {
        if (Request is not null)
        {
            var error = new InvalidOperationException("An explicit command cannot receive a legacy footprint.");
            Request.Failed(error);
            throw error;
        }
        legacyFootprint = footprint ?? throw new ArgumentNullException(nameof(footprint));
    }
    internal IDisposable EnterLegacyInvocation(CommandHandlerRegistration registration)
    {
        if (Request is not null)
        {
            var error = new InvalidOperationException("An explicit command cannot enter a legacy invocation.");
            Request.Failed(error);
            throw error;
        }
        if (registration.Route != CommandRouteKind.LegacyFineGrained || registration.CommandType != CommandType
            || registration.HandlerType != HandlerType || registration.ExecutionKind != ExecutionKind
            || legacyAdmission is { Closed: false })
            throw new InvalidOperationException("The legacy invocation does not match its registration.");
        var admitted = new LegacyAdmission(this, registration, GameplayJobId());
        legacyAdmission = admitted;
        return admitted;
    }

    internal void DemandLegacyInvocation()
    {
        if (Request is not null)
        {
            var error = new InvalidOperationException("An explicit command cannot use legacy admission.");
            Request.Failed(error);
            throw error;
        }
        var admitted = legacyAdmission;
        if (admitted is null || admitted.Closed || !ReferenceEquals(admitted.Owner, this)
            || admitted.ExecutionId != ExecutionId || admitted.Job != GameplayJobId()
            || admitted.Registration.CommandType != CommandType || admitted.Registration.HandlerType != HandlerType
            || admitted.Registration.ExecutionKind != ExecutionKind)
            throw new InvalidOperationException("This context has no matching live legacy invocation admission.");
    }

    private static Guid GameplayJobId() => Dfo.Server.Abstractions.Execution.GameplayJob.Current?.Id
        ?? throw new InvalidOperationException("Legacy admission requires a live gameplay job.");

    private sealed class LegacyAdmission(CommandExecutionContext owner, CommandHandlerRegistration registration, Guid job) : IDisposable
    {
        internal CommandExecutionContext Owner { get; } = owner;
        internal Guid ExecutionId { get; } = owner.ExecutionId;
        internal CommandHandlerRegistration Registration { get; } = registration;
        internal Guid Job { get; } = job;
        internal bool Closed { get; private set; }
        public void Dispose() => Closed = true;
    }

    internal CommandRequestContext DemandRequest()
    {
        var request = Request ?? throw new InvalidOperationException("An explicit command request is required.");
        request.DemandCurrent();
        return request;
    }
}

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
