using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Abstractions.World;

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
/// It is reached two ways. Inside a use case -- a session starting, or one ending because another
/// displaced it -- it writes through that use case. On connection teardown there is no use case at
/// all, because the server deliberately does not model teardown as a command, and rather than
/// exempting the write from needing a boundary it opens one of its own. The guarantee that matters
/// is that a change to a character's stored state is settled by a transaction, not that a command
/// was dispatched.
///
/// It lives beside the domain rather than in the persistence implementation because deciding which
/// of those two it is doing is the whole of its job; the persistence side is only asked to hold
/// the place.
/// </remarks>
internal sealed class SelectedSessionPositionPersistence(
    ICharacterDomainStore? store, IDomainTransactionAccessor transactions, IEntryPlacementRuleService? placement)
    : ISelectedSessionPositionPersistence
{
    public async ValueTask PersistAsync(
        AccountId accountId,
        CharacterId characterId,
        CharacterPositionState position,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(position);
        if (store is null)
        {
            throw new InvalidOperationException(
                "This server was assembled without anywhere to keep a character, so where one was "
                + "standing cannot be saved.");
        }

        var owned = new OwnedCharacterId(accountId, characterId);
        if (placement is null) throw new InvalidOperationException("Position persistence requires inn placement rules.");
        byte? innTown = await placement.CanRememberInnAsync(position.TownId, cancellationToken).ConfigureAwait(false)
            ? position.TownId : null;
        if (transactions.Current is { } ambient)
        {
            // The use case settles this one. Opening a second boundary beside it would put the
            // same database on another connection and leave the two to finish independently --
            // the very thing a boundary exists to prevent.
            store.SessionIn(ambient).WritePosition(owned, position, innTown);
            return;
        }

        await this.AloneAsync(owned, position, innTown, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask AloneAsync(
        OwnedCharacterId owned, CharacterPositionState position, byte? innTown, CancellationToken cancellationToken)
    {
        var alone = new DomainTransaction();
        try
        {
            store!.SessionIn(alone).WritePosition(owned, position, innTown);
            await alone.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await alone.RollbackAsync().ConfigureAwait(false);
            throw;
        }
    }
}
