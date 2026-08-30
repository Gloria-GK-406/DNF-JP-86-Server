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
}
