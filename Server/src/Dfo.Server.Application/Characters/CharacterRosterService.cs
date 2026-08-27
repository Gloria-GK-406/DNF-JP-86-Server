using System.Buffers.Binary;
using System.Text;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Characters;

/// <summary>
/// An account's characters: listing them, finding one, and bringing one into being.
/// </summary>
/// <remarks>
/// Creating a character is six rows across five tables, and the order they go in is the whole of
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
    ICharacterConditionTable condition,
    ICharacterStatsTable stats,
    ICharacterPvpTable pvp,
    ICharacterSettingsTable settings,
    ICharacterExpertJobTable expertJob,
    ICharacterContainerStateTable containerState,
    ICharacterItemTable items) : ICharacterRepository
{
    /// <summary>
    /// A new character enters at full health. Written explicitly because the column's schema
    /// default is zero, which the client renders as an empty bar under a full maximum.
    /// </summary>
    private const int FullHpMpPercent = 100;

    /// <summary>How many slots a new character's main bag has.</summary>
    /// <remarks>
    /// The main container's snapshot carries this number, and the reference session's freshly made
    /// character carries 24. Nothing wrote a <c>character_container_state</c> row at creation, so
    /// the read fell through to the column default and the client was told the bag holds nothing.
    /// </remarks>
    private const int InitialMainContainerSlots = 24;

    /// <summary>The appearance a character is created wearing.</summary>
    /// <remarks>
    /// Three parts -- <c>count(1)</c> then <c>slot(1), displayItem(i32), expansionLength(i32=4),
    /// expansion(4), state(1), linkItem(i32), enchant(u32), flag(1)</c> per part -- shaped from a
    /// working A21 session's character.
    ///
    /// What it draws is the equipment the character is created wearing, so it is built from the
    /// same grants rather than held as a constant. It was a constant until the archive was read
    /// properly: an earlier note here recorded that no per-job creation set could be found and
    /// that starting gear must therefore be server data. That was wrong. Every <c>.chr</c> carries
    /// <c>[create equipment list]</c>, and the constant that stood in for it was transcribed from
    /// a session whose character was a different job, so every character made here was drawn
    /// holding that job's weapon.
    ///
    /// An earlier note also recorded a belief that an empty blob is what makes the A21 client fail
    /// <c>InitGraphicSystem</c> and terminate itself while building the town. That belief did not
    /// survive retesting: with everything else as it is now, a character whose blob is NULL enters
    /// the world just as well. What actually triggers that failure is unresolved; see
    /// protocol/patterns/world-entry.md.
    /// </remarks>
    private static byte[] Appearance(IReadOnlyList<NewCharacterEquipment> equipment)
    {
        var drawn = equipment.Where(piece => piece.SlotIndex <= LastAppearanceSlot).ToArray();
        var blob = new byte[1 + (AppearancePartLength * drawn.Length)];
        blob[0] = checked((byte)drawn.Length);
        for (var index = 0; index < drawn.Length; index++)
        {
            var part = blob.AsSpan(1 + (index * AppearancePartLength));
            part[0] = drawn[index].SlotIndex;
            BinaryPrimitives.WriteInt32LittleEndian(part[1..], drawn[index].ItemId);
            BinaryPrimitives.WriteInt32LittleEndian(part[5..], AppearanceExpansionLength);
        }

        return blob;
    }

    /// <summary>The last equipment slot the roster appearance draws.</summary>
    /// <remarks>
    /// Zero through thirteen: the twelve costume-bar slots, the weapon and the title. Armour is
    /// worn but not drawn here -- a working A21 session's freshly made character carries three
    /// pieces and sends one appearance part, the weapon. Sending the armour too makes the frame
    /// forty-six bytes longer than that session's.
    /// </remarks>
    private const byte LastAppearanceSlot = 13;

    /// <summary>One part of the appearance blob, in bytes.</summary>
    private const int AppearancePartLength = 23;

    /// <summary>
    /// The length each part declares for its own expansion field, which is four zero bytes.
    /// </summary>
    private const int AppearanceExpansionLength = 4;

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
                checked((uint)row.CloneTitleItemId),
                // The stored blob is already a count followed by wire-shaped entries, so this
                // decode is a re-read rather than a translation. It is the entry path's decoder
                // because it is the same blob; that the two paths reach for it from different
                // services is noted in Docs/ProtocolScaffoldDissonances.md.
                //
                // This adds a failure surface the roster did not have: a truncated or over-long
                // blob now fails the whole listing rather than one character's parts. That is the
                // right way round -- a roster that silently drops a character's appearance looks
                // like a content bug for a long time -- but it is new, and it is why the decode
                // stays strict instead of skipping what it cannot read.
                SelectCharacterPreparationService.DecodeAppearances(row.AppearanceBlob))),
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
                // The client reads this as the character's creation time and the entry ACK carries
                // it; a working A21 session sends a real Unix timestamp here, so a fixed epoch is a
                // zero on the wire rather than an unset field.
                DateTimeOffset.FromUnixTimeSeconds(row.CreatedAtUnixSeconds),
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
        var equipment = spec.InitialEquipment ?? [];
        var characterId = await characters.InsertAsync(
            spec.AccountId, DecodeForDisplay(spec.Name), spec.Name, spec.Job, spec.SlotIndex,
            Appearance(equipment), cancellationToken).ConfigureAwait(false);

        // Both pages, and the second is a copy of the first. A character owns two skill trees from
        // the moment it exists; the second stays locked until it is bought, and while it is locked
        // a working A21 session sends it back holding exactly what the first holds.
        foreach (var skill in spec.InitialSkills ?? [])
        {
            for (var page = 0; page < SkillPageCount; page++)
            {
                await skills.InsertAsync(
                    characterId, page, skill.Slot, (int)skill.SkillId, skill.Level,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        // Worn, not carried. The pieces the archive names are the ones the character fights with,
        // and a character holding them in its bag reaches the world unarmed: the entry frame
        // reports what is in the equipment container, so an empty one is a character that cannot
        // attack and whose weapon-bound skills the client draws greyed.
        foreach (var piece in equipment)
        {
            await items.InsertOwnAsync(
                characterId, (int)InventoryContainerType.Equipment, piece.SlotIndex,
                EquipmentCore(piece), null, cancellationToken).ConfigureAwait(false);
        }

        // The rows without which the character cannot be entered. Each of the six holds one
        // row per character for as long as the character exists, so this is where they begin.
        await condition.InsertIfAbsentAsync(characterId, cancellationToken).ConfigureAwait(false);
        await stats.InsertIfAbsentAsync(characterId, spec.Stats, FullHpMpPercent, cancellationToken)
            .ConfigureAwait(false);
        await pvp.InsertIfAbsentAsync(characterId, cancellationToken).ConfigureAwait(false);
        await settings.InsertIfAbsentAsync(characterId, cancellationToken).ConfigureAwait(false);
        await expertJob.InsertIfAbsentAsync(characterId, cancellationToken).ConfigureAwait(false);
        await containerState.InsertIfAbsentAsync(
            characterId, (int)InventoryContainerType.Main, InitialMainContainerSlots,
            cancellationToken).ConfigureAwait(false);
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

    /// <summary>A freshly made piece of equipment, as the item core records one.</summary>
    /// <remarks>
    /// Four fields carry something and the rest are zero, which is what a working A21 session's
    /// newly created pieces hold: the kind the equipment index gives every worn item, the
    /// identifier, the durability its own definition declares, and an instance value that
    /// distinguishes one piece from another. <c>Marker16</c> is -1 and
    /// <c>RandomOptionChangedIndex</c> is 255 because that is what an untouched core reads as
    /// there -- both are "nothing here" rather than zeroes that mean something.
    ///
    /// The instance value is drawn rather than sequenced. Nothing in this repository reads it yet;
    /// what it must be is distinct, and a sequence would need a row of its own to stay distinct
    /// across restarts.
    /// </remarks>
    private static InventoryItemCoreState EquipmentCore(NewCharacterEquipment piece) =>
        new(EquipmentItemKind, piece.ItemId, Random.Shared.Next(1, int.MaxValue), 0,
            piece.Durability, 0, 0, 0, 0, 0, UntouchedMarker,
            EmptyChronicle, EmptyChronicle, 0, 0, 0, 0, 0,
            EmptyRandomOption, EmptyRandomOption, EmptyRandomOption, 0, UntouchedRandomOptionIndex,
            0, EmptyRandomOption, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>What the archive's equipment index says every worn item is.</summary>
    private const byte EquipmentItemKind = 1;

    private const int UntouchedMarker = -1;

    private const byte UntouchedRandomOptionIndex = 255;

    private static readonly ChronicleOptionState EmptyChronicle = new(0, 0, 0, 0, 0);

    private static readonly RandomOptionState EmptyRandomOption = new(0, 0, 0);

    /// <summary>How many skill trees a character owns.</summary>
    private const int SkillPageCount = 2;

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
