using Dfo.Server.Application.Administration;
using Dfo.Server.Application.Mail;
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
using Dfo.Server.Application.Parties;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.Users;
using Dfo.Server.Application.World;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.World;

/// <summary>What the World domain registers for DungeonFlow; called once from <see cref="ApplicationModule"/>.</summary>
internal static class DungeonFlowRegistration
{
    internal static IServiceCollection AddDungeonFlow(this IServiceCollection services)
    {
        services.TryAddSingleton<IDungeonStructureRuleService, DungeonStructureRuleService>();
        services.TryAddSingleton<IDungeonEntryDecisionDomainService, DungeonEntryDecisionDomainService>();
        services.TryAddSingleton<IDungeonEntryRoutingDomainService, DungeonEntryRoutingDomainService>();
        services.TryAddSingleton<IDungeonEntryIntentDomainService, DungeonEntryIntentDomainService>();
        services.TryAddSingleton<IDungeonLifecycleDomainService, DungeonLifecycleDomainService>();
        services.AddDomainEvent<DungeonParticipantLeft>("dungeon.participant-left", 1);
        // What a room declares, and what a run standing in it makes of that: the actor numbering,
        // the monster-or-ally boundary, and which of the declared pieces are still on the floor.
        services.TryAddSingleton<IDungeonRoomRuleService, DungeonRoomRuleService>();
        services.TryAddSingleton<IDungeonRoomMechanismRuleService, DungeonRoomMechanismRuleService>();
        services.AddDungeonDeadlineScheduler();
        services.AddRuntimeEventSubscriber<DungeonRoomMechanismsChanged, DungeonRoomMechanismNotificationSubscriber>();
        services.TryAddSingleton<IDungeonParticipationDomainService, DungeonParticipationDomainService>();
        // Entering a dungeon and walking between its rooms.
        services.TryAddSingleton<IDungeonSelectionRuleService, DungeonSelectionRuleService>();
        services.TryAddSingleton<IDungeonEntryMaterialsDomainService, DungeonEntryMaterialsDomainService>();
        services.TryAddSingleton<IDungeonAdmissionRuleService, DungeonAdmissionRuleService>();
        services.TryAddSingleton<IDungeonAdmissionDomainService, DungeonAdmissionDomainService>();
        services.TryAddSingleton<DungeonVisitDomainService>();
        services.TryAddSingleton<IDungeonVisitDomainService, TransactionalDungeonVisitDomainService>();
        services.TryAddSingleton<IDungeonDifficultyRuleService, DungeonDifficultyRuleService>();
        services.TryAddSingleton<IDungeonCellFactory, DungeonCellFactory>();
        services.TryAddSingleton<IWorldContentEntryRuleService, WorldContentEntryRuleService>();
        services.TryAddSingleton<IEntryPlacementRuleService, EntryPlacementRuleService>();
        services.TryAddSingleton<IRoomMechanismMaintenanceDomainService, RoomMechanismMaintenanceDomainService>();
        return services;
    }
}
