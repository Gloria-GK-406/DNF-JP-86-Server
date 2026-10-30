using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World.IllusionTower;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Application.World.BossTower;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.World;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.World;

/// <summary>What the World domain registers for DungeonContents; called once from <see cref="ApplicationModule"/>.</summary>
internal static class DungeonContentsRegistration
{
    internal static IServiceCollection AddDungeonContents(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton<CoupleDungeonRule>();
        services.TryAddSingleton<ICoupleDungeonDomainService, CoupleDungeonDomainService>();
        services.TryAddSingleton<IHerasRuleService, HerasRuleService>();
        services.TryAddSingleton(new LostLandRuntimePolicy(configuration.GetValue<int?>("LostLand:LimitSecondsOverride"),
            configuration.GetValue<bool>("LostLand:UnlimitedEntriesForTesting")));
        services.TryAddSingleton<ILostLandRuleService, LostLandRuleService>();
        services.TryAddSingleton<ILostLandFeverRuleService, LostLandFeverRuleService>();
        services.TryAddSingleton<ILostLandDropRuleService, LostLandDropRuleService>();
        services.TryAddSingleton<ILostLandDungeonDomainService, LostLandDungeonDomainService>();
        services.TryAddSingleton<ILostLandConsumableRuleService, LostLandConsumableRuleService>();
        services.TryAddSingleton<ILostLandConsumableDomainService, LostLandConsumableDomainService>();
        services.TryAddSingleton<IHerasDungeonDomainService, HerasDungeonDomainService>();
        services.TryAddSingleton<IHerasQuotaDomainService, HerasQuotaDomainService>();
        services.TryAddSingleton<IGiantDragonRuleService, GiantDragonRuleService>();
        services.TryAddSingleton<IGiantDragonDomainService, GiantDragonDomainService>();
        services.TryAddSingleton<ISouthernDaleRuleService, SouthernDaleRuleService>();
        services.TryAddSingleton<IDarkElfRuinsRuleService, DarkElfRuinsRuleService>();
        services.TryAddSingleton<IDarkElfRuinsDomainService, DarkElfRuinsDomainService>();
        services.AddSingleton<IDungeonBossClearHook>(sp => sp.GetRequiredService<IDarkElfRuinsDomainService>());
        services.AddDomainEvent<DarkElfRuinsInventoryChanged>("darkelf-ruins.inventory-changed", 1);
        services.TryAddSingleton<ISealedTreasureRuleService, SealedTreasureRuleService>();
        services.TryAddSingleton<IGoldSilverHornRuleService, GoldSilverHornRuleService>();
        services.TryAddSingleton<IBossTowerRuleService, BossTowerRuleService>();
        services.TryAddSingleton<IBossTowerTicketDomainService, BossTowerTicketDomainService>();
        services.AddDomainEvent<BossTowerTicketRefilled>("boss-tower.ticket-refilled", 1);
        services.AddOnlineTicketRefill<IBossTowerTicketDomainService, IBossTowerRuleService>("boss-tower.ticket-refill.online");
        services.TryAddSingleton<IGoldSilverHornDomainService, GoldSilverHornDomainService>();
        services.TryAddSingleton<ISealedTreasureDomainService, SealedTreasureDomainService>();
        services.TryAddSingleton<ISouthernDaleDomainService, SouthernDaleDomainService>();
        services.AddSingleton<IDungeonBossClearHook>(sp => sp.GetRequiredService<ISouthernDaleDomainService>());
        services.TryAddSingleton<ISouthernDaleQuestDomainService, SouthernDaleQuestDomainService>();
        services.AddDomainEvent<SouthernDaleInventoryChanged>("southern-dale.inventory-changed", 1);
        services.AddDomainEvent<SouthernDaleDailyRefillCompleted>("southern-dale.daily-refill-completed", 1);
        services.AddOnlineTicketRefill<ISouthernDaleDomainService, ISouthernDaleRuleService>("southern-dale.ticket-refill.online");
        services.TryAddSingleton<ITauKingdomRuleService, TauKingdomRuleService>();
        services.TryAddSingleton<ITauKingdomDomainService, TauKingdomDomainService>();
        services.AddDomainEvent<TauKingdomTicketRefilled>("tau-kingdom.ticket-refilled", 1);
        services.AddOnlineTicketRefill<ITauKingdomDomainService, ITauKingdomRuleService>("tau-kingdom.ticket-refill.online");
        services.TryAddSingleton<ISaoTowerRuleService, SaoTowerRuleService>();
        services.TryAddSingleton<ISaoTowerDomainService, SaoTowerDomainService>();
        services.AddSingleton<IDungeonBossClearHook>(sp => sp.GetRequiredService<ISaoTowerDomainService>());
        services.AddDomainEvent<SaoTowerCleared>("sao-tower.cleared", 1);
        services.TryAddSingleton<IDespairTowerRuleService, DespairTowerRuleService>();
        services.TryAddSingleton<IDespairTowerDomainService, DespairTowerDomainService>();
        services.TryAddSingleton<IDimensionDungeonRuleService, DimensionDungeonRuleService>();
        services.TryAddSingleton<IDimensionDungeonDomainService, DimensionDungeonDomainService>();
        services.TryAddSingleton<IHellDungeonEntryRuleService, HellDungeonEntryRuleService>();
        services.TryAddSingleton<IHellDungeonEntryDomainService, HellDungeonEntryDomainService>();
        services.TryAddSingleton<IMirrorDungeonRuleService, MirrorDungeonRuleService>();
        services.TryAddSingleton<IMirrorDungeonEntryDomainService, MirrorDungeonEntryDomainService>();
        services.TryAddSingleton<ITournamentDungeonRuleService, TournamentDungeonRuleService>();
        services.TryAddSingleton<TournamentDungeonDomainService>();
        services.TryAddSingleton<ITournamentDungeonDomainService>(sp => sp.GetRequiredService<TournamentDungeonDomainService>());
        services.AddDomainSubscriber<DungeonParticipantLeft, TournamentDungeonDomainService>("tournament.departure", Dfo.Server.Abstractions.DomainEvents.DomainEventDeliveryPolicy.Reliable);
        services.TryAddSingleton<ICycleDungeonRuleService, CycleDungeonRuleService>();
        services.TryAddSingleton<ICycleDungeonEntryDomainService, CycleDungeonEntryDomainService>();
        services.TryAddSingleton<Dfo.Server.Abstractions.World.BossTower.IBossTowerRewardRuleService, Dfo.Server.Application.World.BossTower.BossTowerRewardRuleService>();
        services.TryAddSingleton<Dfo.Server.Abstractions.World.BossTower.IBossTowerRewardDomainService, Dfo.Server.Application.World.BossTower.BossTowerRewardDomainService>();
        services.TryAddSingleton<Dfo.Server.Abstractions.World.BossTower.IBossTowerDomainService, Dfo.Server.Application.World.BossTower.BossTowerDomainService>();
        services.TryAddSingleton<Dfo.Server.Application.World.IllusionTower.TowerInventoryDomainService>();
        return services;
    }
}
