using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Characters;

internal sealed class CharacterDomainService(
    Func<ICharacterRepository> characterRepository,
    ICharacterStatDefinitionSource characterStats,
    IInitialSkillDefinitionSource initialSkills,
    IInitialEquipmentDefinitionSource initialEquipment,
    Func<CharacterId, ICharacter> playingCharacter)
    : ICharacterDomainService, IEquipmentEligibilityCharacterReader
{
    /// <remarks>
    /// Built rather than looked up. A character holds no state, so there is nothing a registry
    /// could keep for it and nothing for its absence to mean; what is made per call is one small
    /// object over services held elsewhere.
    ///
    /// Reached through a factory for the same reason the repository is: what a character needs in
    /// order to answer questions -- the archive, the cooldowns, the session directories -- is a
    /// great deal more than what a roster or a creation needs, and asking for all of it up front
    /// would make every caller of this service depend on content it never reads.
    /// </remarks>
    public ICharacter Get(CharacterId characterId) => playingCharacter(characterId);

    private const ushort DefaultSlotLimit = 32;

    /// <summary>The first slot of the skill band the client's skill bar does not draw from.</summary>
    private const short FirstPassiveSlot = 102;

    // Bounds and job ceiling carried over from the legacy server, which is the only evidence we
    // have for them. The two byte bounds genuinely differ per protocol -- see CharacterNameRules.
    private const int NameCheckMinBytes = 2;
    private const int NameCheckMaxBytes = 30;
    private const int CreateMinBytes = 2;
    private const int CreateMaxBytes = 18;
    private const byte MaxJobId = 12;

    public async ValueTask<AccountCharacterRoster> GetAccountRosterAsync(
        AccountId accountId,
        CancellationToken cancellationToken)
    {
        var characters = await characterRepository().ListByAccountAsync(
            accountId,
            cancellationToken).ConfigureAwait(false);
        return new AccountCharacterRoster(accountId, DefaultSlotLimit, characters);
    }

    public async ValueTask<ActiveCharacterState> LoadForSelectionAsync(
        AccountId accountId,
        ushort slotIndex,
        CancellationToken cancellationToken)
    {
        return await characterRepository().FindByAccountAndSlotAsync(
            accountId,
            slotIndex,
            cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException(
                $"Account '{accountId}' has no character in slot '{slotIndex}'.");
    }

    /// <summary>
    /// Resolves a roster position to a character. The client indexes its own character vector,
    /// which it fills from the roster notification in the order that notification listed them, so
    /// position N is the N-th entry of <see cref="ICharacterRepository.ListByAccountAsync"/> -- the
    /// same ordering the roster encoder walks.
    /// </summary>
    /// <remarks>
    /// Position is not the slot. They agree only while the roster has no gaps, which is why this
    /// went unnoticed: every earlier sample happened to be gap-free. The 2026-08-16 capture settles
    /// it -- the roster carried slots 0/2/3, the player picked the second entry, and the client
    /// sent 1, a slot that holds no live character. See
    /// protocol/game/client-to-server/0x0004.md.
    /// </remarks>
    public async ValueTask<ActiveCharacterState> LoadByRosterPositionAsync(
        AccountId accountId,
        ushort rosterIndex,
        CancellationToken cancellationToken)
    {
        var roster = await characterRepository().ListByAccountAsync(accountId, cancellationToken)
            .ConfigureAwait(false);
        if (rosterIndex >= roster.Count)
        {
            throw new KeyNotFoundException(
                $"Account '{accountId}' has no character at roster position '{rosterIndex}'.");
        }

        return await LoadForSelectionAsync(
            accountId,
            roster[rosterIndex].SlotIndex,
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<CharacterNameStatus> CheckNameAsync(
        byte[] rawName,
        CancellationToken cancellationToken)
    {
        var shape = CharacterNameRules.Inspect(rawName, NameCheckMinBytes, NameCheckMaxBytes);
        if (shape != CharacterNameShape.WellFormed)
        {
            return ToNameStatus(shape);
        }

        return await characterRepository().NameExistsAsync(rawName, cancellationToken)
            .ConfigureAwait(false)
            ? CharacterNameStatus.AlreadyTaken
            : CharacterNameStatus.Available;
    }

    public async ValueTask<CharacterCreationResult> CreateCharacterAsync(
        AccountId accountId,
        byte job,
        byte[] rawName,
        CancellationToken cancellationToken)
    {
        if (job > MaxJobId)
        {
            return new CharacterCreationResult(CharacterCreationStatus.MalformedRequest, null);
        }

        if (CharacterNameRules.Inspect(rawName, CreateMinBytes, CreateMaxBytes)
            != CharacterNameShape.WellFormed)
        {
            return new CharacterCreationResult(CharacterCreationStatus.InvalidName, null);
        }

        var repository = characterRepository();
        var existing = await repository.ListByAccountAsync(accountId, cancellationToken)
            .ConfigureAwait(false);
        if (existing.Count >= DefaultSlotLimit)
        {
            return new CharacterCreationResult(CharacterCreationStatus.NoFreeSlot, null);
        }

        if (await repository.NameExistsAsync(rawName, cancellationToken).ConfigureAwait(false))
        {
            return new CharacterCreationResult(CharacterCreationStatus.NameTaken, null);
        }

        // The lowest free slot, so the roster stays packed: slot i is always the i-th entry and the
        // highest slot is always Count - 1.
        //
        // This used to be "one past the highest slot in use", to avoid renumbering characters after
        // a reused slot. That reasoning does not hold -- the client rebuilds its list from the
        // roster it is sent and keeps no numbering across refreshes, so a deletion renumbers the
        // entries after it no matter which slot the next character takes. What the old policy did
        // do was let slots drift above the count and, after enough delete/create cycles, above the
        // slot limit this same method advertises: the guard below counts characters, while the
        // slot it handed out counted deletions too.
        var taken = existing.Select(character => character.SlotIndex).ToHashSet();
        ushort slotIndex = 0;
        while (taken.Contains(slotIndex))
        {
            slotIndex++;
        }

        // Resolved here rather than inside the repository: what a character is born with is a rule
        // about characters, and the repository's job is to store what it is handed. It is resolved
        // before the insert because the two have to land in one transaction -- a character row
        // without its stats is the half-built state the entry profile query rejects outright.
        var stats = await characterStats.GetInitialAsync(job, cancellationToken).ConfigureAwait(false);

        // Two bands, because the skill bar reads only the first. Everything written to the low
        // slots reaches the bar in slot order, so granting the passives alongside the actives put
        // 基础精通 and 基础防具精通 on F and G, where a player cannot use them and would not expect
        // them; ordering the actives first was not enough, because the passives still landed in
        // slots after them. Dropping the passives instead answered that, and cost the character
        // the passives themselves.
        //
        // The band the bar does not read is 102 upwards. That is not a choice made here: a working
        // A21 session places a job's three initial passives at 102, 103 and 104 while its three
        // actives sit at 0, 1 and 2, and this repository's own PvP profile already builds its
        // slots the same way. Within each band the archive's order is kept; it is the only
        // ordering available.
        var grants = await initialSkills.GetAsync(job, cancellationToken).ConfigureAwait(false);
        var skills = new List<NewCharacterSkill>();
        short activeSlot = 0;
        short passiveSlot = FirstPassiveSlot;
        foreach (var grant in grants)
        {
            var slot = grant.IsActive ? activeSlot++ : passiveSlot++;
            skills.Add(new NewCharacterSkill(slot, grant.SkillId, grant.Level));
        }

        var equipment =
            (await initialEquipment.GetAsync(job, cancellationToken).ConfigureAwait(false))
            .Select(grant => new NewCharacterEquipment(
                grant.SlotIndex, grant.ItemId, grant.Durability))
            .ToArray();

        var characterId = await repository.CreateAsync(
            new NewCharacterSpec(accountId, rawName, job, slotIndex, stats, skills, equipment),
            cancellationToken).ConfigureAwait(false);
        return new CharacterCreationResult(CharacterCreationStatus.Created, characterId);
    }

    public async ValueTask<CharacterDeletionResult> DeleteCharacterAsync(
        AccountId accountId,
        ushort slotIndex,
        byte[] rawName,
        CancellationToken cancellationToken)
    {
        if (rawName is null || rawName.Length == 0)
        {
            return new CharacterDeletionResult(CharacterDeletionStatus.MalformedRequest, null);
        }

        var repository = characterRepository();
        var existing = await repository.ListByAccountAsync(accountId, cancellationToken)
            .ConfigureAwait(false);

        // The name decides which character goes, not the index. The index the request carries is a
        // position in the client's own character vector, and whether that equals the slot depends
        // on how the client sizes the vector -- unsettled, and indistinguishable while the roster
        // has no gaps, which every observed sample has been. The name has none of that ambiguity:
        // the player typed it, the client itself compares it against the entry before sending, and
        // it is unique among an account's live characters because NameExistsAsync only counts rows
        // with delete_flag = 0.
        var target = existing.FirstOrDefault(
            character => character.Name.AsSpan().SequenceEqual(rawName));
        if (target is null)
        {
            // Nothing on this account answers to that name. From the player's side this is the same
            // situation the confirmation field exists to catch, so it keeps that reason code.
            return existing.Any(character => character.SlotIndex == slotIndex)
                ? new CharacterDeletionResult(CharacterDeletionStatus.NameMismatch, null)
                : new CharacterDeletionResult(CharacterDeletionStatus.SlotEmpty, null);
        }

        return await repository.SoftDeleteAsync(target.CharacterId, cancellationToken)
            .ConfigureAwait(false)
            ? new CharacterDeletionResult(
                CharacterDeletionStatus.Deleted, target.CharacterId, target.SlotIndex)
            : new CharacterDeletionResult(CharacterDeletionStatus.SlotEmpty, null);
    }

    private static CharacterNameStatus ToNameStatus(CharacterNameShape shape) => shape switch
    {
        CharacterNameShape.TooShort => CharacterNameStatus.TooShort,
        CharacterNameShape.TooLong => CharacterNameStatus.TooLong,
        _ => CharacterNameStatus.Malformed,
    };

    public async ValueTask<EquipmentEligibilityCharacterFacts> GetAsync(
        AccountId accountId,
        CharacterId characterId,
        CancellationToken cancellationToken)
    {
        var roster = await GetAccountRosterAsync(accountId, cancellationToken).ConfigureAwait(false);
        var character = roster.Characters.FirstOrDefault(item => item.CharacterId == characterId)
            ?? throw new KeyNotFoundException(
                $"Account '{accountId}' has no character '{characterId}'.");

        return new EquipmentEligibilityCharacterFacts(
            character.CharacterId,
            character.Level,
            character.Job,
            character.GrowType);
    }
}
