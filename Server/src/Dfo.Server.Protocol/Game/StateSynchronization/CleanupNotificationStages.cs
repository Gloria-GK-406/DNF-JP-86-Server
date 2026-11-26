using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.AntonRaid;
using Dfo.Server.Protocol.Game.GuildPresence;
using Dfo.Server.Protocol.Game.Party;

namespace Dfo.Server.Protocol.Game.StateSynchronization;

/// <summary>The shared delivery order for disconnect, return to selection and replacement cleanups.</summary>
internal static class CleanupNotificationStages
{
    internal static async ValueTask DeliverAsync(
        SelectedSessionCleanupNotificationPlan plan,
        IGameCommandOutbox outbox,
        ICommandProjections projections,
        IPartyProjectionStateDomainService projectionState,
        IPartyService parties,
        IOutboundOrdering ordering,
        AntonRaidSquadProjection raidSquads,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        // XO-5 / DR-10: a failed Required owner delivery skips the later BestEffort stages.
        using var order = ordering.Begin("XO-5 session cleanup",
            plan.RequiredOwnerNotices.Count > 0 ? OutboundDelivery.Required : OutboundDelivery.BestEffort);
        if (plan.RequiredOwnerNotices.Count > 0)
            await outbox.RegisterExplicitAsync(plan.OwnerConnectionId,
                new([.. plan.RequiredOwnerNotices.Select(CleanupNoticeMessages.ToMessage)], []), cancellationToken).ConfigureAwait(false);

        order.Delivery = OutboundDelivery.BestEffort;
        if (plan.PeerAudience.Count > 0 && plan.PeerNotices.Count > 0)
            await projections.ProjectAsync(new NotificationAudience.TheseSessions(plan.PeerAudience),
                new SessionCleanupAnnounced(plan.PeerNotices), cancellationToken).ConfigureAwait(false);

        // The party stage retains its own XO-6 order, revision watermark and sampled recipients.
        await PartyNotificationStages.DeliverPartyNotificationsAsync(projections, projectionState, parties, ordering,
            plan.PartyNotifications, plan.PartyRecipients, cancellationToken).ConfigureAwait(false);

        if (plan.RaidDeparture is { } raid)
        {
            await raidSquads.DeliverAsync(raid.Squads, cancellationToken).ConfigureAwait(false);
            await AntonRaidRecruitmentProjection.DeliverAsync(projections, raid, cancellationToken).ConfigureAwait(false);
        }

        foreach (var (audience, left) in ConversationDepartureNotices.For(plan))
            await projections.ProjectAsync(audience, left, cancellationToken).ConfigureAwait(false);
        foreach (var (audience, offline) in FriendDepartureNotices.For(plan))
            await projections.ProjectAsync(audience, offline, cancellationToken).ConfigureAwait(false);
        if (plan.GuildDeparture is { } departure)
            await projections.ProjectAsync(new NotificationAudience.TheseCharacters(departure.OtherMembers),
                new GuildMemberPresenceChanged(departure.Character, departure.Name, Channel: 0), cancellationToken)
                .ConfigureAwait(false);
    }
}
