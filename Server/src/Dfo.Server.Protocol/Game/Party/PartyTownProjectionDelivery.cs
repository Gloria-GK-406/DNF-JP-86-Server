using System.Collections.Immutable;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
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
    IPartyService parties,
    ISessionPresenceDomainService presence,
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
            representedSessions.Concat(exactRecipients), cancellationToken).ConfigureAwait(false);
        foreach (var recipient in exactRecipients)
        {
            var currentPresence = presence.Find(recipient.CharacterId);
            if (currentPresence?.Session != recipient
                || !currentPresence.StandingInTown
                || !currentPresence.SharesTownPresence
                || currentPresence.ChannelId <= 0)
            {
                continue;
            }

            var query = new PartyDirectoryQuery(new PartyDirectoryScope(
                currentPresence.ChannelId,
                currentPresence.TownId,
                currentPresence.AreaId));
            foreach (var candidate in parties.Query(query))
            {
                if (candidate.Members.Any(member => member.CharacterId == recipient.CharacterId))
                    continue;

                var stream = streams.GetOrAdd(candidate.Generation);
                await stream.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var current = parties.Query(query).SingleOrDefault(snapshot =>
                        snapshot.PartyId == candidate.PartyId
                        && snapshot.Generation == candidate.Generation);
                    if (current is null
                        || current.Revision < stream.LatestRevision
                        || current.Members.Any(member =>
                            member.CharacterId == recipient.CharacterId))
                    {
                        continue;
                    }

                    await PartyExactNotificationDelivery.PublishWithRetryAsync(
                        notifications,
                        [recipient],
                        PartyRosterChanged.Refresh(
                            current,
                            ImmutableArray.Create(recipient.CharacterId)),
                        cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    stream.Gate.Release();
                }
            }
        }
    }
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
