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
    ClientAppearanceVisibilityProjection projection) : IDomainEventSubscriber<ClientAppearanceSettingsChanged>
{
    public async ValueTask HandleAsync(DomainEventEnvelope<ClientAppearanceSettingsChanged> message, CancellationToken ct)
    {
        var owner = sessions.FindByCharacter(message.Payload.CharacterId);
        if (owner is null || owner.AccountId != message.Payload.AccountId) return;
        _ = await events.ProjectAsync(new NotificationAudience.TheseSessions(projection.Recipients(owner)),
            new ClientAppearanceVisibilityRefresh(owner), ct).ConfigureAwait(false);
    }
}

internal sealed class ClientAppearanceVisibilityProjection(IClientSettingsDomainService settings,
    ICharacterSettingsDomainService characters, ISelectedSessionDomainService sessions,
    ITownAreaDomainService towns, IDungeonRunDomainService runs, IIllusionTowerDomainService? towers = null)
    : INotificationHandler<ClientAppearanceVisibilityRefresh>
{
    internal IReadOnlyList<SelectedSessionIdentity> Recipients(SelectedSessionIdentity owner)
    {
        if (sessions.FindByCharacter(owner.CharacterId) != owner
            || sessions.FindByConnection(owner.ConnectionId) != owner) return [];
        var result = new HashSet<SelectedSessionIdentity> { owner };
        if (towers?.Find(owner) is { } tower)
        {
            foreach (var member in tower.Members)
                if (sessions.FindByCharacter(member.Session.CharacterId) == member.Session
                    && towers.Find(member.Session)?.RunId == tower.RunId) result.Add(member.Session);
        }
        else if (runs.Find(owner.CharacterId) is { } run)
        {
            foreach (var member in run.Participants)
                if (sessions.FindByCharacter(member.CharacterId) is { } peer
                    && runs.Find(member.CharacterId)?.RunId == run.RunId) result.Add(peer);
        }
        else if (towns.Find(owner.CharacterId) is { } own && owner.Owns(own)
            && towns.Find(own.Area) is { } area)
        {
            foreach (var member in area.VisibleTo(owner.CharacterId))
                if (sessions.FindByCharacter(member.CharacterId) is { } peer && peer.Owns(member)) result.Add(peer);
        }
        return result.ToArray();
    }

    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        ClientAppearanceVisibilityRefresh notification, CancellationToken ct)
    {
        var owner = notification.Owner;
        var receiver = sessions.FindByCharacter(recipient.CharacterId);
        if (receiver is null || receiver.ConnectionId != recipient.ConnectionId
            || recipient.ExpectedSession is { } expected && receiver != expected
            || !Recipients(owner).Contains(receiver)) return [];
        var legacy = await characters.FindAsync(owner.CharacterId, ct).ConfigureAwait(false);
        if (legacy is null) return [];
        var options = await settings.ReadAppearanceAsync(owner.AccountId, checked((byte)legacy.VisibilityBits), ct).ConfigureAwait(false);
        // Both the owner and recipient may move or reconnect while storage is awaited.
        if (sessions.FindByCharacter(receiver.CharacterId) != receiver
            || sessions.FindByConnection(receiver.ConnectionId) != receiver
            || !Recipients(owner).Contains(receiver)) return [];
        return [new UserVisibilityMessage(owner.WireUserId, options.VisibilityBits)];
    }
}
