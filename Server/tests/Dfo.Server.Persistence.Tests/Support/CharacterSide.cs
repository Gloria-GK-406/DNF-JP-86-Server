using Dfo.Server.Application.Configuration;
using Dfo.Server.Application.Accounts;
using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Inventory;
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
    private readonly SqliteItemDomainStore itemStore;

    public CharacterSide(string databasePath)
    {
        this.Scope = new DomainTransactionScope();
        this.itemStore = new SqliteItemDomainStore(databasePath);
        this.database = new SqliteDatabase(databasePath, this.Scope);
    }

    public DomainTransactionScope Scope { get; }

    public SqliteItemDomainStore ItemStore => this.itemStore;

    public ICharacterRepository Roster => this.RosterOver(new SqliteCharacterItemTable(this.database));

    /// <summary>The same roster over a different answer to "what is in this character's slots".</summary>
    /// <remarks>
    /// One test needs the worn read to refuse while everything else about the database stays real.
    /// Corrupting a row cannot produce that: <c>items.item_core</c> is <c>NOT NULL CHECK
    /// (length(item_core) = 82)</c>, so the table will not hold a core the read would reject.
    /// </remarks>
    public ICharacterRepository RosterOver(ICharacterItemTable items) => new CharacterRosterService(
        new SqliteCharacterTable(this.database),
        new SqliteCharacterSkillTable(this.database),
        new SqliteCharacterConditionTable(this.database),
        new SqliteCharacterStatsTable(this.database),
        new SqliteCharacterPvpTable(this.database),
        new SqliteCharacterSettingsTable(this.database),
        new SqliteCharacterExpertJobTable(this.database),
        new SqliteCharacterContainerStateTable(this.database),
        items,
        new ItemPlacementRuleService(new WornPieces(), new NoWornSlots()), new RosterTestFatigue(),
        new ItemGrantDomainService(null!, new ItemPlacementRuleService(new WornPieces(), new NoWornSlots()),
            new ItemLifecycleDomainService(this.itemStore, this.Scope, new ItemPlacementRuleService(new WornPieces(), new NoWornSlots())),
            this.itemStore, this.Scope, new SqliteCharacterTable(this.database), null!, null!, new InventoryCapacityDomainService(new SqliteCharacterContainerStateTable(this.database), new SqliteAccountCargoStateTable(this.database), new InventoryCapacityRuleService())));

    /// <summary>
    /// Every template a worn piece, which is what a character's starting gear is.
    /// </summary>
    /// <remarks>
    /// Creation classifies each piece before it writes it, and these tests are about the rows a
    /// creation lands rather than about the archive. A source that declared nothing would make
    /// every starting piece a pile, which creation refuses.
    /// </remarks>
    private sealed class WornPieces : IItemPlacementDefinitionSource
    {
        public ValueTask<ItemPlacement?> GetAsync(int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ItemPlacement?>(
                new ItemPlacement(1, 1, new(BusinessInventorySpace.CarriedEquipment, 0, 55))
                { WorldClass = ItemWorldClass.Equipment });
    }

    /// <summary>Nothing here resolves a worn slot; no test through this side puts a piece on.</summary>
    private sealed class NoWornSlots : IEquipmentSlotDefinitionSource
    {
        public ValueTask<BusinessSlot?> GetAsync(int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<BusinessSlot?>(null);
    }

    /// <summary>The skill table itself, for the tests that are about what a statement touches.</summary>
    public ICharacterSkillTable Skills => new SqliteCharacterSkillTable(this.database);

    /// <summary>
    /// A character's things, for the tests that have to change what a character is wearing.
    /// </summary>
    /// <remarks>
    /// The roster now draws from what is worn, so a test about the roster following a change has to
    /// be able to make one. Creation can only put a character into the world dressed as it was
    /// made; putting something on afterwards is these two tables, which is what the move path uses
    /// underneath as well.
    /// </remarks>
    public ICharacterItemTable Items => new SqliteCharacterItemTable(this.database);

    /// <summary>
    /// The characters table itself, for the tests that are about what one statement reads.
    /// </summary>
    /// <remarks>
    /// The narrow reads keyed on a character's own identifier answer nobody through the roster, so
    /// a test about which column lands in which field has to ask the table.
    /// </remarks>
    public ICharacterTable Characters => new SqliteCharacterTable(this.database);

    public ICharacterPvpSkillProfileRepository PvpProfile => new PvpSkillProfileService(
        new SqliteCharacterTable(this.database),
        new SqliteCharacterSkillTable(this.database),
        new SqliteCharacterPvpTable(this.database));

    public IExpertSkillContractReconciliationRepository ExpertContracts(
        IExpertSkillContractRuleService rules) =>
        new ExpertSkillContractReconciliationService(
            new PremiumServiceDomainService(new SqliteAccountPremiumTable(this.database), this.Scope, TimeProvider.System, null!, new DisabledDefaultPremiumSettings()),
            new SqliteCharacterTable(this.database),
            new SqliteCharacterSkillTable(this.database),
            rules);

    public IDungeonPersistentEffectRecoveryRepository DungeonRecovery(
        IExperienceRuleService experienceRules, IDungeonEffectRuleService rules) =>
        new DungeonEffectRecoveryService(
            new SqliteDungeonEffectOutboxTable(this.database),
            new SqliteAccountTable(this.database),
            new SqliteCharacterTable(this.database),
            experienceRules, rules, new CharacterDomainService(null!, null!, id => new PlayingCharacter(id,
                null!, null!, null!, null!, null!, new SqliteCharacterTable(database), null!, null!, null!,
                experienceRules: experienceRules,
                capsules: new ExperienceCapsuleDomainService(new SqliteAccountTable(database), experienceRules, rules, Scope),
                transactions: Scope), null!));

    /// <summary>Runs one call inside a use case and settles it, the way one command would.</summary>
    public async Task<TResult> InUseCaseAsync<TResult>(
        Func<CancellationToken, ValueTask<TResult>> work)
    {
        await using var lease = this.Scope.Begin();
        var result = await work(CancellationToken.None).ConfigureAwait(false);
        await lease.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        return result;
    }

    private sealed class ResetSettings : Dfo.Server.Abstractions.Configuration.IServerConfigService
    {
        public T GetConfig<T>(string key) => (T)(object)TimeSpan.FromHours(6);
    }
    private sealed class UsageRules : IPremiumServiceDefinitionSource
    {
        public IReadOnlyList<int> ReadQuestHelperMinimumDropRates() => new[] { 100, 70, 30 };
        public DoublePotRules ReadDoublePotRules() => new(8, new HashSet<int>());
        public IReadOnlyList<int> ReadGrowthBossEquipmentBonusPercents() => new[] {20, 23, 27, 30};
        public double ReadBlackDiamondExperienceBonusRate() => .05;
        public double ReadGrowthExperienceBonusRate() => 0.2;
        public PremiumContractLevels ReadContractLevels() => new(5, 10);
        public int ReadFreeWeaknessDailyLimit() => 10;
        public int ReadFreeGoldCardDailyLimit() => 10;
        public int ReadDungeonBuffDailyLimit() => 10;
        public int ReadAutoRepairDailyLimit() => 6;
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
            new CharacterDungeonDifficultyDomainService(new SqliteCharacterDungeonPermissionTable(this.database), new Dfo.Server.Application.World.DungeonDifficultyRuleService()),
            new ReviveCoinQueryDomainService(new ReviveCoinRuleService(new DailyResetRuleService(new ResetSettings())),
                new Dfo.Server.Application.UsageQuotas.UsageQuotaDomainService(
                    new LegacyUsageQuotaStore(new SqliteConsumableUsageTable(this.database),
                        new SqlitePremiumServiceUsageTable(this.database), new Dfo.Server.Persistence.Quests.SqliteMirrorAradQuestDomainStore(this.database), this.database),
                    this.Scope, null!), TimeProvider.System),
            new SqliteCharacterCreatureTable(this.database),
            new SqliteCharacterTitleBookTable(this.database),
            new SqliteCharacterAchievementTable(this.database),
            new SqliteCharacterCollectionBoxTable(this.database),
            new SqliteCharacterExpertJobTable(this.database),
            new SqliteCharacterExpertRecipeTable(this.database),
            new SqliteCharacterPvpMissionTable(this.database),
            new Dfo.Server.Application.Mail.MailboxQueryDomainService(new Dfo.Server.Persistence.Mail.SqliteMailboxRepository(this.database), TimeProvider.System,
                new SqliteMailboxMessageTable(this.database), new SqliteMailboxAttachmentTable(this.database)),
            goldLimits, new PremiumServiceUsageDomainService(
                new PremiumServiceDomainService(new SqliteAccountPremiumTable(this.database), this.Scope, TimeProvider.System, null!, new DisabledDefaultPremiumSettings()),
                new Dfo.Server.Application.UsageQuotas.UsageQuotaDomainService(
                    new LegacyUsageQuotaStore(new SqliteConsumableUsageTable(this.database),
                        new SqlitePremiumServiceUsageTable(this.database), new Dfo.Server.Persistence.Quests.SqliteMirrorAradQuestDomainStore(this.database), this.database),
                    this.Scope, null!),
                new SqliteCharacterTable(this.database), new PremiumServiceRuleService(new UsageRules()),
                new DailyResetRuleService(new ResetSettings()), this.Scope, TimeProvider.System, null!), new SqliteCharacterSkillBuildTable(this.database));

    /// <summary>
    /// The roster with a use case around every call, which is how the server dispatches these.
    /// </summary>
    /// <remarks>
    /// Creating a character and deleting one are each their own command, and each command is its
    /// own boundary. A test that calls several in a row is several use cases, not one, so this
    /// opens and settles one per call rather than wrapping the whole test.
    /// </remarks>
    public ICharacterRepository PerCall => new PerCallUseCase(this.Scope, this.Roster);

    /// <summary>The same, over a substituted answer to what a character's slots hold.</summary>
    public ICharacterRepository PerCallOver(ICharacterItemTable items) =>
        new PerCallUseCase(this.Scope, this.RosterOver(items));

    private sealed class PerCallUseCase(DomainTransactionScope scope, ICharacterRepository inner)
        : ICharacterRepository
    {
        public ValueTask<IReadOnlyList<CharacterSummary>> ListByAccountAsync(
            AccountId accountId, CancellationToken cancellationToken) =>
            inner.ListByAccountAsync(accountId, cancellationToken);

        public ValueTask<IReadOnlyList<CharacterSummary>> ListByAccountWithAppearanceAsync(
            AccountId accountId, CancellationToken cancellationToken) =>
            inner.ListByAccountWithAppearanceAsync(accountId, cancellationToken);

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

internal sealed class DisabledDefaultPremiumSettings : Dfo.Server.Abstractions.Configuration.IServerConfigService
{
    public T GetConfig<T>(string key) => key == "Premium:BlackDiamondEnabled" && typeof(T) == typeof(bool)
        ? (T)(object)false : key == "Premium:BlackDiamondExtraFatigue" && typeof(T) == typeof(ushort)
            ? (T)(object)(ushort)32 : throw new InvalidOperationException(key);
}

internal sealed class RosterTestFatigue : ICharacterFatigueDomainService
{
    public ValueTask<CharacterFatigueBalance> BalanceOfAsync(CharacterId id, CancellationToken token) => ValueTask.FromResult(new CharacterFatigueBalance(188, 0));
    public ValueTask SpendAsync(CharacterId id, ushort points, CancellationToken token) => ValueTask.CompletedTask;
    public ValueTask RefundAsync(CharacterId id, ushort points, CancellationToken token) => ValueTask.CompletedTask;
}
