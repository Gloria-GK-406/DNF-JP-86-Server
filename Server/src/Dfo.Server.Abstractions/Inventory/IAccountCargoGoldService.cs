using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Abstractions.Inventory;

/// <summary>
/// The gold an account keeps in its cargo.
/// </summary>
/// <remarks>
/// <b>This is not the gold the player is carrying.</b> The game has two of them. The one on the
/// player's bag belongs to a single character and lives in the first virtual slot of its main
/// list; this one belongs to the account, is shared by every character on it, and is only visible
/// once the account cargo is opened. The client has a pair of protocols whose whole purpose is to
/// move an amount between them, which is what settles that they are two accounts rather than one.
/// <para>
/// It is stored as <c>account_cargo_state.value32</c>, read into the account's cargo at entry, and
/// written into the item-list frame between the cargo's selection key and its item count -- the
/// same offset the reference server writes its own <c>AccountCargo.Money</c> at, and the same one
/// this repository's own record of that frame calls the cargo's gold. So this seam names an
/// existing field rather than adding one.
/// </para>
/// <para>
/// Account-scoped because the field is. Every member here takes the account it acts on, so a
/// caller holding only a character cannot reach this seam by accident.
/// </para>
/// </remarks>
public interface IAccountCargoGoldService
{
    /// <summary>
    /// Adds to what the account's cargo holds and answers the total afterwards.
    /// </summary>
    /// <remarks>
    /// A negative amount takes gold away. The total is held at zero and at the wire field's
    /// ceiling: it is a signed 32-bit integer and a total that wrapped would draw a negative
    /// figure on the cargo.
    /// </remarks>
    ValueTask<int> AddAsync(AccountId accountId, int amount, CancellationToken cancellationToken);

    /// <summary>
    /// Takes that much out of the account's cargo, or takes nothing at all.
    /// </summary>
    /// <remarks>
    /// Answers what the cargo holds afterwards, and <c>null</c> when it did not hold that much --
    /// in which case nothing was taken and the total stands exactly where it stood.
    /// <para>
    /// <b>This is not <see cref="AddAsync"/> with the sign turned round, and it may not be
    /// replaced by one.</b> That one holds its total at zero, so an account asked for more than it
    /// has is emptied and then answers as though it had paid. Charging with it would record a
    /// player who cannot afford something as having bought it, and the gold it did not have would
    /// be gone as well. Refusing is the whole reason this member exists.
    /// </para>
    /// <para>
    /// An account whose cargo has never held gold holds nothing, so nothing above zero can be
    /// taken from it. A take of zero is a charge of nothing and is always allowed; a negative
    /// amount is not, because a negative take is an addition wearing the other name, and handing
    /// gold out is the one thing a charging seam must not be able to do by accident.
    /// </para>
    /// </remarks>
    ValueTask<int?> TrySpendAsync(
        AccountId accountId, int amount, CancellationToken cancellationToken);
}
