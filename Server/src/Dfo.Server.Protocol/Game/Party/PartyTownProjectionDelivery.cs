using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.Party;

public interface IPartyTownProjectionDelivery
{
    ValueTask RefreshObservedPartiesAsync(
        IReadOnlyList<SelectedSessionIdentity> recipients,
        IReadOnlyList<SelectedSessionIdentity> representedSessions,
        CancellationToken cancellationToken);

    ValueTask RefreshMovedPartyAsync(
        SelectedSessionIdentity mover,
        PartyDirectoryScope? previousScope,
        CancellationToken cancellationToken);
}

public sealed class PartyTownProjectionDelivery(
    IPartyService parties,
    ITownAreaDomainService townAreas,
    ISelectedSessionDomainService selectedSessions,
    IEventService notifications,
    PartyProjectionStreamRegistry streams) : IPartyTownProjectionDelivery
{
    public async ValueTask RefreshMovedPartyAsync(
        SelectedSessionIdentity mover,
        PartyDirectoryScope? previousScope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mover);
        cancellationToken.ThrowIfCancellationRequested();

        var character = new PartyCharacterSnapshot(
            mover.AccountId, mover.CharacterId, mover.WireUserId, null);
        var candidate = parties.FindParty(character);
        if (candidate.IsVirtual)
            return;

        var memberSessions = candidate.Members
            .Select(member => ExactSession(member.CharacterId))
            .Where(session => session is not null)
            .Cast<SelectedSessionIdentity>()
            .Distinct()
            .ToArray();
        var previousObservers = previousScope is null
            ? []
            : SessionsInScope(previousScope, mover.CharacterId);
        var candidateScope = candidate.LeaderMember.Character.DirectoryScope;
        var currentObservers = candidateScope is null
            ? []
            : SessionsInScope(candidateScope, mover.CharacterId);
        using var participantLease = await streams.EnterParticipantsAsync(
            memberSessions.Concat(previousObservers).Concat(currentObservers).Append(mover),
            cancellationToken)
            .ConfigureAwait(false);
        var stream = streams.GetOrAdd(candidate.Generation);
        GameplayGateAssertion.Enter(stream.Gate, nameof(PartyDeliveryStream), cancellationToken);
        try
        {
            var current = parties.FindParty(character);
            if (current.IsVirtual
                || current.Generation != candidate.Generation
                || current.Revision < stream.LatestRevision
                || stream.CurrentPartyId is { } projectedId
                    && projectedId != current.PartyId)
            {
                return;
            }

            memberSessions = current.Members
                .Select(member => ExactSession(member.CharacterId))
                .Where(session => session is not null)
                .Cast<SelectedSessionIdentity>()
                .Distinct()
                .ToArray();
            var members = memberSessions.Select(value => value.CharacterId).ToImmutableArray();
            var refresh = await PartyExactNotificationDelivery.PublishWithRetryAsync(
                notifications,
                memberSessions,
                PartyRosterChanged.Refresh(current, members),
                cancellationToken).ConfigureAwait(false);
            if (refresh.HasFailures)
                return;

            var leaderMovedDirectory = current.LeaderWireUserId == mover.WireUserId
                && current.LeaderMember.Character.DirectoryScope != previousScope;
            if (leaderMovedDirectory)
            {
                var memberIds = current.Members.Select(value => value.CharacterId).ToHashSet();
                var oldObservers = previousScope is null
                    ? []
                    : SessionsInScope(previousScope, mover.CharacterId)
                    .Where(value => !memberIds.Contains(value.CharacterId))
                    .Distinct()
                    .ToArray();
                if (oldObservers.Length > 0)
                {
                    var clear = await PartyExactNotificationDelivery.PublishWithRetryAsync(
                        notifications,
                        oldObservers,
                        PartyRosterChanged.Clear(
                            oldObservers.Select(value => value.CharacterId).ToImmutableArray(),
                            current.PartyId),
                        cancellationToken).ConfigureAwait(false);
                    if (clear.HasFailures)
                        return;
                }

                var currentScope = current.LeaderMember.Character.DirectoryScope;
                var newObservers = currentScope is null
                    ? []
                    : SessionsInScope(currentScope, mover.CharacterId)
                        .Where(value => !memberIds.Contains(value.CharacterId))
                        .Distinct()
                        .ToArray();
                if (newObservers.Length > 0)
                {
                    var observerRefresh = await PartyExactNotificationDelivery.PublishWithRetryAsync(
                        notifications,
                        newObservers,
                        PartyRosterChanged.Refresh(
                            current,
                            newObservers.Select(value => value.CharacterId).ToImmutableArray()),
                        cancellationToken).ConfigureAwait(false);
                    if (observerRefresh.HasFailures)
                        return;
                }
            }

            stream.LatestRevision = current.Revision;
            stream.CurrentPartyId = current.PartyId;
            stream.CurrentMembers.Clear();
            stream.CurrentMembers.UnionWith(memberSessions);
        }
        finally
        {
            stream.Gate.Release();
        }
    }

    private SelectedSessionIdentity[] SessionsInScope(
        PartyDirectoryScope scope,
        CharacterId excludingCharacterId) =>
        (townAreas.Find(new TownAreaKey(scope.ChannelId, scope.TownId, scope.AreaId, scope.Instance))
            ?.Members ?? [])
            .Where(value => value.SharesTownPresence
                && value.CharacterId != excludingCharacterId)
            .Select(value => ExactSession(value.CharacterId))
            .Where(value => value is not null)
            .Cast<SelectedSessionIdentity>()
            .Distinct()
            .ToArray();

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
            var currentPresence = townAreas.Find(recipient.CharacterId);
            if (currentPresence is null
                || !recipient.Owns(currentPresence)
                || !currentPresence.SharesTownPresence)
            {
                continue;
            }

            var query = new PartyDirectoryQuery(new PartyDirectoryScope(
                currentPresence.Area.ChannelId,
                currentPresence.Area.TownId,
                currentPresence.Area.AreaId, currentPresence.Area.Instance));
            foreach (var candidate in parties.Query(query))
            {
                if (candidate.Members.Any(member => member.CharacterId == recipient.CharacterId))
                    continue;

                var stream = streams.GetOrAdd(candidate.Generation);
                GameplayGateAssertion.Enter(stream.Gate, nameof(PartyDeliveryStream), cancellationToken);
                try
                {
                    var current = parties.Query(query).SingleOrDefault(snapshot =>
                        snapshot.PartyId == candidate.PartyId
                        && snapshot.Generation == candidate.Generation);
                    if (current is null
                        || current.Revision < stream.LatestRevision
                        || stream.CurrentPartyId is { } projectedId
                            && projectedId != current.PartyId
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

    private SelectedSessionIdentity? ExactSession(CharacterId characterId)
    {
        var member = townAreas.Find(characterId);
        return member is not null
            && selectedSessions.TryGetInWorld(characterId, out var session)
            && session.Owns(member)
            ? session
            : null;
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
        IEventService notifications,
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
            var result = await notifications.ProjectAsync(
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
