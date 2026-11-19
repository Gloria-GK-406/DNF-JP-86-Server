using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.Raids;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Party;

namespace Dfo.Server.Protocol.Game.AntonRaid;

internal sealed class AntonRaidSquadProjection(PartyClientIdRegistry numbers,
    ICommandProjections projections, IPartyProjectionStateDomainService projectionState,
    IPartyService parties, IOutboundOrdering ordering, ISelectedSessionDomainService sessions, ITownAreaDomainService towns)
{
    internal async ValueTask DeliverAsync(IReadOnlyList<PartyNotificationPlan> changes, CancellationToken ct)
    {
        foreach (var change in changes)
        {
            var projected = numbers.Project(change);
            await PartyNotificationStages.DeliverPartyNotificationsAsync(projections, projectionState, parties, ordering,
                projected, PartyNotificationRecipientCapture.Capture(projected, sessions, towns), ct).ConfigureAwait(false);
        }
    }

    internal async ValueTask DeliverChangesAsync(IReadOnlyList<AntonRaidRecruitmentChange> changes, CancellationToken ct)
    {
        foreach (var change in changes)
        {
            await DeliverAsync(change.Squads, ct).ConfigureAwait(false);
            await AntonRaidRecruitmentProjection.DeliverAsync(projections, change, ct).ConfigureAwait(false);
        }
    }
}

internal sealed record AntonRaidRuntimeChanged(IReadOnlyList<IGameServerMessage> Messages)
    : INotification<AntonRaidRuntimeChanged>;

internal sealed class AntonRaidRuntimeChangedHandler : INotificationHandler<AntonRaidRuntimeChanged>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient, AntonRaidRuntimeChanged notice, CancellationToken ct) =>
        ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(notice.Messages);
}

internal static class AntonRaidRuntimeProjection
{
    internal static async ValueTask SendAsync(ICommandProjections projections, IReadOnlyList<SelectedSessionIdentity> audience,
        IReadOnlyList<IGameServerMessage> messages, CancellationToken ct)
    {
        if (audience.Count != 0 && messages.Count != 0)
            await projections.ProjectAsync(new NotificationAudience.TheseSessions(audience),
                new AntonRaidRuntimeChanged(messages), ct).ConfigureAwait(false);
    }
}
