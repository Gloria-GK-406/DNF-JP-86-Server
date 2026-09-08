using System.Buffers.Binary;
using System.Text;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
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
    ICharacterItemTable items,
    IItemPlacementRuleService placementRules) : ICharacterRepository
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

    /// <summary>The account's characters, with nothing worked out about how they look.</summary>
    /// <remarks>
    /// One read, and every summary carries a null appearance. This is what the four call sites that
    /// draw nothing get: resolving a roster position, counting slots before a creation, finding a
    /// character by name before a deletion, and the entry burst's check that the selected character
    /// is on the roster.
    /// <para>
    /// <b>It is deliberately cheap, and that is a correctness property rather than a performance
    /// one.</b> Working out an appearance means reading everything every character on the account
    /// wears, and that read can fail for its own reasons -- so folding it in here would let a
    /// moment's trouble reading equipment stop a player entering the world, deleting a character,
    /// or getting as far as the write a creation is asking for.
    /// See <see cref="ListByAccountWithAppearanceAsync"/>.
    /// </para>
    /// <para>
    /// <b>It does not spare a creation altogether</b>, and saying it did would be false: after the
    /// write succeeds, <c>CreateCharacterSlice</c> pushes a roster the client draws, and that one
    /// takes the other listing. The split decides which half of a request pays, not which request.
    /// </para>
    /// </remarks>
    public async ValueTask<IReadOnlyList<CharacterSummary>> ListByAccountAsync(
        AccountId accountId, CancellationToken cancellationToken)
    {
        RequireAccount(accountId);
        var rows = await characters.ReadSummariesByAccountAsync(accountId, cancellationToken)
            .ConfigureAwait(false);
        return [.. rows.Select(row => Summary(row, appearances: null))];
    }

    /// <summary>The account's characters, each drawn wearing what it is wearing.</summary>
    /// <remarks>
    /// Two reads, and the second is the whole of what makes the character-selection screen right.
    /// The appearance used to be decoded out of <c>characters.appearance_blob</c>, which is written
    /// once when a character is created and by nothing afterwards -- so a player who dressed a
    /// character and came back to that screen was drawn the character they made, not the character
    /// they have. Observed live on 2026-09-08.
    /// <para>
    /// What is drawn is asked of <see cref="DrawnAppearance"/>, which is the same answer the
    /// in-world frame writes. The blob is neither written nor removed by this: it stays exactly as
    /// it was, and the entry burst's own roster frame still reads it
    /// (<c>SelectCharacterPreparationService.BuildRosterEntryAsync</c>, through
    /// <c>CharacterEntryStateTables</c>). <b>The two screens therefore disagree</b> until something
    /// decides about the second one; that is recorded rather than left to be discovered.
    /// </para>
    /// <para>
    /// A character wearing nothing has no rows in the second read and is drawn wearing nothing,
    /// which is what an account's freshly made character is -- an empty list, not a null one.
    /// </para>
    /// </remarks>
    public async ValueTask<IReadOnlyList<CharacterSummary>> ListByAccountWithAppearanceAsync(
        AccountId accountId, CancellationToken cancellationToken)
    {
        RequireAccount(accountId);
        var rows = await characters.ReadSummariesByAccountAsync(accountId, cancellationToken)
            .ConfigureAwait(false);
        var worn = await items.ReadWornByAccountAsync(accountId, cancellationToken)
            .ConfigureAwait(false);
        var byCharacter = worn
            .GroupBy(piece => piece.CharacterId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<InventoryItemState>)[.. group.Select(Worn)]);
        return
        [
            .. rows.Select(row => Summary(
                row,
                DrawnAppearance.Of(
                    byCharacter.TryGetValue(row.CharacterId, out var pieces) ? pieces : []))),
        ];
    }

    /// <summary>One row as the listing names it, however much was worked out about it.</summary>
    private static CharacterSummary Summary(
        CharacterSummaryRow row,
        IReadOnlyList<CharacterRosterAppearanceState>? appearances) =>
        new(new CharacterId(row.CharacterId),
            checked((ushort)row.SlotIndex),
            row.Name,
            checked((byte)row.Job),
            checked((byte)row.GrowType),
            checked((byte)row.Level),
            checked((uint)row.CloneTitleItemId),
            appearances);

    /// <summary>One worn row as the piece the drawn appearance is worked out from.</summary>
    /// <remarks>
    /// The identity is the row of <c>items</c> the position names, which for a worn slot is the
    /// piece itself -- the table holds a world slot to naming its own row. Nothing the roster draws
    /// reads it; it is carried because a piece with an identity is what this is.
    /// </remarks>
    private static InventoryItemState Worn(WornItemRow piece) =>
        new(new ItemIdentity(checked((int)piece.ItemUid)), piece.SlotIndex, piece.Core);

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
                DateTimeOffset.FromUnixTimeSeconds(row.CreatedAtUnixSeconds));
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
            // Classified before it is written, out of the one place that classifies. A worn slot
            // holds one thing rather than a pile, so a starting piece the archive declares
            // interchangeable is a mistake in the creation table rather than a character who
            // begins with a stack of potions welded to their shoulder.
            var worldClass = await placementRules
                .ClassifyAsync(piece.ItemId, cancellationToken).ConfigureAwait(false);
            if (worldClass == ItemWorldClass.Interchangeable)
            {
                throw new InvalidDataException(
                    $"Starting piece {piece.ItemId} is not an individual piece the archive "
                    + "declares wearable, so it cannot be worn in slot "
                    + $"{piece.SlotIndex}.");
            }

            // Two rows, because a thing and where it is are two facts. The piece goes into the
            // world first and is given an identity there; the equipment slot then says it holds
            // that identity. Writing only the first would leave a piece nothing anywhere points
            // at -- a character created unarmed, and a row the orphan read would rightly report.
            var identity = await items
                .InsertOwnAsync(EquipmentCore(piece), null, cancellationToken)
                .ConfigureAwait(false);
            if (identity is not { } named)
            {
                throw new InvalidDataException(
                    $"Starting piece {piece.ItemId} could not be put into the world.");
            }

            var worn = checked((int)named);
            if (await items.HoldOwnAsync(
                    characterId,
                    (int)InventoryContainerType.Equipment,
                    piece.SlotIndex,
                    ItemSlotKind.World,
                    worn,
                    worn,
                    1,
                    cancellationToken).ConfigureAwait(false) != 1)
            {
                throw new InvalidDataException(
                    $"Two starting pieces claim equipment slot {piece.SlotIndex}.");
            }
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
    /// newly created pieces hold: the kind the creation rules give every worn item, the
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
        new(piece.ItemKind, piece.ItemId, Random.Shared.Next(1, int.MaxValue), 0,
            piece.Durability, 0, 0, 0, 0, 0, UntouchedMarker,
            EmptyChronicle, EmptyChronicle, 0, 0, 0, 0, 0,
            EmptyRandomOption, EmptyRandomOption, EmptyRandomOption, 0, UntouchedRandomOptionIndex,
            0, EmptyRandomOption, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

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
