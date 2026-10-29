using System.Reflection;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Support;
// Controlled domain collaborators preserve the original projection oracles; the production timer tests cover scope/FIFO/registration.
internal sealed class TimerRefreshFixture : IDisposable
{
    private readonly ServiceProvider provider;
    private readonly SelectedSessionRefreshSource source;
    internal TimerRefreshFixture(SelectedSessionRefreshTopic topic, ISelectedSessionSnapshotQuery directory,
        ISelectedSessionDomainService sessions, IEventService? events, IDailyResetRuleService daily, TimeProvider clock,
        IGameplayQueue queue, IItemUsageDomainService? items = null, IGameMessageSender? sender = null)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddCommandDispatching();
        services.AddSingleton(queue); services.AddSingleton(clock); services.AddSingleton(sessions);
        var usage = DispatchProxy.Create<IPremiumServiceUsageDomainService, Empty>();
        services.AddSingleton<ISelectedSessionRefreshDomainService>(new SelectedSessionRefreshDomainService(directory, sessions, daily, usage, clock));
        services.AddSingleton(usage);
        services.AddSingleton(DispatchProxy.Create<IPremiumServiceDomainService, Empty>());
        services.AddSingleton(items ?? DispatchProxy.Create<IItemUsageDomainService, Empty>());
        services.AddSingleton(DispatchProxy.Create<ICharacterFatigueDomainService, Empty>());
        services.AddSingleton<ICommandProjections>(new Projections(events));
        services.AddSingleton<IGameCommandOutbox>(sender is null ? DispatchProxy.Create<IGameCommandOutbox, Empty>() : new GameOutboxFixture(sender));
        services.AddInternalCommandHandler<CaptureSelectedSessionRefreshCommand, SelectedSessionRefreshScan, SelectedSessionRefreshCommandHandler>();
        services.AddInternalCommandHandler<RefreshQuotaSessionCommand, bool, SelectedSessionRefreshCommandHandler>();
        services.AddInternalCommandHandler<RefreshItemUsageSessionCommand, bool, SelectedSessionRefreshCommandHandler>(TimerReadOnlyCancellationCapability.ItemUsageSnapshot);
        provider = services.AddSessionSafetyStandIns().BuildServiceProvider();
        var type = topic switch
        {
            SelectedSessionRefreshTopic.Altar => typeof(Dfo.Server.Protocol.Game.Altar.AltarQuotaDailyRefreshService),
            SelectedSessionRefreshTopic.Dimension => typeof(DimensionQuotaDailyRefreshService),
            SelectedSessionRefreshTopic.GoldSilverHorn => typeof(GoldSilverHornQuotaDailyRefreshService),
            SelectedSessionRefreshTopic.ReviveCoin => typeof(ReviveCoinQuotaDailyRefreshService),
            SelectedSessionRefreshTopic.Mirror => typeof(MirrorQuotaDailyRefreshService),
            SelectedSessionRefreshTopic.Heras => typeof(Dfo.Server.Protocol.Game.Tournament.HerasQuotaDailyRefreshService),
            SelectedSessionRefreshTopic.Tournament => typeof(Dfo.Server.Protocol.Game.Tournament.TournamentQuotaDailyRefreshService),
            SelectedSessionRefreshTopic.ItemUsage => typeof(ItemUsageDailyRefreshService),
            _ => throw new NotSupportedException(),
        };
        source = (SelectedSessionRefreshSource)ActivatorUtilities.CreateInstance(provider, type);
    }
    public Task RefreshAsync(CancellationToken token) => source.RefreshAsync(token);
    public void Dispose() { source.Dispose(); provider.Dispose(); }
    public class Empty : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new NotSupportedException(targetMethod?.Name);
    }
    private sealed class Projections(IEventService? events) : ICommandProjections
    {
        public ValueTask<NotificationDelivery> ProjectAsync<T>(NotificationAudience audience, T notification, CancellationToken token)
            where T : class, INotification<T> => (events ?? throw new NotSupportedException()).ProjectAsync(audience, notification, token);
    }
}
