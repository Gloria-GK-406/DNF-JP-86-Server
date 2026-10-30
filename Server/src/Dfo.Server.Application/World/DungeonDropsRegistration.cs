using Dfo.Server.Application.Administration;
using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Authentication;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Application.Quests;
using Dfo.Server.Application.Parties;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.World;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.World;

/// <summary>What the World domain registers for DungeonDrops; called once from <see cref="ApplicationModule"/>.</summary>
internal static class DungeonDropsRegistration
{
    internal static IServiceCollection AddDungeonDrops(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton<IObjectDropRuleService, ObjectDropRuleService>();

        // The compensation seam and its first-phase implementation, which never compensates. It is
        // registered rather than left out so that everything upstream is written against the real
        // shape from the first day.
        // What an occasion drops: the one reader of the three drop tables and the only caller of
        // DropRules. The compensation seam below stays outside it, because what a character is
        // owed is state that crosses runs.
        services.TryAddSingleton<IDropRuleService, DropRuleService>();
        services.TryAddSingleton<IIndependentDropRuleService, IndependentDropRuleService>();
        services.TryAddSingleton(new IndependentDropOptions
        {
            DebugGuaranteeGrowthBossEquipment = bool.Parse(configuration["IndependentDrops:DebugGuaranteeGrowthBossEquipment"] ?? "false"),
        });
        services.TryAddSingleton<IExtraDropRuleService, ExtraDropRuleService>();
        services.TryAddSingleton<IWorldDropRuleService, WorldDropRuleService>();
        services.TryAddSingleton<IMonsterDeclaredDropRuleService, MonsterDeclaredDropRuleService>();
        services.TryAddSingleton(new WorldDropOptions
        {
            ProbabilityBasis = configuration["WorldDrops:ProbabilityBasis"] is { } configuredWorldBasis
                ? int.Parse(configuredWorldBasis, System.Globalization.CultureInfo.InvariantCulture) : 100000,
            DebugClearCubeOnMiss = bool.Parse(configuration["WorldDrops:DebugClearCubeOnMiss"] ?? "false"),
        });
        services.TryAddSingleton<IDropRateAdjuster, NoDropRateAdjustment>();
        // The one number the archive does not declare. See DropRateOptions for the two candidate
        // readings and why neither could be chosen from the files.
        services.TryAddSingleton(new DropRateOptions
        {
            RateBasis = int.TryParse(configuration["DropRates:RateBasis"], out var basis)
                && basis > 0
                ? basis
                : new DropRateOptions().RateBasis,
        });
        services.TryAddSingleton<IQuestItemDropDomainService, QuestItemDropDomainService>();
        // The seams the command handlers reach the rules through, so that none of them holds a
        // rule service of its own.
        services.TryAddSingleton<IGroundPickupDomainService, GroundPickupDomainService>();
        services.TryAddSingleton<MonsterDropDomainService>();
        services.TryAddSingleton<IMonsterDropDomainService>(services =>
            services.GetRequiredService<MonsterDropDomainService>());
        services.TryAddSingleton<IQuestEnemyDropDomainService, QuestEnemyDropDomainService>();
        services.TryAddSingleton<IQuestEnemyDropRuleService, QuestEnemyDropRuleService>();
        return services;
    }
}
