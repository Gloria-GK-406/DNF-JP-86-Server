using Dfo.Server.Abstractions.Transactions;

namespace Dfo.Server.Application.Transactions;

/// <summary>
/// Settles one use case's participants together.
/// </summary>
/// <remarks>
/// Commit runs in three stages: every participant prepares, then the single participant whose
/// commit can still fail commits, then the rest commit. The order is the whole guarantee. Up to
/// and including the second stage nothing is visible, so any failure unwinds cleanly; past it
/// the remaining commits have declared that they cannot fail. That is why a second participant
/// whose commit can fail is refused rather than accepted and hoped for.
/// </remarks>
internal sealed class DomainTransaction : IDomainTransaction
{
    private readonly Dictionary<object, ITransactionParticipant> enlisted = [];
    private readonly List<ITransactionParticipant> order = [];
    private ITransactionParticipant? commitFallible;
    private bool settled;

    /// <summary>Whether this transaction has already been committed or rolled back.</summary>
    internal bool IsSettled => this.settled;

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

        this.settled = true;
        if (this.order.Count == 0)
        {
            return;
        }

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
                throw new InvalidOperationException(
                    $"'{participant.GetType().Name}' declared that its commit cannot fail, and it " +
                    "failed. The transaction is now partly applied and cannot be taken back.",
                    violation);
            }
        }
    }

    public ValueTask RollbackAsync()
    {
        if (this.settled)
        {
            return ValueTask.CompletedTask;
        }

        this.settled = true;
        return this.RollbackEnlistedAsync(cause: null);
    }

    private async ValueTask RollbackEnlistedAsync(Exception? cause)
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

        throw cause is null
            ? new AggregateException("Rolling back the transaction failed.", failures)
            : new AggregateException(
                "Rolling back the transaction failed while unwinding another failure.",
                failures.Prepend(cause));
    }
}
