using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Characters;

/// <summary>
/// Answers a character's keys and options out of the two tables that hold them.
/// </summary>
/// <remarks>
/// The options row is the character: a character with no row in <c>character_settings</c> has no
/// settings, and that is what nothing is answered for. The hotkey slots are a separate table with
/// a row per slot, and a character with a settings row and no slots has an empty list of them --
/// that is a character who has never had keys stored, not a character who is not there.
/// <para>
/// The two conditions on the hotkey rows are written the same way the entry load writes them, so
/// that the reader added here and the one already there do not disagree about a slot. The value
/// range is also what makes the conversion to the stored width total. The entry load's further
/// refusals are not repeated: they are checks on the whole profile record it is assembling, and
/// this assembles no such record.
/// </para>
/// <para>
/// Nothing is kept between calls. Both reads go to the database each time, which is right for a
/// service asked from outside the entry sequence: the entry sequence reads these rows once and
/// carries the answer forward for a session, and anything asking later wants what is stored now.
/// </para>
/// </remarks>
internal sealed class CharacterSettingsDomainService(
    ICharacterSettingsTable settings,
    ICharacterHotkeyTable hotkeys) : ICharacterSettingsDomainService
{
    /// <summary>The most slots one character may have stored, as the entry load also requires.</summary>
    private const int MaximumHotkeySlots = 256;

    public async ValueTask<CharacterSettingsEntryState?> FindAsync(
        CharacterId characterId, CancellationToken cancellationToken)
    {
        var row = await settings.FindAsync(characterId, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        var slots = await hotkeys.ReadAsync(characterId, cancellationToken).ConfigureAwait(false);
        if (slots.Any(value => value is < 0 or > ushort.MaxValue)
            || slots.Count > MaximumHotkeySlots)
        {
            throw new InvalidDataException("Character hotkey state is out of range.");
        }

        // The option block is passed on as it was read, null and all. A block that was never
        // stored and an empty one are different statements about a character and the reader
        // underneath keeps them apart; collapsing them here would undo that.
        return new CharacterSettingsEntryState(
            [.. slots.Select(value => (ushort)value)],
            row.OptionBlob,
            row.UserStateBits);
    }
}
