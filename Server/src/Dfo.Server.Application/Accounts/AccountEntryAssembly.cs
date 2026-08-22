using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Application.Accounts;

/// <summary>
/// Puts an account's eight tables together into the one record entering the game needs.
/// </summary>
/// <remarks>
/// This used to be a query -- three of the reads were joins, and the joins carried both the order
/// of the result and the checks that a relation pointed at a character of this account. Neither
/// was ever about how a row is stored, so neither belonged in the persistence implementation.
///
/// Here the tables are read separately and the account is assembled from them: the roster decides
/// what order anything per-character comes back in, and membership of the roster is what makes a
/// relation legitimate. That is the same answer the joins gave, said where it can be read.
///
/// The contract it implements is still called a repository. That name no longer describes it and
/// is left alone deliberately: renaming it is churn across callers that says nothing new, and the
/// dissonance is recorded rather than papered over.
/// </remarks>
internal sealed class AccountEntryAssembly(
    IAccountTable accounts,
    IAccountSettingsTable settings,
    IAccountPremiumTable premiums,
    IAccountLotteryProgressTable lotteryProgress,
    ICharacterTable roster,
    ICharacterConditionTable condition,
    ICharacterBuyRestrictTable buyRestrictions,
    ICharacterMercenarySupportTable mercenarySupport) : IAccountEntryStateRepository
{
    public async ValueTask<AccountEntryStorageRecord> LoadAsync(
        AccountId accountId, CancellationToken cancellationToken)
    {
        if (accountId.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(accountId), "Account ID must be positive.");
        }

        var wallet = await accounts.FindWalletAsync(accountId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException(
                $"Required account {accountId.Value} is unavailable.");
        var characters = await roster.ReadByAccountAsync(accountId, cancellationToken)
            .ConfigureAwait(false);
        if (characters.Count == 0)
        {
            throw new InvalidDataException(
                $"Account {accountId.Value} has no selectable roster state.");
        }

        var ids = characters.Select(character => character.CharacterId).ToArray();
        var slotOf = characters.ToDictionary(
            character => character.CharacterId, character => character.SlotIndex);

        return new AccountEntryStorageRecord(
            accountId,
            await settings.FindAsync(accountId, cancellationToken).ConfigureAwait(false)
                ?? new AccountSettingsStorageRecord(null, [], [], 0, []),
            new AccountWalletEntitlementStorageRecord(
                wallet.Cera, wallet.TokenCera, wallet.HappyTokenCera, wallet.LuckyStar,
                [.. (await premiums.ReadAsync(accountId, cancellationToken).ConfigureAwait(false))
                    .Select(premium => (premium.PremiumType, premium.ExpiresAtUnixSeconds))]),
            new AccountProgressionStorageRecord(
                wallet.HonorExperience,
                wallet.GrowthCapsuleExperience,
                [.. characters.Select(
                    character => (character.CharacterId, character.SlotIndex, character.Level))],
                [.. (await lotteryProgress.ReadAsync(accountId, cancellationToken)
                        .ConfigureAwait(false))
                    .Select(entry => (entry.ItemTemplateId, entry.RewardIndex))]),
            new AccountRelationCompatibilityStorageRecord(
                await RelationsAsync(ids, slotOf, cancellationToken).ConfigureAwait(false),
                await RestrictionsAsync(ids, slotOf, cancellationToken).ConfigureAwait(false),
                await PcRoomStatesAsync(characters, cancellationToken).ConfigureAwait(false)));
    }

    public async ValueTask<AccountSettingsStorageRecord> LoadSettingsAsync(
        AccountId accountId, CancellationToken cancellationToken)
    {
        if (accountId.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(accountId), "Account ID must be positive.");
        }

        return await settings.FindAsync(accountId, cancellationToken).ConfigureAwait(false)
            ?? new AccountSettingsStorageRecord(null, [], [], 0, []);
    }

    /// <remarks>
    /// A supported character has to be one of this account's, still there. The join said so by
    /// reaching for the other row; the roster says so by containing it, which is the same
    /// statement about the same rows.
    /// </remarks>
    private async ValueTask<IReadOnlyList<(int, int, int, int, int)>> RelationsAsync(
        IReadOnlyList<int> ids,
        IReadOnlyDictionary<int, int> slotOf,
        CancellationToken cancellationToken)
    {
        var rows = await mercenarySupport.ReadByOwnersAsync(ids, cancellationToken)
            .ConfigureAwait(false);
        foreach (var row in rows)
        {
            if (!slotOf.ContainsKey(row.SupportCharacterId))
            {
                throw new InvalidDataException(
                    "Account relation references a missing, deleted or foreign character.");
            }
        }

        return
        [
            .. rows
                .OrderBy(row => slotOf[row.OwnerCharacterId])
                .ThenBy(row => row.Slot)
                .Select(row => (row.OwnerCharacterId, row.Slot, row.SupportCharacterId,
                    row.SkillId, row.StrikerSkillId)),
        ];
    }

    private async ValueTask<IReadOnlyList<(int, int, int, int, int)>> RestrictionsAsync(
        IReadOnlyList<int> ids,
        IReadOnlyDictionary<int, int> slotOf,
        CancellationToken cancellationToken)
    {
        var rows = await buyRestrictions.ReadByCharactersAsync(ids, cancellationToken)
            .ConfigureAwait(false);
        return
        [
            .. rows
                .OrderBy(row => slotOf[row.CharacterId])
                .ThenBy(row => row.SortOrder)
                .Select(row => (slotOf[row.CharacterId], row.SortOrder, row.EntryId,
                    row.SentinelOrValue, row.Flag)),
        ];
    }

    /// <remarks>
    /// One per character, in roster order, and every one of them the same. The two conditions were
    /// checked together before and still are, because either one failing means the same thing:
    /// this account cannot be told apart on whether it is in a PC room.
    /// </remarks>
    private async ValueTask<IReadOnlyList<int>> PcRoomStatesAsync(
        IReadOnlyList<CharacterRosterRow> characters, CancellationToken cancellationToken)
    {
        var rows = await condition.ReadPcRoomStatesAsync(
                [.. characters.Select(character => character.CharacterId)], cancellationToken)
            .ConfigureAwait(false);
        var stateOf = rows.ToDictionary(row => row.CharacterId, row => row.PcRoomState);
        var states = new List<int>(characters.Count);
        foreach (var character in characters)
        {
            if (!stateOf.TryGetValue(character.CharacterId, out var state))
            {
                throw new InvalidDataException("PC-room compatibility state is unavailable.");
            }

            states.Add(state);
        }

        return states.Distinct().Count() == 1
            ? states
            : throw new InvalidDataException(
                "Account PC-room compatibility state is missing or inconsistent.");
    }
}
