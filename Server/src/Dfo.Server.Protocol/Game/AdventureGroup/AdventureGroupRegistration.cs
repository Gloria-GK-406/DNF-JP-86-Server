using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Application.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.AdventureGroup;

/// <summary>What the AdventureGroup slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class AdventureGroupRegistration
{
    internal const string LevelUpSubscriberId = "adventure-group.level-up";

    internal static IServiceCollection AddAdventureGroupSlice(this IServiceCollection services)
    {
        services.AddGameNotiMessageEncoder<AdventureGroupLevelUpNotice, AdventureGroupLevelUpNoticeEncoder>();
        // Live-only and without a coalescing key: told at most once per level change, never merged.
        services.AddDomainSubscriber<CharacterLevelChanged, AdventureGroupLevelChangedSubscriber>(
            LevelUpSubscriberId, DomainEventDeliveryPolicy.LiveOnly);
        services.AddNotificationHandler<AdventureGroupLevelUp, AdventureGroupLevelUpHandler>();
        return services;
    }
}
