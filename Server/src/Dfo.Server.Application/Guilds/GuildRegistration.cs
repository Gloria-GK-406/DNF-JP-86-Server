using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Scheduling;
using Dfo.Server.Application.Scheduling;
using Dfo.Server.Application.DomainEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Application.Guilds;

internal static class GuildRegistration
{
    public static void AddGuilds(this IServiceCollection services)
    {
        services.TryAddSingleton<GuildEconomyRuleService>();
        services.TryAddSingleton<GuildProfileRuleService>();
        services.TryAddSingleton<GuildProductRuleService>();
        services.TryAddSingleton<GuildMonthlyContributionRuleService>();
        services.TryAddSingleton<IGuildHallAccessDomainService, GuildHallAccessDomainService>();
        services.TryAddSingleton<IGuildMembershipDomainService, GuildMembershipDomainService>();
        services.TryAddSingleton<IGuildProfileDomainService, GuildProfileDomainService>();
        services.TryAddSingleton<IGuildDonationDomainService, GuildDonationDomainService>();
        services.TryAddSingleton<IGuildRenameDomainService, GuildRenameDomainService>();
        services.TryAddSingleton<IGuildAllianceDomainService, GuildAllianceDomainService>();
        services.TryAddSingleton<IGuildDiscoveryDomainService, GuildDiscoveryDomainService>();
        services.TryAddSingleton<IGuildMembershipQueryDomainService, GuildMembershipQueryDomainService>();
        services.TryAddSingleton<IGuildOverviewDomainService, GuildOverviewDomainService>();
        services.TryAddSingleton<IGuildAttendanceDomainService, GuildAttendanceDomainService>();
        services.TryAddSingleton<IGuildContributionDomainService, GuildContributionDomainService>();
        services.TryAddSingleton<GuildMonthlyContributionDomainService>();
        services.TryAddSingleton<IGuildMonthlyContributionDomainService>(
            provider => provider.GetRequiredService<GuildMonthlyContributionDomainService>());
        services.TryAddSingleton<GuildWarehouseCapacityDomainService>();
        services.TryAddSingleton<GuildBenefitDomainService>();
        services.TryAddSingleton<IGuildBenefitDomainService>(provider => provider.GetRequiredService<GuildBenefitDomainService>());
        services.TryAddSingleton<IGuildBenefitGrants>(provider => provider.GetRequiredService<GuildBenefitDomainService>());
        services.TryAddSingleton<IGuildContentPurchaseDomainService, GuildContentPurchaseDomainService>();
        services.TryAddSingleton<IGuildGiftDomainService, GuildGiftDomainService>();
        services.TryAddSingleton<IGuildRedPacketDomainService, GuildRedPacketDomainService>();
        services.TryAddSingleton<IGuildRedPacketPurchasePolicy, GuildRedPacketPurchasePolicy>();
        services.TryAddSingleton<IGuildWarehouseDomainService, GuildWarehouseDomainService>();
        services.TryAddSingleton<IGuildBoardDomainService, GuildBoardDomainService>();
        services.TryAddSingleton<IGuildDungeonDomainService, GuildDungeonDomainService>();
        services.AddDomainEvent<GuildDungeonProgressChanged>("guild.dungeon.cleared", 1);
        services.AddDomainSubscriber<GuildDungeonProgressChanged, GuildDungeonProgressSubscriber>("guild.dungeon.progress",
            Dfo.Server.Abstractions.DomainEvents.DomainEventDeliveryPolicy.LiveOnly,
            fact => fact.Admission.Guild.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        services.TryAddSingleton<IGuildPartyTeleportDomainService, GuildPartyTeleportDomainService>();
        services.TryAddSingleton<IGuildNameOperationSessionDomainService, GuildNameOperationSessionDomainService>();
        services.TryAddSingleton<IGuildInvitationSessionDomainService, GuildInvitationSessionDomainService>();
        services.AddScoped<GuildContentExpirationTask>();
        services.AddScoped<GuildRedPacketExpirationTask>();
        services.AddScoped<GuildMonthlyContributionTask>();
        services.AddScheduledTask(new(GuildMonthlyContributionTask.Id,
            new GuildContributionMonth(), typeof(GuildMonthlyContributionTask))
        { MissedRun = ScheduledTaskMissedRunPolicy.RunOnce });
        services.AddScheduledTask(new("guild.red-packet.expiration",
            new IntervalTaskSchedule(TimeSpan.FromSeconds(10)), typeof(GuildRedPacketExpirationTask))
        { MissedRun = ScheduledTaskMissedRunPolicy.RunOnce });
        services.AddScheduledTask(new("guild.content.expiration",
            new IntervalTaskSchedule(TimeSpan.FromSeconds(10)), typeof(GuildContentExpirationTask))
        { MissedRun = ScheduledTaskMissedRunPolicy.RunOnce });
    }
}
