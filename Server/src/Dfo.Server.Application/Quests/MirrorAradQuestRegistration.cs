using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.DomainEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Application.Quests;

internal static class MirrorAradQuestRegistration
{
    internal static IServiceCollection AddMirrorAradQuests(this IServiceCollection services)
    {
        services.TryAddSingleton<IMirrorAradQuestRuleService, MirrorAradQuestRuleService>();
        services.TryAddSingleton<IMirrorAradQuestDomainService, MirrorAradQuestDomainService>();
        services.TryAddSingleton<IMirrorDungeonQuotaDomainService, MirrorDungeonQuotaDomainService>();
        services.AddDomainEvent<MirrorQuestChanged>("quest.mirror-changed", 1);
        services.AddDomainSubscriber<DungeonParticipantLeft, MirrorAradQuestDomainService>("quest.mirror-leave", DomainEventDeliveryPolicy.Reliable);
        services.AddDomainSubscriber<MirrorQuestChanged, MirrorQuestNotificationSubscriber>("quest.mirror-notification", DomainEventDeliveryPolicy.LiveOnly,
            x => x.CharacterId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return services;
    }
}
