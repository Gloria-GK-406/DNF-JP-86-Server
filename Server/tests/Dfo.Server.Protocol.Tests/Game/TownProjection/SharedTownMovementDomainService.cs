using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.TownProjection;

namespace Dfo.Server.Protocol.Tests.Game.TownProjection;

internal sealed class SharedTownMovementDomainService : ITownMovementDomainService
{
    public static SharedTownMovementDomainService Instance { get; } = new();

    public ValueTask<bool> DeclaresAreaAsync(
        int townId, int areaId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(true);

    public ValueTask<bool> SharesTownPresenceAsync(
        int townId, int areaId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(true);

    public ValueTask<bool> CanEnterTownAsync(
        ChannelEntryIdentity channel, int townId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(true);
}

internal static class TownAreaProjectionTestFactory
{
    public static TownAreaProjectionDelivery Create(
        ISelectedSessionDomainService sessions,
        ISelectedCharacterCurrentViewDomainService currentViews,
        IGameMessageSender sender,
        ITownAreaDomainService? areas = null) =>
        new(
            areas ?? new SessionPresenceDomainService(),
            sessions,
            currentViews,
            new NoopNotificationBus(),
            new NoopPartyTownProjection(),
            sender,
            SharedTownMovementDomainService.Instance);

    private sealed class NoopNotificationBus : INotificationBus
    {
        public ValueTask<NotificationDelivery> PublishAsync<TNotification>(
            NotificationAudience audience,
            TNotification notification,
            CancellationToken cancellationToken)
            where TNotification : class, INotification<TNotification> =>
            ValueTask.FromResult(new NotificationDelivery([]));
    }

    private sealed class NoopPartyTownProjection : IPartyTownProjectionDelivery
    {
        public ValueTask RefreshObservedPartiesAsync(
            IReadOnlyList<SelectedSessionIdentity> recipients,
            IReadOnlyList<SelectedSessionIdentity> representedSessions,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask RefreshMovedPartyAsync(
            SelectedSessionIdentity mover,
            PartyDirectoryScope? previousScope,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
