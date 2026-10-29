using Dfo.Server.Abstractions.Transactions;
using System.Text;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Application.Inventory;
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
    IItemPlacementRuleService placementRules, ICharacterFatigueDomainService fatigue,
    IItemGrantDomainService grants, ICharacterAppearanceDomainService? appearances = null) : ICharacterRepository
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

    private const int InitialReviveCoins = 10;
    private const int ReviveCoinSlot = 1;
    private const int ReviveCoinItemId = 1;

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
    /// Two reads, and the second is the whole of what makes the character-selection screen right:
    /// a character is drawn in what it wears now, so a player who dressed a character and came back
    /// to that screen sees the character they have, not the one they made.
    /// <para>
    /// What is drawn is asked of <see cref="DrawnAppearance"/>, which is the same answer the
    /// in-world frame writes. <b>The entry burst's roster frame asks this listing too</b>, so the
    /// two screens agree: <c>SelectCharacterPreparationService.BuildRosterEntryAsync</c> takes the
    /// appearance off these summaries.
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
        var result = new List<CharacterSummary>(rows.Count);
        foreach (var row in rows)
        {
            var pieces = byCharacter.GetValueOrDefault(row.CharacterId) ?? [];
            var display = appearances is null
                ? CharacterAppearanceRuleService.Shared.Resolve(new(CharacterCareer.FromPacked(checked((byte)row.Job), row.GrowType)), pieces, new(null, 0))
                : await appearances.ResolveAsync(accountId, new(row.CharacterId), pieces, cancellationToken).ConfigureAwait(false);
            result.Add(Summary(row, display.Parts) with
            { CloneTitleItemId = display.TitleAppearanceItemId, Display = display });
        }
        return result;
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
        if (row is null) return null;
        var balance = await fatigue.BalanceOfAsync(new CharacterId(row.CharacterId), cancellationToken).ConfigureAwait(false);
        return new ActiveCharacterState(
                new CharacterIdentityState(
                    new CharacterId(row.CharacterId),
                    checked((ushort)row.SlotIndex),
                    row.Name,
                    checked((byte)row.Job),
                    checked((byte)row.GrowType)),
                new CharacterProgressionState(checked((byte)row.Level), 0),
                new CharacterFatigueState(balance.Total, balance.Used) { ConsumedToday = balance.ConsumedToday },
                row.Position,
                // The client reads this as the character's creation time and the entry ACK carries
                // it; a working A21 session sends a real Unix timestamp here, so a fixed epoch is a
                // zero on the wire rather than an unset field.
                DateTimeOffset.FromUnixTimeSeconds(row.CreatedAtUnixSeconds))
        {
            TutorialCompleted = row.TutorialCompleted,
            LastInnTownId = row.LastInnTownId,
        };
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
            cancellationToken).ConfigureAwait(false);

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

        foreach (var piece in equipment)
        {
            if (await placementRules.ClassifyAsync(piece.ItemId, cancellationToken).ConfigureAwait(false) != ItemWorldClass.Equipment)
                throw new InvalidDataException($"Starting piece {piece.ItemId} is not wearable equipment.");
            var snapshot = new ItemSnapshot { ItemId = piece.ItemId, LegacyKind = piece.ItemKind,
                Type = ItemTraitClassification.For(piece.ItemKind, piece.ItemId), IsPile = false,
                InstanceValue = Dfo.Server.Application.Inventory.EquipmentQualitySeeds.NewRandom(), Durability = piece.Durability };
            await grants.PutNewAsync(ContainerId.ForCharacter(characterId, InventoryContainerType.Equipment),
                piece.SlotIndex, snapshot, cancellationToken).ConfigureAwait(false);
        }
        await grants.PutNewAsync(ContainerId.ForCharacter(characterId, InventoryContainerType.Main), ReviveCoinSlot,
            new ItemSnapshot { ItemId = ReviveCoinItemId, LegacyKind = 0, Type = ItemTraits.None,
                IsPile = true, Count = InitialReviveCoins }, cancellationToken).ConfigureAwait(false);

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
