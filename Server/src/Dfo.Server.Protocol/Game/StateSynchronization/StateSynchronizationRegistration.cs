using Dfo.Server.Application.Execution;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Protocol.Game.StateSynchronization;

/// <summary>What the StateSynchronization slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class StateSynchronizationRegistration
{
    internal static IServiceCollection AddStateSynchronizationSlice(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddInternalCommandHandler<CaptureSelectedSessionRefreshCommand, SelectedSessionRefreshScan, SelectedSessionRefreshCommandHandler>();
        services.AddInternalCommandHandler<RefreshQuotaSessionCommand, bool, SelectedSessionRefreshCommandHandler>();
        services.AddInternalCommandHandler<RefreshFatigueSessionCommand, bool, SelectedSessionRefreshCommandHandler>();
        services.AddInternalCommandHandler<RefreshPremiumSessionCommand, bool, SelectedSessionRefreshCommandHandler>(ReadOnlyActionCancellationCapability.ConfirmedRollbackRead);
        services.AddInternalCommandHandler<RefreshItemUsageSessionCommand, bool, SelectedSessionRefreshCommandHandler>(ReadOnlyActionCancellationCapability.ConfirmedRollbackRead);
        services.AddNotificationHandler<Dfo.Server.Abstractions.UsageQuotas.UsageQuotaViewChanged, UsageQuotaViewChangedHandler>();
        services.TryAddScoped<ClientAppearanceVisibilityProjection>();
        services.AddNotificationHandler<ClientAppearanceVisibilityRefresh, ClientAppearanceVisibilityProjection>();
        services.AddNotificationHandler<ExpiredItemsRefresh, ExpiredItemsRefreshHandler>();
        services.AddNotificationHandler<CharacterUserStateRefresh, CharacterUserStateRefreshHandler>();
        services.AddDomainSubscriber<OnlineExpiredItemsRemoved, ExpiredItemsNotificationSubscriber>(
            "inventory.expiration.notifications", DomainEventDeliveryPolicy.LiveOnly);
        services.AddDomainSubscriber<Dfo.Server.Abstractions.Accounts.ClientAppearanceSettingsChanged,
            ClientAppearanceSettingsSubscriber>("settings.appearance-visibility", Dfo.Server.Abstractions.DomainEvents.DomainEventDeliveryPolicy.LiveOnly,
            fact => $"{fact.AccountId.Value}:{fact.CharacterId.Value}");
        services.AddTypedGameplayTimer<LostLandCompletionMaintenanceService>();
        services.AddInternalCommandHandler<CaptureLostLandCompletionsCommand, IReadOnlyList<LostLandCompletion>, LostLandCompletionCommandHandler>();
        services.AddInternalCommandHandler<CompleteLostLandCommand, bool, LostLandCompletionCommandHandler>();
        services.AddTypedGameplayTimer<LostLandFeverMaintenanceService>();
        services.AddInternalCommandHandler<CaptureLostLandFeverExpiriesCommand, IReadOnlyList<LostLandFeverExpiry>, LostLandFeverCommandHandler>();
        services.AddInternalCommandHandler<ExpireLostLandFeverCommand, bool, LostLandFeverCommandHandler>();
        var lotteryCounterExperiment = new LotteryCounterExperimentOptions(
            configuration.GetValue<bool>("LotteryCounterExperiment:Enabled"),
            configuration.GetValue<byte>("LotteryCounterExperiment:Current", 0),
            configuration.GetValue<byte>("LotteryCounterExperiment:Threshold", 30));
        if (lotteryCounterExperiment.Current > 127 || lotteryCounterExperiment.Threshold > 127)
            throw new InvalidOperationException("LotteryCounterExperiment values must be in 0..127.");
        services.TryAddSingleton(lotteryCounterExperiment);
        services.TryAddSingleton<ICleanupNotificationDelivery, CleanupNotificationDelivery>();
        services.AddNotificationHandler<SessionCleanupAnnounced, SessionCleanupAnnouncedHandler>();
        services.AddGameCmdMessageDecoder<PremiumServiceUsageReportDecoder>(0x036F, ClientMessageType.GamePremiumServiceUsageReport);

        StateSynchronizationEncoderRegistration.AddStateSynchronizationEncoders(services);
        SupplementalSynchronizationEncoderRegistration.AddSupplementalSynchronizationEncoders(services);
        services.AddProtocolCommandHandler<GamePremiumServiceUsageReportCommand, bool, GamePremiumServiceUsageReportCommandHandler>(new ProtocolCommandMessageBinding(ClientMessageType.GamePremiumServiceUsageReport, typeof(PremiumServiceUsageReport)));
        services.RegisterProtocolMessageHandler<PremiumServiceUsageReportHandler, PremiumServiceUsageReport, GamePremiumServiceUsageReportCommand>(ClientMessageType.GamePremiumServiceUsageReport);
        return services;
    }
}
