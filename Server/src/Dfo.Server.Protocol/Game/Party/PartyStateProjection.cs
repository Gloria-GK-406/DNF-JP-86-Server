using Dfo.Server.Abstractions.Networking;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.Party;

// Parameter-only projection algorithm shared by the legacy adapter and protocol CMDs.
// It neither captures the current request nor resolves services or publishes domain facts.
internal sealed class PartyStateProjection(
    ICommandProjections notifications,
    Func<long, IPartyDeliveryStream> streamOf,
    IPartyService? parties,
    IOutboundOrdering? ordering)
{
    public async ValueTask DeliverAsync(
        PartyNotificationPlan plan,
        PartyNotificationRecipientPlan recipients,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(recipients);
        ValidatePlanShape(plan);
        var resolver = new PartyNotificationRecipientResolver(recipients);
        if (plan.Invitation is { } invitation)
        {
            var target = resolver.Characters([invitation.TargetCharacterId]);
            await PartyExactNotificationDelivery.PublishWithRetryAsync(
                notifications,
                target,
                new PartyInviteIssued(invitation),
                cancellationToken).ConfigureAwait(false);
        }

        if (!TryGetPartyVersion(plan, out var generation, out var revision))
            return;

        var clearAudience = resolver.Characters(plan.ClearAudience);
        var stream = streamOf(generation);
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
            var delivered = await DeliverStaleSideEffectsAsync(
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
        using (ordering?.Begin("XO-6 party state stages", OutboundDelivery.Required))
        {
            stateDelivered = await DeliverPartyStateAsync(
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

    /// <summary>
    /// Answers one member's explicit realtime query without broadcasting another refresh to the
    /// whole party. The shared generation stream keeps a delayed old query below a newer
    /// roster revision, while equal revisions remain answerable because HP can change independently
    /// of party membership. A requester-only answer never advances the full-mutation watermark.
    /// </summary>
    public async ValueTask DeliverRealtimeAsync(
        SelectedSessionIdentity requester,
        PartySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requester);
        ArgumentNullException.ThrowIfNull(snapshot);
        var stream = streamOf(snapshot.Generation);
        if (snapshot.Revision < stream.LatestRevision)
            return;
        await PartyExactNotificationDelivery.PublishWithRetryAsync(
            notifications,
            [requester],
            new PartyRealtimeChanged(snapshot),
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<bool> DeliverPartyStateAsync(
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
                notifications,
                settingsAudience,
                new PartySettingsChanged(settingsSnapshot),
                cancellationToken).ConfigureAwait(false);
            return !settingsDelivery.HasFailures
                && await DeliverObserverStateAsync(plan, resolver, cancellationToken)
                    .ConfigureAwait(false);
        }

        if (plan.LeadershipTransfer is { } transfer)
        {
            var transferAudience = resolver.Characters(
                transfer.Members.Select(value => value.CharacterId));
            var transferDelivery = await PartyExactNotificationDelivery.PublishWithRetryAsync(
                notifications, transferAudience,
                new PartyLeadershipChanged(transfer), cancellationToken).ConfigureAwait(false);
            return !transferDelivery.HasFailures
                && await DeliverObserverStateAsync(plan, resolver, cancellationToken)
                    .ConfigureAwait(false);
        }

        if (!await DeliverClearAsync(
            resolver.Characters(plan.ClearAudience),
            plan.ClearPartyId,
            cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        if (plan.Snapshot is not { } snapshot)
            return await DeliverObserverStateAsync(plan, resolver, cancellationToken)
                .ConfigureAwait(false);

        var memberAudience = resolver.Characters(
            snapshot.Members.Select(value => value.CharacterId));
        var realtimeDelivery = await PartyExactNotificationDelivery.PublishWithRetryAsync(
            notifications,
            memberAudience,
            new PartyRealtimeChanged(snapshot),
            cancellationToken).ConfigureAwait(false);
        if (realtimeDelivery.HasFailures)
            return false;
        if (plan.Acceptance is { } acceptance)
        {
            if (!await DeliverAcceptanceAsync(acceptance, resolver, cancellationToken)
                    .ConfigureAwait(false))
                return false;
        }
        var endpointsDelivery = await PartyExactNotificationDelivery.PublishWithRetryAsync(
            notifications,
            memberAudience,
            new PartyEndpointsChanged(snapshot),
            cancellationToken).ConfigureAwait(false);
        if (endpointsDelivery.HasFailures)
            return false;
        var rosterDelivery = await PartyExactNotificationDelivery.PublishWithRetryAsync(
            notifications,
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
                notifications,
                memberAudience,
                new PartyRealtimeChanged(snapshot),
                cancellationToken).ConfigureAwait(false);
            if (replayDelivery.HasFailures)
                return false;
        }

        return await DeliverObserverStateAsync(plan, resolver, cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask<bool> DeliverObserverStateAsync(
        PartyNotificationPlan plan,
        PartyNotificationRecipientResolver resolver,
        CancellationToken cancellationToken)
    {
        if (plan.DirectoryChange is not { } change)
            return true;

        if (plan.Snapshot is { } projected
            && parties is not null
            && projected.Members
                .Select(member => parties.FindParty(member.Character))
                .FirstOrDefault(current => !current.IsVirtual
                    && current.Generation == projected.Generation)
                is { } latest
            && latest.Revision > projected.Revision
            && latest.LeaderMember.Character.DirectoryScope != change.Current)
        {
            // Presence migration advances the Party revision even though the movement protocol
            // performs its own directory read-repair. Do not let a delayed pre-move plan publish
            // the old scope again after that newer revision is already authoritative.
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
                notifications,
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
                notifications,
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
                notifications,
                previousObservers,
                PartyRosterChanged.Clear(
                    previousObservers.Select(value => value.CharacterId).ToImmutableArray(),
                    partyId),
                cancellationToken).ConfigureAwait(false);
            return !delivery.HasFailures;
        }

        return true;
    }

    private async ValueTask<bool> DeliverStaleSideEffectsAsync(
        PartyNotificationPlan plan,
        ImmutableArray<SelectedSessionIdentity> staleClearAudience,
        HashSet<SelectedSessionIdentity> currentMembers,
        PartyNotificationRecipientResolver resolver,
        CancellationToken cancellationToken)
    {
        if (!await DeliverClearAsync(
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
            return await DeliverAcceptanceAsync(acceptance, resolver, cancellationToken)
                .ConfigureAwait(false);
        }

        return true;
    }

    private async ValueTask<bool> DeliverClearAsync(
        ImmutableArray<SelectedSessionIdentity> clearAudience,
        ushort? clearPartyId,
        CancellationToken cancellationToken)
    {
        if (clearAudience.IsDefaultOrEmpty)
            return true;

        var partyId = clearPartyId
            ?? throw new InvalidOperationException("A party clear requires its party ID.");
        var delivery = await PartyExactNotificationDelivery.PublishWithRetryAsync(
            notifications,
            clearAudience,
            PartyRosterChanged.Clear(
                clearAudience.Select(value => value.CharacterId).ToImmutableArray(),
                partyId),
            cancellationToken).ConfigureAwait(false);
        return !delivery.HasFailures;
    }

    private async ValueTask<bool> DeliverAcceptanceAsync(
        PartyAcceptanceNotification acceptance,
        PartyNotificationRecipientResolver resolver,
        CancellationToken cancellationToken)
    {
        var inviter = resolver.Characters([acceptance.InviterCharacterId]);
        var delivery = await PartyExactNotificationDelivery.PublishWithRetryAsync(
            notifications,
            inviter,
            new PartyInviteAccepted(acceptance),
            cancellationToken).ConfigureAwait(false);
        return !delivery.HasFailures;
    }

    private static void ValidatePlanShape(PartyNotificationPlan plan)
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

    private static bool TryGetPartyVersion(
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
