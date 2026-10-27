using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.StateSynchronization;

internal sealed record ClientAppearanceVisibilityRefresh(SelectedSessionIdentity Owner)
    : INotification<ClientAppearanceVisibilityRefresh>;

internal sealed class ClientAppearanceSettingsSubscriber(IEventService events, ISelectedSessionDomainService sessions,
    ICharacterVisibleSessionDomainService visibility) : IDomainEventSubscriber<ClientAppearanceSettingsChanged>
{
    public async ValueTask HandleAsync(DomainEventEnvelope<ClientAppearanceSettingsChanged> message, CancellationToken ct)
    {
        if (!sessions.TryGetInWorld(message.Payload.CharacterId, out var owner) || owner.AccountId != message.Payload.AccountId) return;
        _ = await events.ProjectAsync(new NotificationAudience.TheseSessions(visibility.Resolve(owner)),
            new ClientAppearanceVisibilityRefresh(owner), ct).ConfigureAwait(false);
    }
}

internal sealed class ClientAppearanceVisibilityProjection(ICharacterAppearanceDomainService appearances,
    ISelectedSessionDomainService sessions, ICharacterVisibleSessionDomainService visibility)
    : INotificationHandler<ClientAppearanceVisibilityRefresh>
{
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        ClientAppearanceVisibilityRefresh notification, CancellationToken ct)
    {
        var owner = notification.Owner;
        if (!sessions.TryGetInWorld(recipient.CharacterId, out var receiver) || receiver.ConnectionId != recipient.ConnectionId
            || recipient.ExpectedSession is { } expected && receiver != expected
            || !visibility.Resolve(owner).Contains(receiver)) return [];
        var display = await appearances.ResolveAsync(owner.AccountId, owner.CharacterId, [], ct).ConfigureAwait(false);
        // Both the owner and recipient may move or reconnect while storage is awaited.
        if (!sessions.IsInWorld(receiver)
            || !visibility.Resolve(owner).Contains(receiver)) return [];
        return [new UserVisibilityMessage(owner.WireUserId, display.VisibilityBits)];
    }
}
