using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.Effects;

namespace Dfo.Server.Application.Commands;

public enum CommandWorkUnitKind { Ordinary, MainSettlement }
public enum CommandFootprintMode { None, NonRequest }
public sealed record CommandWorkUnitDeclaration(string Name, IReadOnlyList<SelectedSessionRecipient> Participants,
    CommandWorkUnitKind Kind = CommandWorkUnitKind.Ordinary, CommandFootprintMode Footprint = CommandFootprintMode.None);

public sealed class CommandWorkUnit
{
    private readonly IDomainTransaction transaction;
    private readonly CommandRequestContext request;
    private bool open = true;
    private readonly IEffectFootprintRecorder? footprint;
    internal CommandWorkUnit(IDomainTransaction transaction, CommandRequestContext request, IEffectFootprintRecorder? footprint = null)
    { this.transaction = transaction; this.request = request; this.footprint = footprint; }
    public IEffectFootprintRecorder Footprint
    {
        get
        {
            request.DemandCurrent();
            if (!open || footprint is null) throw new InvalidOperationException("This work unit has no live declared effect footprint.");
            return footprint;
        }
    }
    public void RequireRollback()
    {
        request.DemandCurrent();
        if (!open) throw new InvalidOperationException("The command work unit has ended.");
        transaction.RequireRollback();
    }
    internal Exception? RollbackCause { get; private set; }
    internal Exception? RejectedRollbackCause { get; private set; }
    public void RequireRollback(Exception cause)
    {
        ArgumentNullException.ThrowIfNull(cause);
        RequireRollback();
        RollbackCause ??= cause;
        if (cause is OperationCanceledException || TransactionOutcomeUncertainException.Contains(cause))
        {
            RejectedRollbackCause = cause;
            request.Failed(cause);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(cause);
        }
    }
    internal void Close() => open = false;
}

public abstract class CommandWriteResult<T> where T : notnull
{
    private CommandWriteResult() { }
    public sealed class Returned : CommandWriteResult<T>
    {
        internal Returned(T value) => Value = value;
        public T Value { get; }
    }
    public sealed class CommitRolledBack : CommandWriteResult<T>
    {
        internal CommitRolledBack(Exception cause) => Cause = cause;
        public Exception Cause { get; }
        public CommandWriteCommitAttempt CommitAttempt => CommandWriteCommitAttempt.CommitAsync;
    }
}
public enum CommandWriteCommitAttempt { CommitAsync }

public interface ICommandWorkUnits
{
    ValueTask<CommandWriteResult<TResult>> WriteResultAsync<TResult>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
        Func<CommandWorkUnit, CancellationToken, ValueTask<TResult>> action, CancellationToken cancellationToken = default)
        where TResult : notnull;
    ValueTask<TResult> WriteAsync<TResult>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
        Func<CommandWorkUnit, CancellationToken, ValueTask<TResult>> action, CancellationToken cancellationToken = default)
        where TResult : notnull;
    ValueTask<TResult> ReadOnlyAsync<TResult>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
        Func<CancellationToken, ValueTask<TResult>> action, CancellationToken cancellationToken = default)
        where TResult : notnull;
}
