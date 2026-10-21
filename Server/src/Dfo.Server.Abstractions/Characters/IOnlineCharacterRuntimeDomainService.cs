using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Characters;

/// <summary>
/// What a character being played is like right now: where it stands, its vitals, the cooldowns
/// it is waiting on and the spirit potion it is under.
/// </summary>
/// <remarks>
/// Who the character is belongs to the selected session; this is how it is, and only while it is
/// being played. It is not the character domain service: that one reads what is stored, and
/// nothing here is stored except the position, which the session's teardown writes back.
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
    /// Starts the record of a session entering the world, replacing any earlier record of the
    /// character. Cooldowns and the spirit potion start empty.
    /// </summary>
    void Begin(SelectedSessionIdentity session, CharacterPositionState position, byte hpPercent);

    /// <summary>Ends exactly this session's record. False when it does not own one.</summary>
    bool EndExact(SelectedSessionIdentity session);

    /// <summary>Where this session's character stands, or null when the session owns no record.</summary>
    CharacterPositionState? PositionOf(SelectedSessionIdentity session);

    /// <summary>Where a character being played stands, or null when it is not being played.</summary>
    CharacterPositionState? PositionOf(CharacterId characterId);

    /// <summary>Moves the character into another area.</summary>
    /// <remarks>The facing is the one the character already had: the crossing report does not carry one.</remarks>
    /// <exception cref="InvalidOperationException">The session does not own the record.</exception>
    CharacterPositionState CrossInto(
        SelectedSessionIdentity session, byte townId, byte areaId, short x, short y);

    /// <summary>Moves the character within its area.</summary>
    /// <exception cref="InvalidOperationException">The session does not own the record.</exception>
    CharacterPositionState StepTo(
        SelectedSessionIdentity session, short x, short y, byte direction);

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
