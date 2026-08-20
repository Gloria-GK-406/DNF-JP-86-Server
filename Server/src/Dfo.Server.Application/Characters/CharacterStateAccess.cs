using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Characters;

/// <summary>
/// Reaches a store through whatever boundary the caller is inside.
/// </summary>
/// <remarks>
/// This exists so that business code never names a transaction. It says what it wants to read or
/// change; whether a use case boundary is open, and what to do when one is not, is decided here.
///
/// Reading outside a use case is allowed -- it is how state is looked at without anything being
/// changed -- and the session it opens is discarded rather than settled, because a read has
/// nothing to settle.
///
/// Changing durable state outside a use case is refused. Such a change would be one nothing could
/// take back: no transaction to unwind it, and no failure for anyone to see. That is exactly the
/// silent non-atomic write this whole boundary exists to make impossible. A store that keeps
/// nothing past the process is not refused, because there is nothing to be left half done.
/// </remarks>
internal sealed class AmbientStoreAccess(IDomainTransactionAccessor transactions)
{
    public async ValueTask<TResult> ReadAsync<TSession, TResult>(
        IDomainStore<TSession> store, Func<TSession, ValueTask<TResult>> read)
        where TSession : class
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(read);
        if (transactions.Current is { } ambient)
        {
            return await read(store.SessionIn(ambient)).ConfigureAwait(false);
        }

        var alone = new DomainTransaction();
        try
        {
            return await read(store.SessionIn(alone)).ConfigureAwait(false);
        }
        finally
        {
            await alone.RollbackAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Makes a change to a store, inside whatever boundary is appropriate.</summary>
    /// <remarks>
    /// The change is handed in rather than the session handed out, because a change made outside a
    /// use case needs a boundary opened around it and then settled -- and nothing could settle a
    /// boundary the caller never learned about. An earlier shape returned the session and left the
    /// one-shot transaction with no one to commit it, so writes to a volatile store outside a use
    /// case were staged and then dropped: no exception, no effect.
    /// </remarks>
    public async ValueTask ChangeAsync<TSession>(
        IDomainStore<TSession> store,
        Func<TSession, ValueTask> change,
        CancellationToken cancellationToken)
        where TSession : class
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(change);
        if (transactions.Current is { } ambient)
        {
            // The use case settles this one; finishing it here would settle it early.
            await change(store.SessionIn(ambient)).ConfigureAwait(false);
            return;
        }

        if (store.IsDurable)
        {
            throw new InvalidOperationException(
                "State that outlives the process cannot be changed outside a use case boundary; "
                + "there would be no transaction to unwind the change and no failure for anyone "
                + "to see.");
        }

        var alone = new DomainTransaction();
        try
        {
            await change(store.SessionIn(alone)).ConfigureAwait(false);
            await alone.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await alone.RollbackAsync().ConfigureAwait(false);
            throw;
        }
    }
}

/// <summary>
/// Saves the exact place a character was standing when their session was torn down.
/// </summary>
/// <remarks>
/// This runs on connection teardown, which the server deliberately does not model as a command --
/// so there is no use case boundary around it. Rather than exempting the write from needing one,
/// it opens a boundary of its own and settles it. The guarantee that matters is that a change to a
/// character's stored state is settled by a transaction, not that a command was dispatched.
///
/// It lives beside the domain rather than in the persistence implementation because opening that
/// boundary is what it does; the persistence side is only asked to hold the place.
/// </remarks>
internal sealed class SelectedSessionPositionPersistence(
    ICharacterStore? store, IDomainTransactionAccessor transactions)
    : ISelectedSessionPositionPersistence
{
    public async ValueTask PersistAsync(
        AccountId accountId,
        CharacterId characterId,
        CharacterPositionState position,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(position);
        if (transactions.Current is not null)
        {
            // This exists to open a boundary where there is none. Settling one of its own beside
            // an open one would put the same database on a second connection and leave the two to
            // finish independently -- which is the thing the boundary exists to prevent.
            throw new InvalidOperationException(
                "Where a character was standing is saved outside a use case; a use case is open, "
                + "so it should be saved through that instead.");
        }

        if (store is null)
        {
            throw new InvalidOperationException(
                "This server was assembled without anywhere to keep a character, so where one was "
                + "standing cannot be saved.");
        }

        var alone = new DomainTransaction();
        try
        {
            store.SessionIn(alone)
                .WritePosition(new OwnedCharacterId(accountId, characterId), position);
            await alone.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await alone.RollbackAsync().ConfigureAwait(false);
            throw;
        }
    }
}
