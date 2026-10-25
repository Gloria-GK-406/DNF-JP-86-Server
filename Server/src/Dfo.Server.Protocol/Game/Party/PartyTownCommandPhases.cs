using Dfo.Server.Abstractions.Networking;
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

internal abstract partial class PartyMutationProtocolCommandHandler
{
    protected virtual async ValueTask PartyTownRefreshMovedPartyAsync(
        SelectedSessionIdentity mover,
        PartyDirectoryScope? previousScope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mover);
        cancellationToken.ThrowIfCancellationRequested();

        var character = new PartyCharacterSnapshot(
            mover.AccountId, mover.CharacterId, mover.WireUserId, null);
        var candidate = PartyService!.FindParty(character);
        if (candidate.IsVirtual)
            return;

        var stream = streams(candidate.Generation);
        var current = PartyService!.FindParty(character);
        if (current.IsVirtual
            || current.Generation != candidate.Generation
            || current.Revision < stream.LatestRevision
            || stream.CurrentPartyId is { } projectedId
                && projectedId != current.PartyId)
        {
            return;
        }

        var memberSessions = current.Members
            .Select(member => PartyTownExactSession(member.CharacterId))
            .Where(session => session is not null)
            .Cast<SelectedSessionIdentity>()
            .Distinct()
            .ToArray();
        var members = memberSessions.Select(value => value.CharacterId).ToImmutableArray();
        var refresh = await PartyExactNotificationDelivery.PublishWithRetryAsync(
            projections,
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
                : PartyTownSessionsInScope(previousScope, mover.CharacterId)
                .Where(value => !memberIds.Contains(value.CharacterId))
                .Distinct()
                .ToArray();
            if (oldObservers.Length > 0)
            {
                var clear = await PartyExactNotificationDelivery.PublishWithRetryAsync(
                    projections,
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
                : PartyTownSessionsInScope(currentScope, mover.CharacterId)
                    .Where(value => !memberIds.Contains(value.CharacterId))
                    .Distinct()
                    .ToArray();
            if (newObservers.Length > 0)
            {
                var observerRefresh = await PartyExactNotificationDelivery.PublishWithRetryAsync(
                    projections,
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

    protected SelectedSessionIdentity[] PartyTownSessionsInScope(
        PartyDirectoryScope scope,
        CharacterId excludingCharacterId) =>
        (townAreas.Find(new TownAreaKey(scope.ChannelId, scope.TownId, scope.AreaId, scope.Instance))
            ?.Members ?? [])
            .Where(value => value.SharesTownPresence
                && value.CharacterId != excludingCharacterId)
            .Select(value => PartyTownExactSession(value.CharacterId))
            .Where(value => value is not null)
            .Cast<SelectedSessionIdentity>()
            .Distinct()
            .ToArray();

    protected virtual async ValueTask PartyTownRefreshObservedPartiesAsync(
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
            foreach (var candidate in PartyService!.Query(query))
            {
                if (candidate.Members.Any(member => member.CharacterId == recipient.CharacterId))
                    continue;

                var stream = streams(candidate.Generation);
                var current = PartyService!.Query(query).SingleOrDefault(snapshot =>
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
                    projections,
                    [recipient],
                    PartyRosterChanged.Refresh(
                        current,
                        ImmutableArray.Create(recipient.CharacterId)),
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    protected SelectedSessionIdentity? PartyTownExactSession(CharacterId characterId)
    {
        var member = townAreas.Find(characterId);
        return member is not null
            && sessions.TryGetInWorld(characterId, out var session)
            && session.Owns(member)
            ? session
            : null;
    }

}
