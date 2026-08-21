using System.Text;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Characters;

/// <summary>
/// An account's characters: listing them, finding one, and bringing one into being.
/// </summary>
/// <remarks>
/// Creating a character is five rows across four tables, and the order they go in is the whole of
/// what makes it a character rather than a row in <c>characters</c>: without its skills, its two
/// detail rows and its entry flags, it cannot be entered. That sequence is a decision about
/// characters, so it is made here; each table only knows how to hold its own row.
///
/// They settle together because the use case they run inside settles them, not because this
/// arranges it.
/// </remarks>
internal sealed class CharacterRosterService(
    ICharacterTable characters,
    ICharacterSkillTable skills,
    ICharacterSubtype0Table subtype0,
    ICharacterSubtype1Table subtype1,
    ICharacterInitFlagTable initFlags) : ICharacterRepository
{
    /// <summary>
    /// A new character enters at full health. Written explicitly because the column's schema
    /// default is zero, which the client renders as an empty bar under a full maximum.
    /// </summary>
    private const int FullHpMpPercent = 100;

    public async ValueTask<IReadOnlyList<CharacterSummary>> ListByAccountAsync(
        AccountId accountId, CancellationToken cancellationToken)
    {
        RequireAccount(accountId);
        var rows = await characters.ReadSummariesByAccountAsync(accountId, cancellationToken)
            .ConfigureAwait(false);
        return
        [
            .. rows.Select(row => new CharacterSummary(
                new CharacterId(row.CharacterId),
                checked((ushort)row.SlotIndex),
                row.Name,
                checked((byte)row.Job),
                checked((byte)row.GrowType),
                checked((byte)row.Level),
                checked((uint)row.CloneTitleItemId))),
        ];
    }

    public async ValueTask<ActiveCharacterState?> FindByAccountAndSlotAsync(
        AccountId accountId, ushort slotIndex, CancellationToken cancellationToken)
    {
        RequireAccount(accountId);
        var row = await characters.FindBySlotAsync(accountId, slotIndex, cancellationToken)
            .ConfigureAwait(false);
        return row is null
            ? null
            : new ActiveCharacterState(
                new CharacterIdentityState(
                    new CharacterId(row.CharacterId),
                    checked((ushort)row.SlotIndex),
                    row.Name,
                    checked((byte)row.Job),
                    checked((byte)row.GrowType)),
                new CharacterProgressionState(checked((byte)row.Level), 0),
                new CharacterFatigueState(188, 0),
                new CharacterPositionState(0, 0, 0, 0, 5),
                DateTimeOffset.UnixEpoch,
                DarkKnightCombo: null);
    }

    public ValueTask<bool> NameExistsAsync(byte[] name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);
        return characters.NameIsTakenAsync(name, cancellationToken);
    }

    public async ValueTask<CharacterId> CreateAsync(
        NewCharacterSpec spec, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (spec.AccountId.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(spec), "Account ID must be positive.");
        }

        // name and name_bytes are both written: name_bytes is the authority every read path uses,
        // while name stays populated because the column is NOT NULL and remains the only
        // human-readable handle when inspecting the database by hand.
        var characterId = await characters.InsertAsync(
            spec.AccountId, DecodeForDisplay(spec.Name), spec.Name, spec.Job, spec.SlotIndex,
            cancellationToken).ConfigureAwait(false);

        foreach (var skill in spec.InitialSkills ?? [])
        {
            await skills.InsertAsync(
                characterId, skill.Slot, (int)skill.SkillId, skill.Level, cancellationToken)
                .ConfigureAwait(false);
        }

        // The three rows without which the character cannot be entered.
        await subtype0.InsertIfAbsentAsync(characterId, cancellationToken).ConfigureAwait(false);
        await subtype1.InsertIfAbsentAsync(characterId, spec.Stats, FullHpMpPercent, cancellationToken)
            .ConfigureAwait(false);
        await initFlags.InsertIfAbsentAsync(characterId, cancellationToken).ConfigureAwait(false);
        return characterId;
    }

    public ValueTask<bool> SoftDeleteAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        characters.MarkDeletedAsync(characterId, cancellationToken);

    private static void RequireAccount(AccountId accountId)
    {
        if (accountId.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(accountId), "Account ID must be positive.");
        }
    }

    /// <summary>Best-effort text form for the NOT NULL <c>name</c> column.</summary>
    /// <remarks>
    /// Never used for matching. Undecodable bytes fall back to hex so the row stays inspectable
    /// instead of carrying replacement characters that look like real content.
    /// </remarks>
    private static string DecodeForDisplay(byte[] name)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(name);
        }
        catch (DecoderFallbackException)
        {
            return Convert.ToHexString(name);
        }
    }
}
