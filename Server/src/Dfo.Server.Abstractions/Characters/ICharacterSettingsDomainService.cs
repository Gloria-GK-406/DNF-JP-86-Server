using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Characters;

/// <summary>
/// A character's key bindings and client options, asked for by character.
/// </summary>
/// <remarks>
/// They are stored in two tables -- one row of options per character, one row per hotkey slot --
/// and the only code that ever put the two together was the entry load, on its way to the frames
/// a character is sent when they enter the world. So the only way to find out what somebody's keys
/// were was to enter the world as them.
/// <para>
/// This is the seam that answers the question on its own. It reads the same two tables the entry
/// load reads, and it does not change the entry load: that one goes on building its frames from
/// the storage records it already has in hand, because they are pure functions over rows already
/// read, and making them ask a service instead would turn each into a world query and put a
/// database round trip on a path that has none.
/// </para>
/// <para>
/// It answers and nothing else. A character's settings row is put there with its declared defaults
/// when the character is created and no statement in this server ever updates it, and no statement
/// writes a hotkey slot at all -- a player rebinding a key has nowhere to land today. So there is
/// no report here to accept one. The verb belongs with the protocol that will carry the rebinding,
/// and shaping it before that protocol exists would be guessing.
/// </para>
/// </remarks>
public interface ICharacterSettingsDomainService
{
    /// <summary>
    /// This character's hotkey slots, option block and visibility bits, or nothing when there is
    /// no character by that name here.
    /// </summary>
    /// <remarks>
    /// Nothing rather than an all-empty answer. A character who has never touched their keys has
    /// an empty list of slots, and one this server does not have has no settings at all; handing
    /// back the second as the first would describe somebody who is not there.
    /// <para>
    /// The three values come back in the record the entry projection already builds out of the
    /// same three, rather than in a second record of the same shape declared next to this. Two
    /// records saying the same thing drift, and the shape is not this seam's to invent -- it is
    /// what these rows are.
    /// </para>
    /// </remarks>
    ValueTask<CharacterSettingsEntryState?> FindAsync(
        CharacterId characterId, CancellationToken cancellationToken);
}
