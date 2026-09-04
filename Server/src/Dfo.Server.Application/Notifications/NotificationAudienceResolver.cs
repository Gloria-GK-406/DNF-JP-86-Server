using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Characters;

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
internal sealed class NotificationAudienceResolver(ISelectedSessionDomainService sessions)
    : INotificationAudienceResolver
{
    public IReadOnlyList<NotificationRecipient> Resolve(NotificationAudience audience)
    {
        ArgumentNullException.ThrowIfNull(audience);
        return audience switch
        {
            NotificationAudience.TheseCharacters these => Reachable(these.CharacterIds),
            // Not unreachable: it is what a shape added above without an arm here arrives at, and
            // saying so beats delivering an empty audience that looks like nobody was online.
            _ => throw new InvalidOperationException(
                $"Notification audience '{audience.GetType().Name}' has no resolution."),
        };
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
