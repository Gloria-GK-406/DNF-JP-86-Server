using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Execution.Outbox;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Application.Transactions;

internal enum DomainTransactionCompletion { Open, ConfirmedRollback, Committed, Uncertain }


/// <summary>
/// Settles one use case's participants together.
/// </summary>
/// <remarks>
/// Commit runs in three stages: every participant prepares, then the single participant whose
/// commit can still fail commits, then the rest commit. The order is the whole guarantee. Up to
/// and including the second stage nothing is visible, so any failure unwinds cleanly; past it
/// the remaining commits have declared that they cannot fail. That is why a second participant
/// whose commit can fail is refused rather than accepted and hoped for.
/// <para>
/// A writable transaction opened on a job also owns that job's outbox work unit (RQ-031(0)(b)): what is
/// registered while it is open is sent only if it commits, its pending compositions are composed after
/// every participant has committed and before control returns to the CMD, and a rollback or an
/// uncertain outcome drops them.
/// </para>
/// </remarks>
internal sealed class DomainTransaction(
    bool readOnly = false, ILogger<DomainTransaction>? logger = null, GameplayOutboxUnit? unit = null) : IDomainTransaction
{
    private readonly Dictionary<object, Func<CancellationToken, ValueTask>> afterCommit = [];
    public bool IsReadOnly => readOnly;
    public bool IsRollbackOnly { get; private set; }
    public void RequireRollback()
    {
        if (settled) throw new InvalidOperationException("The transaction is already settled.");
        IsRollbackOnly = true;
    }

    public void RegisterAfterCommit(object key, Func<CancellationToken, ValueTask> callback)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(callback);
        if (this.settled || this.IsReadOnly) throw new InvalidOperationException("Post-commit effects require an open writable transaction.");
        this.afterCommit[key] = callback;
    }
    private readonly Dictionary<object, ITransactionParticipant> enlisted = [];
    private readonly List<ITransactionParticipant> order = [];
    private ITransactionParticipant? commitFallible;
    private bool settled;

    /// <summary>Whether this transaction has already been committed or rolled back.</summary>
    internal bool IsSettled => this.settled;
    internal DomainTransactionCompletion Completion { get; private set; }

    public TParticipant Enlist<TParticipant>(object identity, Func<TParticipant> create)
        where TParticipant : ITransactionParticipant
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(create);
        if (this.settled)
        {
            throw new InvalidOperationException(
                "The transaction has already been settled and admits no further participant.");
        }

        if (this.enlisted.TryGetValue(identity, out var existing))
        {
            return existing is TParticipant already
                ? already
                : throw new InvalidOperationException(
                    $"'{identity}' already takes part as '{existing.GetType().Name}', which is " +
                    $"not a '{typeof(TParticipant).Name}'.");
        }

        var participant = create();
        if (this.IsReadOnly && !participant.SupportsReadOnly)
            throw new InvalidOperationException("A read-only query cannot enlist a writable state participant.");
        if (participant.CommitCanFail && this.commitFallible is not null)
        {
            throw new InvalidOperationException(
                "A transaction admits at most one participant whose commit can still fail; " +
                $"'{this.commitFallible.GetType().Name}' already holds that place, so " +
                $"'{participant.GetType().Name}' cannot join it. Settling two of them together " +
                "would need a distributed commit protocol this server does not have.");
        }

        this.enlisted.Add(identity, participant);
        this.order.Add(participant);
        if (participant.CommitCanFail)
        {
            this.commitFallible = participant;
        }

        return participant;
    }

    public async ValueTask CommitAsync(CancellationToken cancellationToken)
    {
        if (this.settled)
        {
            throw new InvalidOperationException("The transaction has already been settled.");
        }

        if (IsRollbackOnly)
        {
            await AbortAsync(cause: null).ConfigureAwait(false);
            return;
        }
        this.settled = true;

        try
        {
            foreach (var participant in this.order)
            {
                await participant.PrepareAsync(cancellationToken).ConfigureAwait(false);
            }

            // Everything that can fail has now happened but for this one commit, and nothing is
            // visible yet, so failing here still unwinds with nothing half applied.
            if (this.commitFallible is not null)
            {
                await this.commitFallible.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception failure)
        {
            this.afterCommit.Clear();
            await this.RollbackEnlistedAsync(failure).ConfigureAwait(false);
            throw;
        }

        foreach (var participant in this.order)
        {
            if (ReferenceEquals(participant, this.commitFallible))
            {
                continue;
            }

            try
            {
                await participant.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception violation)
            {
                this.afterCommit.Clear();
                Completion = DomainTransactionCompletion.Uncertain;
                EndUnit(GameplayOutboxUnitState.Uncertain);
                throw new TransactionOutcomeUncertainException(
                    TransactionUncertaintyReason.NonFallibleCommitContractViolation,
                    $"'{participant.GetType().Name}' declared that its commit cannot fail, and it " +
                    "failed. The transaction is now partly applied and cannot be taken back.",
                    violation);
            }
        }
        Completion = DomainTransactionCompletion.Committed;
        // Composed against the committed state, before anything that runs after the commit (RQ-031(0)).
        // A composition that throws leaves the unit uncertain and fails the CMD; the commit stands.
        Exception? composition = null;
        if (unit is { State: GameplayOutboxUnitState.Open })
        {
            try
            {
                await unit.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                composition = error;
            }
        }

        // These effects observe an already committed outcome. Failure cannot turn it into
        // a rollback or a retryable command failure, nor suppress other recipients' effects.
        foreach (var callback in this.afterCommit.Values)
        {
            try
            {
                await callback(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                (logger ?? NullLogger<DomainTransaction>.Instance).LogWarning(error,
                    "Post-commit effect failed; the transaction remains committed.");
            }
        }
        this.afterCommit.Clear();
        if (composition is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(composition);
    }

    public ValueTask RollbackAsync()
        => this.AbortAsync(cause: null);

    internal ValueTask AbortAsync(Exception? cause)
    {
        if (this.settled)
        {
            return ValueTask.CompletedTask;
        }

        this.settled = true;
        this.afterCommit.Clear();
        return this.RollbackEnlistedAsync(cause);
    }

    private void EndUnit(GameplayOutboxUnitState state)
    {
        if (unit is not { State: GameplayOutboxUnitState.Open } open) return;
        if (state == GameplayOutboxUnitState.Uncertain) open.MarkUncertain();
        else open.Rollback();
    }

    private async ValueTask RollbackEnlistedAsync(Exception? cause)
    {
        try
        {
            await RollbackParticipantsAsync(cause).ConfigureAwait(false);
        }
        catch (TransactionOutcomeUncertainException)
        {
            Completion = DomainTransactionCompletion.Uncertain;
            EndUnit(GameplayOutboxUnitState.Uncertain);
            throw;
        }

        Completion = cause is not null && TransactionOutcomeUncertainException.Contains(cause)
            ? DomainTransactionCompletion.Uncertain : DomainTransactionCompletion.ConfirmedRollback;
        EndUnit(GameplayOutboxUnitState.RolledBack);
    }

    private async ValueTask RollbackParticipantsAsync(Exception? cause)
    {
        List<Exception>? failures = null;
        for (var index = this.order.Count - 1; index >= 0; index--)
        {
            try
            {
                // Unwinding is not cancellable. A rollback abandoned halfway would leave exactly
                // the partly applied state this transaction exists to prevent.
                await this.order[index].RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                (failures ??= []).Add(failure);
            }
        }

        if (failures is null)
        {
            return;
        }

        var causes = cause is null
            ? new AggregateException("Rolling back the transaction failed.", failures)
            : new AggregateException(
                "Rolling back the transaction failed while unwinding another failure.",
                failures.Prepend(cause));
        throw new TransactionOutcomeUncertainException(
            TransactionUncertaintyReason.RollbackFailure,
            "The transaction could not confirm a complete rollback.", causes);
    }
}
