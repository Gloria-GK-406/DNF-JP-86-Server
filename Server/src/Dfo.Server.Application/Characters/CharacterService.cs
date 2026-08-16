using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Characters;

internal sealed class CharacterService(Func<ICharacterRepository> characterRepository)
    : ICharacterService, IEquipmentEligibilityCharacterReader
{
    private const ushort DefaultSlotLimit = 32;

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

        // The roster is ordered by slot, and the client addresses a character by its slot index in
        // that order. Reusing a freed slot would silently renumber every character after it, so the
        // new one always goes past the highest slot currently in use.
        var slotIndex = existing.Count == 0
            ? (ushort)0
            : checked((ushort)(existing.Max(character => character.SlotIndex) + 1));

        var characterId = await repository.CreateAsync(
            new NewCharacterSpec(accountId, rawName, job, slotIndex),
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
        var target = existing.FirstOrDefault(character => character.SlotIndex == slotIndex);
        if (target is null)
        {
            return new CharacterDeletionResult(CharacterDeletionStatus.SlotEmpty, null);
        }

        // The client echoes the name back as confirmation. Requiring it to match is what stops a
        // stale roster on the client from deleting whoever now occupies that slot.
        if (!target.Name.AsSpan().SequenceEqual(rawName))
        {
            return new CharacterDeletionResult(CharacterDeletionStatus.NameMismatch, null);
        }

        return await repository.SoftDeleteAsync(target.CharacterId, cancellationToken)
            .ConfigureAwait(false)
            ? new CharacterDeletionResult(CharacterDeletionStatus.Deleted, target.CharacterId)
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
