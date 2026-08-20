using Dfo.Server.Abstractions;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Persistence.Accounts;
using Dfo.Server.Persistence.Characters;
using Dfo.Server.Persistence.Inventory;
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
        services.TryAddSingleton<IAccountRepository>(
            _ => new SqliteAccountRepository(databasePath));
        services.TryAddSingleton<IAccountEntryStateRepository>(
            _ => new SqliteAccountEntryStateRepository(databasePath));
        services.TryAddSingleton<ICharacterRepository>(
            _ => new SqliteCharacterRepository(databasePath));
        services.TryAddSingleton(
            _ => new SqliteInventoryEntryRepository(databasePath));
        services.TryAddSingleton<ICharacterInventoryEntryRepository>(
            provider => provider.GetRequiredService<SqliteInventoryEntryRepository>());
        services.TryAddSingleton<IAccountInventoryEntryRepository>(
            provider => provider.GetRequiredService<SqliteInventoryEntryRepository>());
        services.TryAddSingleton<IEntryInventoryLeasePersistence>(
            provider => provider.GetRequiredService<SqliteInventoryEntryRepository>());
        services.TryAddSingleton<ICharacterEntryStateRepository>(
            provider => new SqliteCharacterEntryStateRepository(
                databasePath, provider.GetRequiredService<IGoldLimitRuleService>()));
        services.TryAddSingleton<ICharacterPvpSkillProfileRepository>(
            _ => new SqliteCharacterPvpSkillProfileRepository(databasePath));
        services.TryAddSingleton<IDungeonPersistentEffectRecoveryRepository>(
            provider => new SqliteDungeonPersistentEffectRecoveryRepository(databasePath,
                provider.GetRequiredService<IDungeonExperienceProgressionPolicySource>(),
                provider.GetRequiredService<IDungeonEffectRuleService>()));
        services.TryAddSingleton<IExpertSkillContractReconciliationRepository>(
            provider => new SqliteExpertSkillContractReconciliationRepository(
                databasePath, provider.GetRequiredService<IExpertSkillContractRuleService>()));
        services.TryAddSingleton<IEntryInventoryMutationRepository>(
            provider => new SqliteEntryInventoryMutationRepository(
                databasePath,
                provider.GetRequiredService<IDailyRefillRuleService>(),
                provider.GetRequiredService<IItemArrivalRuleService>()));
        // The one store whose commit can still fail, and so the one a use case may hold. It is
        // registered as the item store without qualification because it is the one that keeps
        // things; the store that does not is asked for by its own name.
        services.TryAddSingleton(_ => new SqliteItemStore(databasePath));
        // The character's own rows, kept in the same database and therefore settled by the same
        // participant as the items it owns.
        services.TryAddSingleton<ICharacterStore>(_ => new SqliteCharacterStore(databasePath));
        services.TryAddSingleton<IItemStore>(
            provider => provider.GetRequiredService<SqliteItemStore>());
    }
}
