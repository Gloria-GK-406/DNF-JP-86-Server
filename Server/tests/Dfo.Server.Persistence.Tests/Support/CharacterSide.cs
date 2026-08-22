using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Accounts;
using Dfo.Server.Persistence.Characters;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;

namespace Dfo.Server.Persistence.Tests.Support;

/// <summary>
/// The character side of one database, put together the way the server puts it together.
/// </summary>
/// <remarks>
/// Anything that writes has to be inside a use case, which in the server is a command and here is
/// whatever the test says it is. <see cref="InUseCaseAsync{T}"/> is that: it opens one, runs the
/// work, and settles it.
/// </remarks>
internal sealed class CharacterSide
{
    private readonly SqliteDatabase database;

    public CharacterSide(string databasePath)
    {
        this.Scope = new DomainTransactionScope();
        this.database = new SqliteDatabase(databasePath, this.Scope);
    }

    public DomainTransactionScope Scope { get; }

    public ICharacterRepository Roster => new CharacterRosterService(
        new SqliteCharacterTable(this.database),
        new SqliteCharacterSkillTable(this.database),
        new SqliteCharacterConditionTable(this.database),
        new SqliteCharacterStatsTable(this.database),
        new SqliteCharacterPvpTable(this.database),
        new SqliteCharacterSettingsTable(this.database),
        new SqliteCharacterExpertJobTable(this.database));

    /// <summary>The skill table itself, for the tests that are about what a statement touches.</summary>
    public ICharacterSkillTable Skills => new SqliteCharacterSkillTable(this.database);

    public ICharacterPvpSkillProfileRepository PvpProfile => new PvpSkillProfileService(
        new SqliteCharacterTable(this.database),
        new SqliteCharacterSkillTable(this.database),
        new SqliteCharacterPvpTable(this.database));

    public IExpertSkillContractReconciliationRepository ExpertContracts(
        IExpertSkillContractRuleService rules) =>
        new ExpertSkillContractReconciliationService(
            new SqliteAccountPremiumTable(this.database),
            new SqliteCharacterTable(this.database),
            new SqliteCharacterSkillTable(this.database),
            rules);

    public IDungeonPersistentEffectRecoveryRepository DungeonRecovery(
        IDungeonExperienceProgressionPolicySource progression, IDungeonEffectRuleService rules) =>
        new DungeonEffectRecoveryService(
            new SqliteDungeonEffectOutboxTable(this.database),
            new SqliteAccountTable(this.database),
            new SqliteCharacterTable(this.database),
            progression,
            rules);

    /// <summary>Runs one call inside a use case and settles it, the way one command would.</summary>
    public async Task<TResult> InUseCaseAsync<TResult>(
        Func<CancellationToken, ValueTask<TResult>> work)
    {
        await using var lease = this.Scope.Begin();
        var result = await work(CancellationToken.None).ConfigureAwait(false);
        await lease.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// Everything a character is entered with, wired the way the server wires it.
    /// </summary>
    /// <remarks>
    /// Thirty-four things go into this. Naming them at each call site would say nothing about
    /// what any test is for, so the wiring lives here and a test says which database.
    /// </remarks>
    public ICharacterEntryStateRepository EntryState(IGoldLimitRuleService goldLimits) =>
        new CharacterEntryAssembly(
            new SqliteCharacterEntryTable(this.database),
            new SqliteCharacterConditionTable(this.database),
            new SqliteCharacterStatsTable(this.database),
            new SqliteCharacterSettingsTable(this.database),
            new SqliteCharacterPvpTable(this.database),
            new SqliteCharacterHotkeyTable(this.database),
            new SqliteCharacterDimensionTable(this.database),
            new SqliteCharacterSkillTable(this.database),
            new SqliteCharacterComboPageTable(this.database),
            new SqliteCharacterShieldDeckTable(this.database),
            new SqliteCharacterActiveQuestTable(this.database),
            new SqliteCharacterQuestNotifyTable(this.database),
            new SqliteCharacterQuestClearFlagTable(this.database),
            new SqliteCharacterGrowthWeaponTable(this.database),
            new SqliteCharacterGoldLimitTable(this.database),
            new SqliteCharacterDailyChallengeTable(this.database),
            new SqliteCharacterDailyChallengeEntryTable(this.database),
            new SqliteCharacterDailyChallengeTailTable(this.database),
            new SqliteCharacterDailyScheduleTable(this.database),
            new SqliteCharacterDungeonPermissionTable(this.database),
            new SqliteCharacterDailyCounterValueTable(this.database),
            new SqliteCharacterCreatureTable(this.database),
            new SqliteCharacterTitleBookTable(this.database),
            new SqliteCharacterAchievementTable(this.database),
            new SqliteCharacterCollectionBoxTable(this.database),
            new SqliteCharacterExpertJobTable(this.database),
            new SqliteCharacterExpertRecipeTable(this.database),
            new SqliteCharacterPvpMissionTable(this.database),
            new SqliteMailboxRecipientTable(this.database),
            new SqliteMailboxMessageTable(this.database),
            new SqliteMailboxAttachmentTable(this.database),
            goldLimits);

    /// <summary>
    /// The roster with a use case around every call, which is how the server dispatches these.
    /// </summary>
    /// <remarks>
    /// Creating a character and deleting one are each their own command, and each command is its
    /// own boundary. A test that calls several in a row is several use cases, not one, so this
    /// opens and settles one per call rather than wrapping the whole test.
    /// </remarks>
    public ICharacterRepository PerCall => new PerCallUseCase(this.Scope, this.Roster);

    private sealed class PerCallUseCase(DomainTransactionScope scope, ICharacterRepository inner)
        : ICharacterRepository
    {
        public ValueTask<IReadOnlyList<CharacterSummary>> ListByAccountAsync(
            AccountId accountId, CancellationToken cancellationToken) =>
            inner.ListByAccountAsync(accountId, cancellationToken);

        public ValueTask<ActiveCharacterState?> FindByAccountAndSlotAsync(
            AccountId accountId, ushort slotIndex, CancellationToken cancellationToken) =>
            inner.FindByAccountAndSlotAsync(accountId, slotIndex, cancellationToken);

        public ValueTask<bool> NameExistsAsync(byte[] name, CancellationToken cancellationToken) =>
            inner.NameExistsAsync(name, cancellationToken);

        public ValueTask<CharacterId> CreateAsync(
            NewCharacterSpec spec, CancellationToken cancellationToken) =>
            Settled(() => inner.CreateAsync(spec, cancellationToken), cancellationToken);

        public ValueTask<bool> SoftDeleteAsync(
            CharacterId characterId, CancellationToken cancellationToken) =>
            Settled(() => inner.SoftDeleteAsync(characterId, cancellationToken), cancellationToken);

        private async ValueTask<TResult> Settled<TResult>(
            Func<ValueTask<TResult>> work, CancellationToken cancellationToken)
        {
            await using var lease = scope.Begin();
            var result = await work();
            await lease.CommitAsync(cancellationToken);
            return result;
        }
    }
}
