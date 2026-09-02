using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Abstractions.Inventory;

/// <summary>
/// What an account is carrying, in money.
/// </summary>
/// <remarks>
/// <b>The money was already on the wire; nothing had ever named it.</b> It is stored as
/// <c>account_cargo_state.value32</c>, read into the account's cargo at entry, and written into the
/// item-list frame between the cargo's selection key and its item count. The reference server
/// writes its own <c>AccountCargo.Money</c> at exactly that offset, and its repair acknowledgement
/// records that the client overwrites its displayed purse from that field
/// (<c>CInventory::get_money</c>). So this seam names an existing field rather than adding one.
/// <para>
/// Account-scoped, not character-scoped, because that is where the field lives. Every character on
/// an account draws on one purse.
/// </para>
/// </remarks>
public interface IAccountMoneyService
{
    /// <summary>
    /// Adds to what the account carries and answers the total afterwards.
    /// </summary>
    /// <remarks>
    /// A negative amount takes money away. The total is held at zero and at the wire field's
    /// ceiling: it is a signed 32-bit integer and a total that wrapped would draw the player a
    /// negative purse.
    /// </remarks>
    ValueTask<int> AddAsync(AccountId accountId, int amount, CancellationToken cancellationToken);

    /// <summary>
    /// Takes that much out of the account, or takes nothing at all.
    /// </summary>
    /// <remarks>
    /// Answers what the account carries afterwards, and <c>null</c> when it did not carry that
    /// much -- in which case nothing was taken and the purse stands exactly where it stood.
    /// <para>
    /// <b>This is not <see cref="AddAsync"/> with the sign turned round, and it may not be
    /// replaced by one.</b> That one holds its total at zero, so an account asked for more than it
    /// has is emptied and then answers as though it had paid. Charging with it would record a
    /// player who cannot afford something as having bought it, and the money it did not have would
    /// be gone as well. Refusing is the whole reason this member exists.
    /// </para>
    /// <para>
    /// An account that has never held money carries nothing, so nothing above zero can be taken
    /// from it. A take of zero is a charge of nothing and is always allowed; a negative amount is
    /// not, because a negative take is an addition wearing the other name, and handing money out
    /// is the one thing a charging seam must not be able to do by accident.
    /// </para>
    /// </remarks>
    ValueTask<int?> TrySpendAsync(
        AccountId accountId, int amount, CancellationToken cancellationToken);
}
