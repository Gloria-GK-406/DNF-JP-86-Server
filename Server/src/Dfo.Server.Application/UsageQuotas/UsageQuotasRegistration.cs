using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Application.Sessions;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.UsageQuotas;

/// <summary>What the UsageQuotas domain registers; called once from <see cref="Dfo.Server.Application.ApplicationModule"/>.</summary>
internal static class UsageQuotasRegistration
{
    internal static IServiceCollection AddUsageQuotas(this IServiceCollection services)
    {
        services.TryAddSingleton<Dfo.Server.Abstractions.UsageQuotas.IUsageQuotaDomainService,
            Dfo.Server.Application.UsageQuotas.UsageQuotaDomainService>();
        services.AddRuntimeEventSubscriber<Dfo.Server.Abstractions.UsageQuotas.ConsumableUsageRecorded,
            Dfo.Server.Application.UsageQuotas.UsageQuotaDomainService>();
        services.AddRuntimeEventSubscriber<Dfo.Server.Abstractions.UsageQuotas.PremiumUsageRecorded,
            Dfo.Server.Application.UsageQuotas.UsageQuotaDomainService>();
        services.AddRuntimeEventSubscriber<Dfo.Server.Abstractions.UsageQuotas.MirrorEntryRecorded,
            Dfo.Server.Application.UsageQuotas.UsageQuotaDomainService>();
        services.AddRuntimeEventSubscriber<Dfo.Server.Abstractions.UsageQuotas.MirrorRewardRecorded,
            Dfo.Server.Application.UsageQuotas.UsageQuotaDomainService>();
        services.AddDomainEvent<Dfo.Server.Abstractions.UsageQuotas.UsageQuotaChanged>("usage-quota.changed", 1);
        services.TryAddSingleton<Dfo.Server.Abstractions.UsageQuotas.IUsageQuotaSnapshotDomainService,
            Dfo.Server.Application.UsageQuotas.UsageQuotaSnapshotDomainService>();
        services.AddDomainSubscriber<Dfo.Server.Abstractions.UsageQuotas.UsageQuotaChanged,
            Dfo.Server.Application.UsageQuotas.UsageQuotaNotificationSubscriber>("usage-quota.notifications",
            Dfo.Server.Abstractions.DomainEvents.DomainEventDeliveryPolicy.LiveOnly,
            fact => $"{fact.CharacterId.Value}:{(int)fact.Family}");
        services.AddRuntimeEventSubscriber<Dfo.Server.Abstractions.UsageQuotas.DimensionEntryRecorded,
            Dfo.Server.Application.UsageQuotas.UsageQuotaDomainService>();
        services.AddRuntimeEventSubscriber<Dfo.Server.Abstractions.UsageQuotas.TournamentEntryRecorded,
            Dfo.Server.Application.UsageQuotas.UsageQuotaDomainService>();
        services.AddRuntimeEventSubscriber<Dfo.Server.Abstractions.UsageQuotas.AltarEntryRecorded,
            Dfo.Server.Application.UsageQuotas.UsageQuotaDomainService>();
        services.AddRuntimeEventSubscriber<Dfo.Server.Abstractions.UsageQuotas.LostLandEntryRecorded,
            Dfo.Server.Application.UsageQuotas.UsageQuotaDomainService>();
        services.AddRuntimeEventSubscriber<Dfo.Server.Abstractions.UsageQuotas.HerasQuotaRecorded,
            Dfo.Server.Application.UsageQuotas.UsageQuotaDomainService>();
        services.AddRuntimeEventSubscriber<Dfo.Server.Abstractions.UsageQuotas.GoldSilverHornEntryRecorded,
            Dfo.Server.Application.UsageQuotas.UsageQuotaDomainService>();
        services.AddRuntimeEventSubscriber<Dfo.Server.Abstractions.UsageQuotas.DespairQuotaRecorded,
            Dfo.Server.Application.UsageQuotas.UsageQuotaDomainService>();
        services.AddRuntimeEventSubscriber<Dfo.Server.Abstractions.UsageQuotas.ReviveCoinClaimRecorded,
            Dfo.Server.Application.UsageQuotas.UsageQuotaDomainService>();
        services.AddRuntimeEventSubscriber<Dfo.Server.Abstractions.UsageQuotas.GiantDragonCrystalExchangeRecorded,
            Dfo.Server.Application.UsageQuotas.UsageQuotaDomainService>();
        return services;
    }
}
