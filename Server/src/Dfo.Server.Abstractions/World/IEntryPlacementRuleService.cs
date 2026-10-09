using Dfo.Server.Data.Channel;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.World;

/// <summary>
/// Raised when the content cannot say where a character should stand.
/// </summary>
/// <remarks>
/// Ordinary login placement falls back to the configured initial town's declared gate.
/// Missing that gate, or failing to resolve a restricted channel's required spawn, remains
/// an error rather than placing the character at arbitrary coordinates.
/// </remarks>
public sealed class EntryPlacementUnavailableException(string message)
    : InvalidOperationException(message);

/// <summary>Decides where a character stands and what the channel permits.</summary>
public interface IEntryPlacementRuleService
{
    /// <summary>Whether this ordinary town has a usable Seria gate and can become the remembered inn.</summary>
    ValueTask<bool> CanRememberInnAsync(int townId, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves login placement and permissions. Ordinary channels fall back to the initial
    /// town when the remembered inn has no usable arrival gate. Online queries do not call this.
    /// </summary>
    /// <param name="persistedTownId">
    /// The remembered inn town; callers may use the current town for legacy saves with no inn record.
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

    /// <summary>
    /// Whether the content describes one shared town-presence space at this area.
    /// </summary>
    ValueTask<bool> SharesTownPresenceAsync(
        int townId,
        int areaId,
        CancellationToken cancellationToken);
}
