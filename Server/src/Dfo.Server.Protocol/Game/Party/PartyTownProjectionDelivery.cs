using System.Collections.Immutable;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.Party;

public interface IPartyTownProjectionDelivery
{
    ValueTask RefreshObservedPartiesAsync(
        IReadOnlyList<SelectedSessionIdentity> recipients,
        IReadOnlyList<SelectedSessionIdentity> representedSessions,
        CancellationToken cancellationToken);
}

public sealed class PartyTownProjectionDelivery(
    IPartyDomainService parties,
    INotificationBus notifications,
    PartyProjectionStreamRegistry streams) : IPartyTownProjectionDelivery
{
    public async ValueTask RefreshObservedPartiesAsync(
        IReadOnlyList<SelectedSessionIdentity> recipients,
        IReadOnlyList<SelectedSessionIdentity> representedSessions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recipients);
        ArgumentNullException.ThrowIfNull(representedSessions);
        cancellationToken.ThrowIfCancellationRequested();

        var exactRecipients = recipients.Distinct().ToArray();
        if (exactRecipients.Length == 0)
            return;

        using var participantLease = await streams.EnterParticipantsAsync(
            representedSessions, cancellationToken).ConfigureAwait(false);
        var candidates = representedSessions
            .Distinct()
            .Select(session => new PartyCandidate(session, parties.FindParty(session)))
            .Where(candidate => candidate.Snapshot is not null)
            .GroupBy(candidate => new PartyKey(
                candidate.Snapshot!.PartyId,
                candidate.Snapshot.Generation))
            .OrderBy(group => group.Key.PartyId)
            .ThenBy(group => group.Key.Generation)
            .ToArray();

        foreach (var candidate in candidates)
        {
            var stream = streams.GetOrAdd(candidate.Key.Generation);
            await stream.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var current = candidate
                    .Select(value => parties.FindParty(value.Session))
                    .Where(snapshot => snapshot is not null
                        && snapshot.PartyId == candidate.Key.PartyId
                        && snapshot.Generation == candidate.Key.Generation)
                    .OrderByDescending(snapshot => snapshot!.Revision)
                    .FirstOrDefault();
                if (current is null || current.Revision < stream.LatestRevision)
                    continue;

                var currentMembers = current.Members
                    .Select(member => member.Session)
                    .ToHashSet();
                var observers = exactRecipients
                    .Where(recipient => !currentMembers.Contains(recipient))
                    .ToImmutableArray();
                if (observers.IsEmpty)
                    continue;

                await PartyExactNotificationDelivery.PublishWithRetryAsync(
                    notifications,
                    observers,
                    PartyRosterChanged.Refresh(current, observers),
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                stream.Gate.Release();
            }
        }
    }

    private sealed record PartyCandidate(
        SelectedSessionIdentity Session,
        PartySnapshot? Snapshot);

    private readonly record struct PartyKey(ushort PartyId, long Generation);

}

internal readonly record struct PartyExactNotificationDeliveryResult(
    ImmutableArray<SelectedSessionIdentity> Delivered,
    bool HasFailures);

internal static class PartyExactNotificationDelivery
{
    // Party handlers deliberately have no detached NotificationBus retry. The causal owner retries
    // only exact recipients that failed, so a town 0x0002 retry can still be followed by its 0x0009
    // and a successful recipient is never replayed merely because its neighbour failed.
    private const int MaximumAttempts = 2;

    public static async ValueTask<PartyExactNotificationDeliveryResult> PublishWithRetryAsync<
        TNotification>(
        INotificationBus notifications,
        IEnumerable<SelectedSessionIdentity> intendedAudience,
        TNotification notification,
        CancellationToken cancellationToken)
        where TNotification : class, INotification<TNotification>
    {
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(intendedAudience);
        ArgumentNullException.ThrowIfNull(notification);

        var exactAudience = intendedAudience
            .Distinct()
            .ToArray();
        var pending = exactAudience;
        var delivered = new HashSet<SelectedSessionIdentity>();

        for (var attempt = 0; attempt < MaximumAttempts && pending.Length > 0; attempt++)
        {
            var result = await notifications.PublishAsync(
                new NotificationAudience.TheseSessions(pending),
                notification,
                cancellationToken).ConfigureAwait(false);
            var deliveredCharacters = result.With(NotificationOutcome.Delivered).ToHashSet();
            foreach (var session in pending.Where(
                         session => deliveredCharacters.Contains(session.CharacterId)))
            {
                delivered.Add(session);
            }

            var failedCharacters = result.With(NotificationOutcome.Failed).ToHashSet();
            pending = pending
                .Where(session => failedCharacters.Contains(session.CharacterId))
                .ToArray();
        }

        return new PartyExactNotificationDeliveryResult(
            exactAudience.Where(delivered.Contains).ToImmutableArray(),
            pending.Length > 0);
    }
}
