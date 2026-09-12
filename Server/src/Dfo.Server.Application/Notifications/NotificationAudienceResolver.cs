using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Notifications;

/// <summary>
/// Answers an audience with the people who are here to receive it.
/// </summary>
/// <remarks>
/// A character nobody is playing right now is not an error and not a recipient. Telling somebody
/// something is best effort by construction here: the state they would have been told about is
/// already true, and they will see it when they next look.
///
/// The connection is read at resolution time rather than remembered, because the pair of
/// (character, connection) has a shorter life than either half of it.
/// </remarks>
internal sealed class NotificationAudienceResolver(
    ISelectedSessionDomainService sessions,
    ISessionPresenceDomainService? presence = null)
    : INotificationAudienceResolver
{
    private readonly ISessionPresenceDomainService? presence = presence;

    public IReadOnlyList<NotificationRecipient> Resolve(NotificationAudience audience)
    {
        ArgumentNullException.ThrowIfNull(audience);
        return audience switch
        {
            NotificationAudience.TheseCharacters these => Reachable(these.CharacterIds),
            NotificationAudience.TheseSessions these => Reachable(these.Sessions),
            NotificationAudience.Scoped scoped => Reachable(scoped.Scope),
            // Not unreachable: it is what a shape added above without an arm here arrives at, and
            // saying so beats delivering an empty audience that looks like nobody was online.
            _ => throw new InvalidOperationException(
                $"Notification audience '{audience.GetType().Name}' has no resolution."),
        };
    }

    private IReadOnlyList<NotificationRecipient> Reachable(SessionPresenceScope scope)
    {
        var directory = presence ?? throw new InvalidOperationException(
            "Scoped notification audiences require session presence.");
        var recipients = new List<NotificationRecipient>();
        foreach (var candidate in directory.Query(scope))
        {
            if (candidate.Session is not { } exact
                || sessions.FindByCharacter(candidate.CharacterId) is not { } current
                || current != exact)
            {
                continue;
            }
            recipients.Add(new NotificationRecipient(
                candidate.CharacterId,
                current.ConnectionId,
                current));
        }

        return recipients;
    }

    private IReadOnlyList<NotificationRecipient> Reachable(
        IReadOnlyList<SelectedSessionIdentity> exactSessions)
    {
        ArgumentNullException.ThrowIfNull(exactSessions);
        var recipients = new List<NotificationRecipient>(exactSessions.Count);
        foreach (var exact in exactSessions)
        {
            ArgumentNullException.ThrowIfNull(exact);
            if (sessions.FindByCharacter(exact.CharacterId) == exact)
            {
                recipients.Add(new NotificationRecipient(
                    exact.CharacterId,
                    exact.ConnectionId,
                    exact));
            }
        }

        return recipients;
    }

    private IReadOnlyList<NotificationRecipient> Reachable(IReadOnlyList<CharacterId> characterIds)
    {
        ArgumentNullException.ThrowIfNull(characterIds);
        var recipients = new List<NotificationRecipient>(characterIds.Count);
        foreach (var characterId in characterIds)
        {
            if (sessions.FindByCharacter(characterId) is { } session)
                recipients.Add(new NotificationRecipient(characterId, session.ConnectionId));
        }

        return recipients;
    }
}
