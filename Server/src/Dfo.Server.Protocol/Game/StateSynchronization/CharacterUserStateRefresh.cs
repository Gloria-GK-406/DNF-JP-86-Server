using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.StateSynchronization;

internal sealed record CharacterUserStateRefresh(SelectedSessionIdentity Owner) : INotification<CharacterUserStateRefresh>;

internal sealed class CharacterUserStateRefreshHandler(ISelectedSessionDomainService sessions,
    ISelectedCharacterCurrentViewDomainService views, ICharacterVisibleSessionDomainService visibility)
    : INotificationHandler<CharacterUserStateRefresh>
{
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        CharacterUserStateRefresh notification, CancellationToken ct)
    {
        var owner = notification.Owner;
        var receiver = sessions.FindByConnection(recipient.ConnectionId);
        if (receiver is null || receiver.CharacterId != recipient.CharacterId
            || recipient.ExpectedSession is { } expected && expected != receiver
            || sessions.Find(owner.AccountId) != owner || !visibility.Resolve(owner).Contains(receiver)) return [];
        var current = await views.TryLoadAsync(owner, ct).ConfigureAwait(false);
        if (current is null || sessions.FindByConnection(receiver.ConnectionId) != receiver
            || sessions.Find(owner.AccountId) != owner || !visibility.Resolve(owner).Contains(receiver)) return [];
        return CharacterUserStateProjection.Compose(current, receiver == owner);
    }
}
