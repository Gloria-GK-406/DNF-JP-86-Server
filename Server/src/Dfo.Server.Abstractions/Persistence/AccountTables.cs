using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Persistence;

/// <summary>What one row of <c>accounts</c> holds of a player's balances and progress.</summary>
public sealed record AccountWalletRow(
    long Cera,
    long TokenCera,
    long HappyTokenCera,
    long LuckyStar,
    long HonorExperience,
    long GrowthCapsuleExperience);

/// <summary>One row of <c>account_premiums</c>.</summary>
public sealed record AccountPremiumRow(int PremiumType, long ExpiresAtUnixSeconds);

/// <summary>One row of <c>account_increase_chance_lottery_progress</c>.</summary>
public sealed record AccountLotteryRow(int ItemTemplateId, int RewardIndex);

/// <summary>
/// The account itself.
/// </summary>
/// <remarks>
/// One table, and only the things one can do to a row of it: put one there if it is not already,
/// and read one back. Finding out whether an account exists and creating it when it does not is
/// two of those in a row, which is a decision -- so it is made by whoever is logging the player
/// in, not here.
/// </remarks>
public interface IAccountTable
{
    /// <summary>Changes only the three currencies, if they still equal the expected balances.</summary>
    ValueTask<int> TrySetCeraBalancesAsync(AccountId accountId, AccountCeraBalances expected,
        AccountCeraBalances next, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This account table cannot change CERA balances.");

    /// <summary>Adds the account unless that login name already has one.</summary>
    ValueTask InsertIfAbsentAsync(
        string loginId, string passwordHash, CancellationToken cancellationToken);

    /// <summary>The account with that login name, or nothing when there is none.</summary>
    ValueTask<AccountRecord?> FindByLoginIdAsync(string loginId, CancellationToken cancellationToken);

    /// <summary>What the account holds, or nothing when the account is not there.</summary>
    ValueTask<AccountWalletRow?> FindWalletAsync(
        AccountId accountId, CancellationToken cancellationToken);

    /// <summary>How many lucky stars the account has, or nothing when it is not there.</summary>
    ValueTask<int?> FindLuckyStarAsync(AccountId accountId, CancellationToken cancellationToken);

    /// <summary>The six cube fragment counts, or nothing when the account is not there.</summary>
    ValueTask<CubeFragmentRow?> FindCubeFragmentsAsync(
        AccountId accountId, CancellationToken cancellationToken);

    /// <summary>Sets how many lucky stars the account has. Says how many rows that touched.</summary>
    ValueTask<int> SetLuckyStarAsync(
        AccountId accountId, int luckyStar, CancellationToken cancellationToken);

    /// <summary>The two experience totals an account keeps, or nothing when it is not there.</summary>
    ValueTask<AccountExperienceRow?> FindExperienceAsync(
        AccountId accountId, CancellationToken cancellationToken);

    /// <summary>Sets both of the account's experience totals.</summary>
    ValueTask<int> SetExperienceAsync(
        AccountId accountId,
        long honourExperience,
        long growthCapsuleExperience,
        CancellationToken cancellationToken);
}

/// <summary>The two experience totals an account keeps.</summary>
public sealed record AccountExperienceRow(long HonourExperience, long GrowthCapsuleExperience);

/// <summary>
/// One dungeon effect waiting to be applied.
/// </summary>
/// <remarks>
/// The columns that identify it are four, not one: an effect is the same effect when the event,
/// the kind, the scope and the target all match. Every statement that acts on one names all four.
/// </remarks>
public sealed record DungeonEffectOutboxRow(
    string SourceEventId,
    string EffectKind,
    int EffectScope,
    long ScopeTarget,
    int AccountId,
    int PayloadVersion,
    string PayloadJson);

/// <summary>The <c>dungeon_persistent_effect_outbox</c> table.</summary>
public interface IDungeonEffectOutboxTable
{
    /// <summary>The character's effects that are waiting or whose lease has run out.</summary>
    ValueTask<IReadOnlyList<DungeonEffectOutboxRow>> ReadClaimableAsync(
        CharacterId characterId, long nowUnixMilliseconds, CancellationToken cancellationToken);

    /// <summary>
    /// Takes one effect for this recovery to work on. False when someone else got there first.
    /// </summary>
    ValueTask<bool> TryClaimAsync(
        DungeonEffectOutboxRow effect,
        string leaseId,
        long leaseExpiresAtUnixMilliseconds,
        long nowUnixMilliseconds,
        CancellationToken cancellationToken);

    /// <summary>Records what became of one effect this recovery had claimed.</summary>
    ValueTask SettleAsync(
        DungeonEffectOutboxRow effect,
        int state,
        string failureReason,
        string resultJson,
        string leaseId,
        CancellationToken cancellationToken);

    /// <summary>How many of the character's effects are still not done with.</summary>
    ValueTask<int> CountUnfinishedAsync(
        CharacterId characterId, CancellationToken cancellationToken);
}

/// <summary>The account's settings row.</summary>
public interface IAccountSettingsTable
{
    /// <summary>The settings, or nothing when the account has never saved any.</summary>
    ValueTask<AccountSettingsStorageRecord?> FindAsync(
        AccountId accountId, CancellationToken cancellationToken);
}

/// <summary>The account's premium entitlements.</summary>
public interface IAccountPremiumTable
{
    ValueTask<IReadOnlyList<AccountPremiumRow>> ReadAsync(
        AccountId accountId, CancellationToken cancellationToken);
}

/// <summary>The account's progress through the increased-chance lottery.</summary>
public interface IAccountLotteryProgressTable
{
    ValueTask<IReadOnlyList<AccountLotteryRow>> ReadAsync(
        AccountId accountId, CancellationToken cancellationToken);
}
