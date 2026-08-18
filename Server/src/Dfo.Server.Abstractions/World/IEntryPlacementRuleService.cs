using Dfo.Server.Data.Channel;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.World;

/// <summary>
/// Raised when the content cannot say where a character should stand.
/// </summary>
/// <remarks>
/// Placement has no safe default. Dropping a character at an arbitrary coordinate would
/// leave them outside the walkable world with no indication that anything went wrong, so
/// an unresolvable spawn is an error.
/// </remarks>
public sealed class EntryPlacementUnavailableException(string message)
    : InvalidOperationException(message);

/// <summary>Decides where a character stands and what the channel permits.</summary>
public interface IEntryPlacementRuleService
{
    /// <summary>
    /// Resolves the placement and permissions for a character entering on a channel.
    /// </summary>
    /// <param name="persistedTownId">
    /// The town the character was last in, or zero when they have none.
    /// </param>
    /// <exception cref="EntryPlacementUnavailableException">
    /// The content defines no usable arrival point for the resolved town.
    /// </exception>
    ValueTask<ChannelEntryPolicyState> ResolveAsync(
        ChannelEntryIdentity channel,
        int persistedTownId,
        CancellationToken cancellationToken);

    /// <summary>Whether a character on this channel may enter a given town.</summary>
    ValueTask<bool> CanEnterTownAsync(
        ChannelEntryIdentity channel,
        int townId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Whether the content declares this area of this town, and so whether a character can stand
    /// there at all.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="CanEnterTownAsync"/>, which answers a channel-policy question about
    /// a town the content is already known to declare. This one answers the prior question, and it
    /// is asked wherever the client proposes a destination: the town byte the entry acknowledgement
    /// carries is a lookup key the client dereferences unchecked, so an undeclared town accepted
    /// here becomes a crash on the next login rather than an error now.
    /// </remarks>
    ValueTask<bool> DeclaresAreaAsync(int townId, int areaId, CancellationToken cancellationToken);
}
