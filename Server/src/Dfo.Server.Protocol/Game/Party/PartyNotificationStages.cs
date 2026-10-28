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

internal static class PartyNotificationStages
{
    internal static async ValueTask DeliverPartyNotificationsAsync(ICommandProjections projections, IPartyProjectionStateDomainService projectionState, IPartyService parties, IOutboundOrdering ordering, 
        PartyNotificationPlan plan,
        PartyNotificationRecipientPlan recipients,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(recipients);
        PartyNotificationStages.ValidatePlanShape(plan);
        var resolver = new PartyNotificationRecipientResolver(recipients);
        if (plan.Invitation is { } invitation)
        {
            var target = resolver.Characters([invitation.TargetCharacterId]);
            await PartyExactNotificationDelivery.PublishWithRetryAsync(
                projections,
                target,
                new PartyInviteIssued(invitation),
                cancellationToken).ConfigureAwait(false);
        }

        if (!PartyNotificationStages.TryGetPartyVersion(plan, out var generation, out var revision))
            return;

        var clearAudience = resolver.Characters(plan.ClearAudience);
        var stream = projectionState.StreamOf(generation);
        // A party command and its projection run in one CMD, and CMDs deliver in the order they
        // complete (RQ-032), so every stage of this projection is registered before any later
        // revision's. The stream's revision watermark still keeps an older snapshot from being
        // replayed over a newer one; a late older caller's recipient-specific clear or acceptance
        // acknowledgement must still be delivered.
        if (revision <= stream.LatestRevision)
        {
            var staleClearAudience = clearAudience
                .Where(session => !stream.CurrentMembers.Contains(session))
                .ToImmutableArray();
            var delivered = await PartyNotificationStages.DeliverStaleSideEffectsAsync(projections, 
                plan,
                staleClearAudience,
                stream.CurrentMembers,
                resolver,
                cancellationToken).ConfigureAwait(false);
            if (delivered)
                stream.FullStateMembers.ExceptWith(staleClearAudience);
            return;
        }
        var promoteSettingsUpdate = plan.SettingsChange == PartySettingsChangeKind.Updated
            && plan.Snapshot!.Members.Any(member =>
                resolver.Character(member.CharacterId) is not { } current
                || !stream.FullStateMembers.Contains(current));
        // XO-6: each stage's entries are delivered only after the stage before it; a stage is
        // Required, so a recipient whose delivery fails stops the later stages, as a stage with
        // a failure stops the advance here (TASK-23 ruling 5).
        bool stateDelivered;
        using (ordering.Begin("XO-6 party state stages", OutboundDelivery.Required))
        {
            stateDelivered = await PartyNotificationStages.DeliverPartyStateAsync(projections, parties, 
                plan, promoteSettingsUpdate, resolver, cancellationToken).ConfigureAwait(false);
        }
        if (!stateDelivered)
            return;

        stream.LatestRevision = revision;
        stream.CurrentPartyId = plan.Snapshot?.PartyId;
        if (plan.Snapshot is { } latestSnapshot)
        {
            stream.CurrentMembers.Clear();
            stream.CurrentMembers.UnionWith(
                resolver.Characters(latestSnapshot.Members.Select(member => member.CharacterId)));
        }
        else
        {
            stream.CurrentMembers.ExceptWith(clearAudience);
        }
        stream.FullStateMembers.ExceptWith(clearAudience);
        if (plan.Snapshot is not null
            && (plan.SettingsChange != PartySettingsChangeKind.Updated || promoteSettingsUpdate))
        {
            stream.FullStateMembers.UnionWith(
                resolver.Characters(plan.Snapshot.Members.Select(member => member.CharacterId)));
        }
    }

