using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Inventory;

/// <summary>Whether a character may use an item, and when not, why not.</summary>
/// <remarks>
/// The refusals are named rather than counted because the client can say each of them out loud:
/// it has separate sentences for the wrong job, too low a level, and "not right now". Answering
/// with a boolean would throw away information the player is able to act on.
/// </remarks>
public enum ItemUsability
{
    Allowed = 0,

    /// <summary>
    /// The archive declares nothing usable under that identifier, or declares it in terms this
    /// contract cannot read.
    /// </summary>
    NotAUsableItem,

    LevelTooLow,
    JobMismatch,
    OnCooldown,
    PlaceNotAllowed,

    /// <summary>
    /// The character's own state could not be read, so no rule could be applied.
    /// </summary>
    /// <remarks>
    /// Not a refusal the player caused. Entry registers the session and binds the container before
    /// it publishes the runtime state, so a request arriving in that window finds a character it
    /// cannot ask about. Folded in with the archive's own refusals it would be answered with
    /// 「非消耗品， 无法使用」 -- a sentence about the item, for a gap in the server.
    /// </remarks>
    CharacterStateUnavailable,
}

/// <summary>
/// Answers whether a character may use an item.
/// </summary>
/// <remarks>
/// Read-only, and it gathers its own facts. A caller that had to assemble the cooldown record, the
/// character's level and job, and where they are standing would be a caller that could assemble
/// them wrongly -- and every new caller would assemble them again. The question a protocol handler
/// has is exactly "may this character use this item"; that is the whole signature.
/// </remarks>
public interface IItemUsabilityDomainService
{
    ValueTask<ItemUsability> CanUseAsync(
        CharacterId characterId,
        int itemId,
        CancellationToken cancellationToken);
}
