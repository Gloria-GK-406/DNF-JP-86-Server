using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Execution;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.CharacterDeath;
using Dfo.Server.Protocol.Game.DeleteItem;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.FinishLoading;
using Dfo.Server.Protocol.Game.GroundPickup;
using Dfo.Server.Protocol.Game.IllusionTowerContinue;
using Dfo.Server.Protocol.Game.IllusionTowerInfo;
using Dfo.Server.Protocol.Game.IllusionTowerMap;
using Dfo.Server.Protocol.Game.IllusionTowerRanking;
using Dfo.Server.Protocol.Game.IllusionTowerReward;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Protocol.Game.Weakness;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace Dfo.Server.Protocol.Game.Party;

internal static class PartyTownProjection
{
    internal static async ValueTask PartyTownRefreshMovedPartyAsync(ISelectedSessionDomainService sessions, ITownAreaDomainService townAreas, ICommandProjections projections, IPartyProjectionStateDomainService projectionState, IPartyService parties, 
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

        var stream = projectionState.StreamOf(candidate.Generation);
        var current = parties.FindParty(character);
        if (current.IsVirtual
            || current.Generation != candidate.Generation
            || current.Revision < stream.LatestRevision
            || stream.CurrentPartyId is { } projectedId
                && projectedId != current.PartyId)
        {
            return;
        }

        var memberSessions = current.Members
            .Select(member => PartyTownProjection.PartyTownExactSession(sessions, townAreas, member.CharacterId))
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
                : PartyTownProjection.PartyTownSessionsInScope(sessions, townAreas, previousScope, mover.CharacterId)
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
                : PartyTownProjection.PartyTownSessionsInScope(sessions, townAreas, currentScope, mover.CharacterId)
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

    internal static SelectedSessionIdentity[] PartyTownSessionsInScope(ISelectedSessionDomainService sessions, ITownAreaDomainService townAreas, 
        PartyDirectoryScope scope,
        CharacterId excludingCharacterId) =>
        (townAreas.Find(new TownAreaKey(scope.ChannelId, scope.TownId, scope.AreaId, scope.Instance))
            ?.Members ?? [])
            .Where(value => value.SharesTownPresence
                && value.CharacterId != excludingCharacterId)
            .Select(value => PartyTownProjection.PartyTownExactSession(sessions, townAreas, value.CharacterId))
            .Where(value => value is not null)
            .Cast<SelectedSessionIdentity>()
            .Distinct()
            .ToArray();

    internal static async ValueTask PartyTownRefreshObservedPartiesAsync(ITownAreaDomainService townAreas, ICommandProjections projections, IPartyProjectionStateDomainService projectionState, IPartyService parties, 
        IReadOnlyList<SelectedSessionIdentity> recipients,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recipients);
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
            foreach (var candidate in parties.Query(query))
            {
                if (candidate.Members.Any(member => member.CharacterId == recipient.CharacterId))
                    continue;

                var stream = projectionState.StreamOf(candidate.Generation);
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
                    projections,
                    [recipient],
                    PartyRosterChanged.Refresh(
                        current,
                        ImmutableArray.Create(recipient.CharacterId)),
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    internal static SelectedSessionIdentity? PartyTownExactSession(ISelectedSessionDomainService sessions, ITownAreaDomainService townAreas, CharacterId characterId)
    {
        var member = townAreas.Find(characterId);
        return member is not null
            && sessions.TryGetInWorld(characterId, out var session)
            && session.Owns(member)
            ? session
            : null;
    }
}