    internal static async ValueTask DeliverPartyRealtimeAsync(ICommandProjections projections, IPartyProjectionStateDomainService projectionState, 
        SelectedSessionIdentity requester,
        PartySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requester);
        ArgumentNullException.ThrowIfNull(snapshot);
        var stream = projectionState.StreamOf(snapshot.Generation);
        if (snapshot.Revision < stream.LatestRevision)
            return;
        await PartyExactNotificationDelivery.PublishWithRetryAsync(
            projections,
            [requester],
            new PartyRealtimeChanged(snapshot),
            cancellationToken).ConfigureAwait(false);
    }

    internal static async ValueTask<bool> DeliverPartyStateAsync(ICommandProjections projections, IPartyService parties, 
        PartyNotificationPlan plan,
        bool promoteSettingsUpdate,
        PartyNotificationRecipientResolver resolver,
        CancellationToken cancellationToken)
    {

        if (plan.SettingsChange == PartySettingsChangeKind.Updated && !promoteSettingsUpdate)
        {
            var settingsSnapshot = plan.Snapshot!;
            var settingsAudience = resolver.Characters(
                settingsSnapshot.Members.Select(value => value.CharacterId));
            var settingsDelivery = await PartyExactNotificationDelivery.PublishWithRetryAsync(
                projections,
                settingsAudience,
                new PartySettingsChanged(settingsSnapshot),
                cancellationToken).ConfigureAwait(false);
            return !settingsDelivery.HasFailures
                && await PartyNotificationStages.DeliverObserverStateAsync(projections, parties, plan, resolver, cancellationToken)
                    .ConfigureAwait(false);
        }

        if (plan.LeadershipTransfer is { } transfer)
        {
            var transferAudience = resolver.Characters(
                transfer.Members.Select(value => value.CharacterId));
            var transferDelivery = await PartyExactNotificationDelivery.PublishWithRetryAsync(
                projections, transferAudience,
                new PartyLeadershipChanged(transfer), cancellationToken).ConfigureAwait(false);
            return !transferDelivery.HasFailures
                && await PartyNotificationStages.DeliverObserverStateAsync(projections, parties, plan, resolver, cancellationToken)
                    .ConfigureAwait(false);
        }

        if (!await PartyNotificationStages.DeliverClearAsync(projections, 
            resolver.Characters(plan.ClearAudience),
            plan.ClearPartyId,
            cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        if (plan.Snapshot is not { } snapshot)
            return await PartyNotificationStages.DeliverObserverStateAsync(projections, parties, plan, resolver, cancellationToken)
                .ConfigureAwait(false);

        var memberAudience = resolver.Characters(
            snapshot.Members.Select(value => value.CharacterId));
        var realtimeDelivery = await PartyExactNotificationDelivery.PublishWithRetryAsync(
            projections,
            memberAudience,
            new PartyRealtimeChanged(snapshot),
            cancellationToken).ConfigureAwait(false);
        if (realtimeDelivery.HasFailures)
            return false;
        if (plan.Acceptance is { } acceptance)
        {
            if (!await PartyNotificationStages.DeliverAcceptanceAsync(projections, acceptance, resolver, cancellationToken)
                    .ConfigureAwait(false))
                return false;
        }
        var endpointsDelivery = await PartyExactNotificationDelivery.PublishWithRetryAsync(
            projections,
            memberAudience,
            new PartyEndpointsChanged(snapshot),
            cancellationToken).ConfigureAwait(false);
        if (endpointsDelivery.HasFailures)
            return false;
        var rosterDelivery = await PartyExactNotificationDelivery.PublishWithRetryAsync(
            projections,
            memberAudience,
            PartyRosterChanged.Refresh(
                snapshot,
                memberAudience.Select(value => value.CharacterId).ToImmutableArray()),
            cancellationToken).ConfigureAwait(false);
        if (rosterDelivery.HasFailures)
            return false;
        if (plan.Acceptance is not null)
        {
            // A21 drops the first realtime summary when it arrives before the party slots exist.
            // Replay the same immutable snapshot after the full roster so both clients can bind
            // the current HP without relying on an optional per-client 0x00A6 follow-up query.
            var replayDelivery = await PartyExactNotificationDelivery.PublishWithRetryAsync(
                projections,
                memberAudience,
                new PartyRealtimeChanged(snapshot),
                cancellationToken).ConfigureAwait(false);
            if (replayDelivery.HasFailures)
                return false;
        }

        return await PartyNotificationStages.DeliverObserverStateAsync(projections, parties, plan, resolver, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static async ValueTask<bool> DeliverObserverStateAsync(ICommandProjections projections, IPartyService parties, 
        PartyNotificationPlan plan,
        PartyNotificationRecipientResolver resolver,
        CancellationToken cancellationToken)
    {
        if (plan.DirectoryChange is not { } change)
            return true;

        if (plan.Snapshot is { } projected
            && projected.Members
                .Select(member => parties.FindParty(member.Character))
                .FirstOrDefault(current => !current.IsVirtual
                    && current.Generation == projected.Generation)
                is { } latest
            && latest.LeaderMember.Character.DirectoryScope != change.Current)
        {
            // The leader's scope is read from where the leader stands now. A plan that names
            // another scope was made before a later move or roster change, whose own projection
            // repairs the directory; publishing this one would put the party back where it was.
            return true;
        }

        var rekeyed = plan.Snapshot is { } currentSnapshot
            && plan.ClearPartyId is { } oldId
            && oldId != currentSnapshot.PartyId;
        var excluded = plan.Snapshot is { } snapshot
            ? snapshot.Members.Select(member => member.CharacterId).ToHashSet()
            : plan.ClearAudience.ToHashSet();
        if (rekeyed)
            excluded.UnionWith(plan.ClearAudience);
        var previousObservers = resolver.Scope(change.Previous)
            .Where(session => !excluded.Contains(session.CharacterId))
            .Distinct()
            .ToImmutableArray();
        var currentObservers = resolver.Scope(change.Current)
            .Where(session => !excluded.Contains(session.CharacterId))
            .Distinct()
            .ToImmutableArray();

        if (change.Previous is not null && (change.Previous != change.Current || rekeyed)
            && !previousObservers.IsEmpty)
        {
            var partyId = plan.ClearPartyId ?? plan.Snapshot?.PartyId
                ?? throw new InvalidOperationException("A moved party directory entry requires its party ID.");
            var clear = await PartyExactNotificationDelivery.PublishWithRetryAsync(
                projections,
                previousObservers,
                PartyRosterChanged.Clear(
                    previousObservers.Select(value => value.CharacterId).ToImmutableArray(),
                    partyId),
                cancellationToken).ConfigureAwait(false);
            if (clear.HasFailures)
                return false;
        }

        if (plan.Snapshot is { } refresh && !currentObservers.IsEmpty)
        {
            var delivery = await PartyExactNotificationDelivery.PublishWithRetryAsync(
                projections,
                currentObservers,
                PartyRosterChanged.Refresh(
                    refresh,
                    currentObservers.Select(value => value.CharacterId).ToImmutableArray()),
                cancellationToken).ConfigureAwait(false);
            return !delivery.HasFailures;
        }

        if (plan.Snapshot is null && change.Previous is not null
            && change.Previous == change.Current && !previousObservers.IsEmpty)
        {
            var partyId = plan.ClearPartyId
                ?? throw new InvalidOperationException("An observer party clear requires its party ID.");
            var delivery = await PartyExactNotificationDelivery.PublishWithRetryAsync(
                projections,
                previousObservers,
                PartyRosterChanged.Clear(
                    previousObservers.Select(value => value.CharacterId).ToImmutableArray(),
                    partyId),
                cancellationToken).ConfigureAwait(false);
            return !delivery.HasFailures;
        }

        return true;
    }

    internal static async ValueTask<bool> DeliverStaleSideEffectsAsync(ICommandProjections projections, 
        PartyNotificationPlan plan,
        ImmutableArray<SelectedSessionIdentity> staleClearAudience,
        HashSet<SelectedSessionIdentity> currentMembers,
        PartyNotificationRecipientResolver resolver,
        CancellationToken cancellationToken)
    {
        if (!await PartyNotificationStages.DeliverClearAsync(projections, 
            staleClearAudience,
            plan.ClearPartyId,
            cancellationToken).ConfigureAwait(false))
        {
            return false;
        }
        if (plan.Acceptance is { } acceptance
            && resolver.Character(acceptance.InviterCharacterId) is { } inviter
            && resolver.Character(acceptance.InviteeCharacterId) is { } invitee
            && currentMembers.Contains(inviter)
            && currentMembers.Contains(invitee))
        {
            return await PartyNotificationStages.DeliverAcceptanceAsync(projections, acceptance, resolver, cancellationToken)
                .ConfigureAwait(false);
        }

        return true;
    }

    internal static async ValueTask<bool> DeliverClearAsync(ICommandProjections projections, 
        ImmutableArray<SelectedSessionIdentity> clearAudience,
        ushort? clearPartyId,
        CancellationToken cancellationToken)
    {
        if (clearAudience.IsDefaultOrEmpty)
            return true;

        var partyId = clearPartyId
            ?? throw new InvalidOperationException("A party clear requires its party ID.");
        var delivery = await PartyExactNotificationDelivery.PublishWithRetryAsync(
            projections,
            clearAudience,
            PartyRosterChanged.Clear(
                clearAudience.Select(value => value.CharacterId).ToImmutableArray(),
                partyId),
            cancellationToken).ConfigureAwait(false);
        return !delivery.HasFailures;
    }

    internal static async ValueTask<bool> DeliverAcceptanceAsync(ICommandProjections projections, 
        PartyAcceptanceNotification acceptance,
        PartyNotificationRecipientResolver resolver,
        CancellationToken cancellationToken)
    {
        var inviter = resolver.Characters([acceptance.InviterCharacterId]);
        var delivery = await PartyExactNotificationDelivery.PublishWithRetryAsync(
            projections,
            inviter,
            new PartyInviteAccepted(acceptance),
            cancellationToken).ConfigureAwait(false);
        return !delivery.HasFailures;
    }

    internal static void ValidatePlanShape(PartyNotificationPlan plan)
    {
        if (plan.LeadershipTransfer is { } transfer
            && (plan.Snapshot is not { } final
                || transfer.PartyId != final.PartyId
                || transfer.Generation != final.Generation
                || transfer.Revision != final.Revision
                || transfer.LeaderWireUserId != final.LeaderWireUserId
                || !plan.ClearAudience.IsDefaultOrEmpty))
        {
            throw new InvalidOperationException(
                "A leadership transfer must be the final unchanged roster for the same party revision.");
        }

        var settingsChange = plan.SettingsChange;
        if (settingsChange is not null
            && settingsChange is not PartySettingsChangeKind.Created
                and not PartySettingsChangeKind.Updated)
        {
            throw new InvalidOperationException("A party plan contains an unknown settings change kind.");
        }

        var hasClearAudience = !plan.ClearAudience.IsDefaultOrEmpty;
        var hasDirectoryChange = plan.DirectoryChange is not null;
        var hasAllClearMetadata = plan.ClearPartyId is not null
            && plan.ClearPartyGeneration is not null
            && plan.ClearPartyRevision is not null;
        var hasAnyClearMetadata = plan.ClearPartyId is not null
            || plan.ClearPartyGeneration is not null
            || plan.ClearPartyRevision is not null;
        if ((hasClearAudience && !hasAllClearMetadata)
            || (!hasClearAudience && hasAnyClearMetadata))
        {
            throw new InvalidOperationException(
                "A party clear requires an audience, party ID, generation, and revision together.");
        }

        if (hasDirectoryChange && plan.Snapshot is null && !hasClearAudience)
        {
            throw new InvalidOperationException(
                "A directory change requires a party snapshot or a complete party clear.");
        }

        if (plan.Invitation is not null
            && (plan.Snapshot is not null
                || plan.Acceptance is not null
                || hasClearAudience
                || settingsChange is not null))
        {
            throw new InvalidOperationException("A party invitation plan cannot contain another operation.");
        }

        if (plan.Acceptance is { } acceptance
            && (plan.Snapshot is null
                || settingsChange is not null
                || (acceptance.JoiningCharacterId is { } joining
                    && (joining != acceptance.InviterCharacterId && joining != acceptance.InviteeCharacterId
                        || !plan.Snapshot.Members.Any(member => member.CharacterId == joining)))
                || (hasClearAudience
                    && (plan.ClearAudience.Length != 1
                        || plan.ClearAudience[0] != (acceptance.JoiningCharacterId ?? acceptance.InviteeCharacterId)))))
        {
            throw new InvalidOperationException(
                "A party acceptance requires its resulting snapshot and may clear only the joining peer.");
        }

        if (settingsChange is not null
            && (plan.Snapshot is null
                || plan.Invitation is not null
                || plan.Acceptance is not null
                || hasClearAudience))
        {
            throw new InvalidOperationException(
                "A party settings change requires only its resulting snapshot.");
        }
    }

    internal static bool TryGetPartyVersion(
        PartyNotificationPlan plan,
        out long generation,
        out long revision)
    {
        if (plan.Snapshot is { } snapshot)
        {
            generation = snapshot.Generation;
            revision = snapshot.Revision;
            if (!plan.ClearAudience.IsDefaultOrEmpty
                && (plan.ClearPartyGeneration != generation
                    || plan.ClearPartyRevision != revision))
            {
                throw new InvalidOperationException(
                    "A party clear and survivor refresh must describe one party revision.");
            }
            return true;
        }

        if (!plan.ClearAudience.IsDefaultOrEmpty)
        {
            generation = plan.ClearPartyGeneration
                ?? throw new InvalidOperationException("A party clear requires its generation.");
            revision = plan.ClearPartyRevision
                ?? throw new InvalidOperationException("A party clear requires its revision.");
            return true;
        }

        generation = 0;
        revision = 0;
        return false;
    }
}
