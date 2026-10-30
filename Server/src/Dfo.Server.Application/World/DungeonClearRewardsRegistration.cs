using Dfo.Server.Application.Administration;
using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Authentication;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
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

/// <summary>What the World domain registers for DungeonClearRewards; called once from <see cref="ApplicationModule"/>.</summary>
internal static class DungeonClearRewardsRegistration
{
    internal static IServiceCollection AddDungeonClearRewards(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton<IHellRewardRuleService, HellRewardRuleService>();
        services.TryAddSingleton<IHellRewardDomainService, HellRewardDomainService>();
        services.TryAddSingleton(new HellRewardOptions
        {
            EquipmentRatePercent = int.TryParse(configuration["HellRewards:EquipmentRatePercent"], out var hellRate)
                ? hellRate : 100,
        });
        services.TryAddSingleton<IDungeonSettlementRuleService, DungeonSettlementRuleService>();
        services.TryAddSingleton<IDungeonClearDomainService, DungeonClearDomainService>();
        services.TryAddSingleton<IDungeonRevivalDomainService, DungeonRevivalDomainService>();
        // What a clear's card face holds: the one reader of the clear-reward rate table and the
        // only caller of ClearRewardRules. It is handed three content sources and a basis, and
        // nothing that could tell it anything about the world. The third joined when a dungeon
        // became able to switch its own paid card off; the count here said two until then.
        services.TryAddSingleton<IClearRewardRuleService, ClearRewardRuleService>();
        // The card flip's own undeclared number, kept apart from the monster side's rather than
        // shared with it: the two files declare different sections and there is nothing saying
        // their numerators are over the same thing. See ClearRewardRateOptions for the
        // composition this basis belongs to and why neither could be read out of the archive.
        services.TryAddSingleton(new ClearRewardRateOptions
        {
            DebugGuaranteePaidEquipment = bool.TryParse(
                configuration["ClearRewardRates:DebugGuaranteePaidEquipment"], out var guaranteedCard)
                && guaranteedCard,
            RateBasis =
                int.TryParse(configuration["ClearRewardRates:RateBasis"], out var cardBasis)
                && cardBasis > 0
                    ? cardBasis
                    : new ClearRewardRateOptions().RateBasis,
        });
        // Where a clear's two faces are decided and where each one's seed is prepared. It sits
        // above the rules because how many faces a clear has, and which facts each is drawn from,
        // is a question about the world -- which the rules are not allowed to ask.
        services.TryAddSingleton<ICardRewardDomainService, CardRewardDomainService>();
        services.TryAddSingleton<ICardFlipCostDomainService, CardFlipCostDomainService>();
        services.TryAddSingleton<IDungeonClearSettlementDomainService, DungeonClearSettlementDomainService>();
        // Runtime kill interpretation reads only the published Cell snapshot.
        services.TryAddSingleton<IMonsterKillDomainService, MonsterKillDomainService>();
        services.TryAddSingleton<ICardFlipDomainService, CardFlipDomainService>();
        services.TryAddSingleton<IMonsterKillRewardsDomainService, MonsterKillRewardsDomainService>();
        services.TryAddSingleton<IMonsterDeathDomainService, MonsterDeathDomainService>();
        services.TryAddSingleton<IDungeonPremiumBuffDomainService, DungeonPremiumBuffDomainService>();
        return services;
    }
}
