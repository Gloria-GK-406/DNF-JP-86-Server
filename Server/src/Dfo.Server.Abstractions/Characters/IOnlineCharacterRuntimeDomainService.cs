using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Characters;

/// <summary>
/// What a character being played is like right now: where it is, its vitals, the cooldowns it is
/// waiting on and the spirit potion it is under.
/// </summary>
/// <remarks>
/// Who the character is belongs to the selected session; this is how it is, and only while it is
/// being played. It is not the character domain service: that one reads what is stored, and
/// nothing here is stored except the position, which area crossings and the session's teardown
/// write back.
/// <para>
/// The position is kept by the character location service, which also knows who stands in each
/// town area; this service is the only way to change it, so a character's position and its place
/// in an area cannot disagree. Questions about an area as a whole -- who stands in it -- are asked
/// of <see cref="ITownAreaDomainService"/> instead.
/// </para>
/// <para>
/// Each character has at most one record, and the record belongs to the session generation that
/// began it. A write names its session and is refused or ignored when that session no longer owns
/// the record, so a replaced session cannot reach its successor and nothing can re-create a record
/// after its session ended. A read that names a session answers only for that session; a read by
/// character answers for whichever session is playing it now.
/// </para>
/// </remarks>
public interface IOnlineCharacterRuntimeDomainService
{
    /// <summary>
    /// Starts the record of a session entering the world, standing in its first area, replacing any
    /// earlier record of the character. Cooldowns and the spirit potion start empty.
    /// </summary>
    TownAreaMembershipChange Begin(
        SelectedSessionIdentity session,
        TownAreaKey area,
        CharacterPositionState position,
        bool sharesTownPresence,
        byte hpPercent);

    /// <summary>Ends exactly this session's record. False when it does not own one.</summary>
    bool EndExact(SelectedSessionIdentity session);

    /// <summary>
    /// Where this session's character is -- standing in town, or the place it left while away --
    /// or null when the session owns no record.
    /// </summary>
    CharacterPositionState? PositionOf(SelectedSessionIdentity session);

    /// <summary>Where a character being played is, in town or away, or null when it is not being played.</summary>
    CharacterPositionState? PositionOf(CharacterId characterId);

    /// <summary>
    /// Where this session's character stands in town, or null when the session owns no record or
    /// its character is away from town.
    /// </summary>
    TownAreaMember? StandingOf(SelectedSessionIdentity session);

    /// <summary>Moves a character standing in town into another area.</summary>
    /// <param name="direction">The facing to take; the one the character had when null, as the crossing report carries none.</param>
    /// <returns>
    /// The transfer, or null when the session's character is not standing in town, the destination
    /// is a couple room without its instance, or nothing would change.
    /// </returns>
    TownAreaMembershipChange? CrossInto(
        SelectedSessionIdentity session,
        TownAreaKey destination,
        short x,
        short y,
        bool sharesTownPresence,
        byte? direction = null);

    /// <summary>Moves a character standing in town within its area.</summary>
    /// <returns>The movement, or null when the session's character is not standing in town or is already there.</returns>
    TownAreaMovement? StepTo(SelectedSessionIdentity session, short x, short y, byte direction);

    /// <summary>Takes the character out of town -- to choose or run a dungeon -- keeping where it stood.</summary>
    /// <returns>The departure, or null when the session's character is not standing in town.</returns>
    TownAreaMembershipChange? LeaveTown(SelectedSessionIdentity session, TownAreaDepartureReason reason);

    /// <summary>Stands the character in a town area, back from away or from another area.</summary>
    /// <returns>The arrival, or null when the session owns no record.</returns>
    TownAreaMembershipChange? ReturnToTown(
        SelectedSessionIdentity session,
        TownAreaKey area,
        CharacterPositionState position,
        bool sharesTownPresence);

    /// <summary>The character's HP percentage, or zero when it is not being played.</summary>
    byte HpPercentOf(CharacterId characterId);

    bool IsCoolingDown(CharacterId characterId, ItemCooldownKey key);

    /// <summary>Starts a cooldown for this session. Ignored when the session owns no record.</summary>
    void StartCooldown(SelectedSessionIdentity session, ItemCooldownKey key, TimeSpan duration);

    /// <summary>Puts this session's character under a spirit potion. Ignored when the session owns no record.</summary>
    void ApplySpiritPotion(SelectedSessionIdentity session, double bonusRate, TimeSpan duration);

    /// <summary>The spirit potion's experience bonus, or zero when none is running.</summary>
    double SpiritPotionBonusRateOf(CharacterId characterId);
}
