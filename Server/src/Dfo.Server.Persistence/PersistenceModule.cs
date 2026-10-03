using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Persistence.Mail;
using Dfo.Server.Abstractions;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Persistence.Accounts;
using Dfo.Server.Persistence.Characters;
using Dfo.Server.Persistence.Creatures;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;
using Dfo.Server.Persistence.Quests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Persistence;

/// <summary>
/// Puts the one database behind every contract that keeps something.
/// </summary>
/// <remarks>
/// Nothing outside this project names a type it registers: every class here is internal, and what
/// callers hold is the contract declared in Abstractions. That is the whole reason the project can
/// stand apart -- there is no surface for anyone to reach around the contract and touch a row.
///
/// It does not stand alone at runtime, though. Three of the registrations below ask for something
/// the content module provides, so a container built from this module by itself resolves nothing
/// useful. The two modules are meant to be present together, not chosen between.
/// </remarks>
public sealed class PersistenceModule : IServerModule
{
    public string Name => "Persistence";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var configuredDatabasePath = configuration["Database:Path"] ?? "Data/dfo-server.db";
        var databasePath = Path.IsPathRooted(configuredDatabasePath)
            ? configuredDatabasePath
            : Path.Combine(AppContext.BaseDirectory, configuredDatabasePath);
        // The database first: it is what every table below reaches through, and it is what
        // puts the file in shape.
        services.TryAddSingleton(provider => new SqliteDatabase(
            databasePath, provider.GetRequiredService<IDomainTransactionAccessor>()));
        services.TryAddSingleton<INoticeStateTable, Dfo.Server.Persistence.Notices.NoticeStateTable>();
        services.TryAddSingleton<IAccountTable>(
            provider => new SqliteAccountTable(provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<IAccountPurchaseRestrictionTable, SqliteAccountPurchaseRestrictionTable>();
        services.TryAddSingleton<IAuthenticationPersistence, SqliteAuthenticationPersistence>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Administration.IGmAdministrationPersistence, Dfo.Server.Persistence.Administration.SqliteGmAdministrationPersistence>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Administration.IGmPlayerDirectoryPersistence, Dfo.Server.Persistence.Administration.SqliteGmPlayerDirectoryPersistence>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Administration.IGmMailPersistence, Dfo.Server.Persistence.Administration.SqliteGmMailPersistence>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Administration.IGmCeraPersistence, Dfo.Server.Persistence.Administration.SqliteGmCeraPersistence>();
        services.TryAddSingleton<IAccountSettingsTable>(
            provider => new SqliteAccountSettingsTable(
                provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<IAccountPremiumTable>(
            provider => new SqliteAccountPremiumTable(
                provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<IAccountLotteryProgressTable>(
            provider => new SqliteAccountLotteryProgressTable(
                provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<ICharacterTable>(
            provider => new SqliteCharacterTable(provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<ICharacterTutorialRepository>(provider => (ICharacterTutorialRepository)provider.GetRequiredService<ICharacterTable>());
        services.TryAddSingleton<ICharacterSkillBuildTable, SqliteCharacterSkillBuildTable>();
        services.TryAddSingleton<ICharacterSkillTable>(
            provider => new SqliteCharacterSkillTable(
                provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<IDungeonEffectOutboxTable>(
            provider => new SqliteDungeonEffectOutboxTable(
                provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<ICharacterBuyRestrictTable>(
            provider => new SqliteCharacterBuyRestrictTable(
                provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<ICharacterMercenarySupportTable>(
            provider => new SqliteCharacterMercenarySupportTable(
                provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<ICharacterItemTable>(
            provider => new SqliteCharacterItemTable(provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<IAccountCargoItemTable>(
            provider => new SqliteAccountCargoItemTable(provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<ICharacterContainerStateTable>(
            provider => new SqliteCharacterContainerStateTable(provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<IInventoryExpansionStateTable, SqliteInventoryExpansionStateTable>();
        services.TryAddSingleton<IAccountCargoStateTable>(
            provider => new SqliteAccountCargoStateTable(provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<ICharacterConditionTable>(
            provider => new SqliteCharacterConditionTable(provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<ICharacterStatsTable>(
            provider => new SqliteCharacterStatsTable(provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<ICharacterPvpTable>(
            provider => new SqliteCharacterPvpTable(provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<ICharacterSettingsTable>(
            provider => new SqliteCharacterSettingsTable(provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<ICharacterExpertJobTable>(
            provider => new SqliteCharacterExpertJobTable(provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<ICharacterDailyChallengeTable>(
            provider => new SqliteCharacterDailyChallengeTable(provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<ICharacterCarriedGoldTable>(
            provider => new SqliteCharacterCarriedGoldTable(
                provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<INpcShopPurchaseTable, SqliteNpcShopPurchaseTable>();
        services.TryAddSingleton<ICharacterGoldLimitTable>(
            provider => new SqliteCharacterGoldLimitTable(provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<ICharacterItemCooldownTable>(
            provider => new SqliteCharacterItemCooldownTable(provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<ICharacterItemEffectTable>(
            provider => new SqliteCharacterItemEffectTable(provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<ICharacterItemLockTable>(
            provider => new SqliteCharacterItemLockTable(provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<ICharacterRentalTable>(
            provider => new SqliteCharacterRentalTable(provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<IPremiumServiceUsageTable, SqlitePremiumServiceUsageTable>();
        services.TryAddSingleton<IConsumableUsageTable, SqliteConsumableUsageTable>();
        services.TryAddSingleton<ICharacterFatigueTable, SqliteCharacterFatigueTable>();
        services.TryAddSingleton<ICharacterDailyCounterTable>(
            provider => new SqliteCharacterDailyCounterTable(provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<ICharacterEntryTable>(
            provider => new SqliteCharacterEntryTable(provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<ICharacterHotkeyTable>(
            provider => new SqliteCharacterHotkeyTable(provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<ICharacterDimensionTable>(
            provider => new SqliteCharacterDimensionTable(provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<ICharacterComboPageTable>(
            provider => new SqliteCharacterComboPageTable(provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<ICharacterShieldDeckTable>(
            provider => new SqliteCharacterShieldDeckTable(provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<ICharacterActiveQuestTable>(
            provider => new SqliteCharacterActiveQuestTable(provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<ICharacterQuestNotifyTable>(
            provider => new SqliteCharacterQuestNotifyTable(provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<ICharacterQuestClearFlagTable>(
            provider => new SqliteCharacterQuestClearFlagTable(provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<ICharacterGrowthWeaponTable>(
            provider => new SqliteCharacterGrowthWeaponTable(provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<ICharacterDailyChallengeEntryTable>(
            provider => new SqliteCharacterDailyChallengeEntryTable(provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<ICharacterDailyChallengeTailTable>(
            provider => new SqliteCharacterDailyChallengeTailTable(provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<ICharacterDailyScheduleTable>(
            provider => new SqliteCharacterDailyScheduleTable(provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<ICharacterDungeonPermissionTable>(
            provider => new SqliteCharacterDungeonPermissionTable(provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<ICharacterDailyCounterValueTable>(
            provider => new SqliteCharacterDailyCounterValueTable(provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<ICharacterCreatureTable>(
            provider => new SqliteCharacterCreatureTable(provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<ICharacterTitleBookTable>(
            provider => new SqliteCharacterTitleBookTable(provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<ICharacterAchievementTable>(
            provider => new SqliteCharacterAchievementTable(provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<ICharacterCollectionBoxTable>(
            provider => new SqliteCharacterCollectionBoxTable(provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<ICharacterExpertRecipeTable>(
            provider => new SqliteCharacterExpertRecipeTable(provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<ICharacterPvpMissionTable>(
            provider => new SqliteCharacterPvpMissionTable(provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<IMailboxRepository, SqliteMailboxRepository>();
        services.TryAddSingleton<IMailboxMessageTable>(
            provider => new SqliteMailboxMessageTable(provider.GetRequiredService<SqliteDatabase>()));
        services.TryAddSingleton<IMailboxAttachmentTable>(
            provider => new SqliteMailboxAttachmentTable(provider.GetRequiredService<SqliteDatabase>()));
        // The one store whose commit can still fail, and so the one a use case may hold. It is
        // registered as the item store without qualification because it is the one that keeps
        // things; the store that does not is asked for by its own name.
        services.TryAddSingleton(_ => new SqliteItemDomainStore(databasePath));
        // The character's own rows, kept in the same database and therefore settled by the same
        // participant as the items it owns.
        services.TryAddSingleton<ICharacterDomainStore>(_ => new SqliteCharacterDomainStore(databasePath));
        services.TryAddSingleton<ICreatureDomainStore>(_ => new SqliteCreatureDomainStore(databasePath));
        services.TryAddSingleton<IItemDomainStore>(
            provider => provider.GetRequiredService<SqliteItemDomainStore>());
        // The character's quests, kept in the same database and therefore settled by the same
        // participant as the character and the items it owns.
        services.TryAddSingleton<IQuestDomainStore>(_ => new SqliteQuestDomainStore(databasePath));
        // The character's combo pages, kept in the same database and therefore settled by the same
        // participant again. It is handed the entry load's own reader of those rows rather than
        // being left to select them a second way.
        services.TryAddSingleton<IDarkKnightComboDomainStore>(
            provider => new SqliteDarkKnightComboDomainStore(
                databasePath, provider.GetRequiredService<ICharacterComboPageTable>()));
    }
}
