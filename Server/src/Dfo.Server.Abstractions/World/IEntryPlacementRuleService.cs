using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
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

/// <summary>Why the couple-room rules refuse an area crossing.</summary>
public enum TownCrossingRefusal
{
    /// <summary>Only a married character enters, from an entry source or from inside, to area 0.</summary>
    CoupleRoomEntry,

    /// <summary>The couple room is left only for the area it was entered from.</summary>
    CoupleRoomExit,
}

/// <summary>What crossing between two towns does to the session's household lease.</summary>
public enum TownHousingChange
{
    None,

    /// <summary>Entering the couple room from outside; the way back is remembered first.</summary>
    Enters,

    /// <summary>Leaving the couple room; the lease and the way back end.</summary>
    Leaves,
}

/// <summary>The facts a couple-room crossing is judged on, read by the caller.</summary>
/// <param name="StandingAtEntrySource">Whether the character stands at a couple-room entry source.</param>
/// <param name="HouseholdInstance">The character's household couple-room instance, or null when it has none.</param>
/// <param name="WayBack">Where the character entered the couple room from, or null when none is remembered.</param>
public sealed record TownCrossingFacts(
    CharacterPositionState Standing,
    int TownId,
    int AreaId,
    bool StandingAtEntrySource,
    int? HouseholdInstance,
    CharacterPositionState? WayBack);

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
    /// <remarks>
    /// The couple room is shared whatever its area says: one instance holds one household, and
    /// both see each other.
    /// </remarks>
    ValueTask<bool> SharesTownPresenceAsync(
        int townId,
        int areaId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Whether this town is a household's own area -- the couple room -- whose instance, entry,
    /// exit and storage <see cref="AreaInstance"/>, <see cref="RefuseCrossing"/>,
    /// <see cref="StoredPositionAfter"/> and <see cref="HousingChangeOf"/> decide.
    /// </summary>
    bool IsHouseholdTown(int townId);

    /// <summary>
    /// The area instance a character stands in at this town: zero for an ordinary town, the
    /// household's instance for the couple room, or null when the character has no household to enter.
    /// </summary>
    int? AreaInstance(int townId, int? householdInstance);

    /// <summary>Whether the couple-room rules refuse a crossing; null when they allow it or do not apply.</summary>
    TownCrossingRefusal? RefuseCrossing(TownCrossingFacts facts);

    /// <summary>
    /// The position to store for a crossing. A couple-room position is never stored: a character
    /// inside keeps the place it entered from, so the next login lands there.
    /// </summary>
    CharacterPositionState StoredPositionAfter(CharacterPositionState standing, CharacterPositionState destination);

    /// <summary>What crossing from one town to another does to the session's household lease.</summary>
    TownHousingChange HousingChangeOf(int fromTownId, int toTownId);
}
