using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

/// <summary>A mode whose entry ticket is also topped up for characters already in the world at the reset.</summary>
/// <remarks>
/// The ticket id is the one fact both halves of that refill read: the reset job grants it online, and
/// the first-login batch must leave it to its own period. Declaring it once here is what keeps a new
/// ticket mode from being refilled twice, or never, because only one of the two learned its id.
/// </remarks>
public interface IOnlineRefilledTicketRule
{
    int TicketItemId { get; }
}

/// <summary>Grants a mode's ticket to one character still in the world when the game day turns.</summary>
public interface IOnlineTicketRefillDomainService
{
    /// <returns>False when the session has already left the world; the job then skips it.</returns>
    ValueTask<bool> RefillOnlineTicketAsync(SelectedSessionIdentity session, CancellationToken ct);
}
