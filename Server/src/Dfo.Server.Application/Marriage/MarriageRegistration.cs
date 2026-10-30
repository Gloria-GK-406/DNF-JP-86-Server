using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Data.Authentication;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Application.Quests;
using Dfo.Server.Application.Sessions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Marriage;

/// <summary>What the Marriage domain registers; called once from <see cref="Dfo.Server.Application.ApplicationModule"/>.</summary>
internal static class MarriageRegistration
{
    internal static IServiceCollection AddMarriage(this IServiceCollection services, IConfiguration configuration)
    {
        var proposalTimeoutSeconds = configuration.GetValue<int>("Marriage:ProposalTimeoutSeconds", 60);
        if (proposalTimeoutSeconds is < 1 or > 600) throw new InvalidOperationException("Marriage proposal timeout must be 1..600 seconds.");
        services.TryAddSingleton(new Dfo.Server.Application.Marriage.MarriageProposalOptions(TimeSpan.FromSeconds(proposalTimeoutSeconds)));
        services.TryAddSingleton<Dfo.Server.Abstractions.Marriage.IMarriageProposalRuleService, Dfo.Server.Application.Marriage.MarriageProposalRuleService>();
        services.TryAddSingleton<Dfo.Server.Application.Marriage.CoupleLedger>();
        services.TryAddSingleton<Dfo.Server.Application.Marriage.IMarriageRelations>(sp => sp.GetRequiredService<Dfo.Server.Application.Marriage.CoupleLedger>());
        services.TryAddSingleton<Dfo.Server.Application.Marriage.IMarriageRelationWriter>(sp => sp.GetRequiredService<Dfo.Server.Application.Marriage.CoupleLedger>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Microsoft.Extensions.Hosting.IHostedService, Dfo.Server.Application.Marriage.MarriageRelationAudit>());
        services.TryAddSingleton<Dfo.Server.Abstractions.Marriage.IMarriageCeremonyRuleService, Dfo.Server.Application.Marriage.MarriageCeremonyRuleService>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Marriage.IMarriageChurchRuleService, Dfo.Server.Application.Marriage.MarriageChurchRuleService>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Marriage.IMarriageLovePointRuleService, Dfo.Server.Application.Marriage.MarriageLovePointRuleService>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Marriage.IMarriageGuestbookRuleService, Dfo.Server.Application.Marriage.MarriageGuestbookRuleService>();
        services.TryAddSingleton<Dfo.Server.Application.Marriage.MarriageEligibility>();
        services.TryAddSingleton<Dfo.Server.Application.Marriage.ReservationDesk>();
        services.TryAddSingleton<Dfo.Server.Application.Marriage.ChurchRooms>();
        services.TryAddSingleton<Dfo.Server.Application.Marriage.WeddingSettlement>();
        services.TryAddSingleton<Dfo.Server.Application.Marriage.GuestList>();
        services.TryAddSingleton<Dfo.Server.Application.Marriage.Guestbook>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Marriage.IMarriageTrialRuleService, Dfo.Server.Application.Marriage.MarriageTrialRuleService>();
        services.TryAddSingleton<Dfo.Server.Application.Marriage.MarriageQuestDomainService>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Marriage.IMarriageQuestDomainService>(sp => sp.GetRequiredService<Dfo.Server.Application.Marriage.MarriageQuestDomainService>());
        services.TryAddSingleton<Dfo.Server.Application.Marriage.CapsuleState>();
        services.TryAddSingleton<Dfo.Server.Application.Marriage.GrowthCapsules>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Marriage.IMarriageCapsuleQueryDomainService>(sp => sp.GetRequiredService<Dfo.Server.Application.Marriage.CapsuleState>());
        services.TryAddSingleton<Dfo.Server.Abstractions.Marriage.IMarriageCapsuleDomainService>(sp => sp.GetRequiredService<Dfo.Server.Application.Marriage.MarriageDomainService>());
        services.AddDomainEvent<Dfo.Server.Abstractions.Marriage.MarriageCapsuleEnergyChanged>("marriage.capsule-energy", 1);
        services.AddDomainEvent<Dfo.Server.Abstractions.Marriage.MarriageCapsuleGiftChanged>("marriage.capsule-gift", 1);
        services.AddDomainSubscriber<DungeonMapCleared, Dfo.Server.Application.Marriage.CapsuleClearSubscriber>("marriage.capsule-clear", Dfo.Server.Abstractions.DomainEvents.DomainEventDeliveryPolicy.Reliable);
        services.TryAddSingleton<Dfo.Server.Application.Marriage.HousingState>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Marriage.IMarriageHousingSessionDomainService>(sp => sp.GetRequiredService<Dfo.Server.Application.Marriage.HousingState>());
        services.TryAddSingleton<Dfo.Server.Application.Marriage.HousingReadView>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Marriage.IMarriageHousingQueryDomainService>(sp => sp.GetRequiredService<Dfo.Server.Application.Marriage.HousingReadView>());
        services.TryAddSingleton<Dfo.Server.Application.Marriage.HousingOperations>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Marriage.IMarriageHousingDomainService>(sp => sp.GetRequiredService<Dfo.Server.Application.Marriage.MarriageDomainService>());
        services.TryAddSingleton<Dfo.Server.Application.Marriage.LovePoints>();
        services.TryAddSingleton<Dfo.Server.Application.Marriage.MarriageDomainService>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Marriage.IMarriageQueryDomainService>(sp => sp.GetRequiredService<Dfo.Server.Application.Marriage.MarriageDomainService>());
        services.TryAddSingleton<Dfo.Server.Abstractions.Marriage.IMarriageCoupleDomainService>(sp => sp.GetRequiredService<Dfo.Server.Application.Marriage.MarriageDomainService>());
        services.TryAddSingleton<Dfo.Server.Abstractions.Marriage.IMarriageVenueDomainService>(sp => sp.GetRequiredService<Dfo.Server.Application.Marriage.MarriageDomainService>());
        services.AddDomainSubscriber<DungeonMapCleared, Dfo.Server.Application.Marriage.MarriageTrialClearSubscriber>(
            "quest.wedding-trial", Dfo.Server.Abstractions.DomainEvents.DomainEventDeliveryPolicy.Reliable);
        return services;
    }
}
