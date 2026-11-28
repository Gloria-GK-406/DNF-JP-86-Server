using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game.ExpertJobs;

internal sealed record ExpertStoreArrival(SelectedSessionIdentity Viewer) : INotification<ExpertStoreArrival>;
internal sealed record ExpertStoreOpened(ExpertStoreView View) : INotification<ExpertStoreOpened>;
internal sealed record ExpertStoreClosed(ExpertStoreSnapshot Store, bool IncludeOwner) : INotification<ExpertStoreClosed>;

internal static class ExpertStoreProjection
{
    internal static async ValueTask ArrivedAsync(ICommandProjections projections, SelectedSessionIdentity viewer, CancellationToken ct) =>
        _ = await projections.ProjectAsync(new NotificationAudience.TheseSessions([viewer]), new ExpertStoreArrival(viewer), ct).ConfigureAwait(false);

    internal static async ValueTask OpenedAsync(ISelectedSessionDomainService sessions, ITownAreaDomainService areas,
        ICommandProjections projections, ExpertStoreView view, CancellationToken ct)
    {
        var recipients = InArea(sessions, areas, view.Store.Area);
        if (recipients.Count > 0)
            _ = await projections.ProjectAsync(new NotificationAudience.TheseSessions(recipients), new ExpertStoreOpened(view), ct).ConfigureAwait(false);
    }

    internal static async ValueTask ClosedAsync(ISelectedSessionDomainService sessions, ITownAreaDomainService areas,
        ICommandProjections projections, ExpertStoreClosure closure, bool includeOwner, CancellationToken ct)
    {
        var owner = closure.Store.Owner;
        var recipients = InArea(sessions, areas, closure.Store.Area).Concat(closure.Visitors)
            .Where(session => session != owner && sessions.IsInWorld(session)).ToHashSet();
        if (includeOwner && sessions.IsInWorld(owner)) recipients.Add(owner);
        if (recipients.Count > 0)
            _ = await projections.ProjectAsync(new NotificationAudience.TheseSessions(recipients.ToArray()),
                new ExpertStoreClosed(closure.Store, includeOwner), ct).ConfigureAwait(false);
    }

    private static IReadOnlyList<SelectedSessionIdentity> InArea(ISelectedSessionDomainService sessions,
        ITownAreaDomainService areas, TownAreaKey key) =>
        areas.Find(key)?.Members.Where(member => member.SharesTownPresence && sessions.IsInWorld(member.Session))
            .Select(member => member.Session).ToArray() ?? [];
}

internal sealed class ExpertStoreArrivalHandler(IExpertStoreGameplayDomainService gameplay)
    : INotificationHandler<ExpertStoreArrival>
{
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient, ExpertStoreArrival notice, CancellationToken ct)
    {
        if (recipient.ExpectedSession != notice.Viewer) return [];
        return (await gameplay.VisibleToAsync(notice.Viewer, ct).ConfigureAwait(false))
            .Select(view => (IServerNetworkMessage)new ExpertStoreOpenedMessage(view)).ToArray();
    }
}

internal sealed class ExpertStoreOpenedHandler : INotificationHandler<ExpertStoreOpened>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient, ExpertStoreOpened notice, CancellationToken ct) =>
        ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([new ExpertStoreOpenedMessage(notice.View)]);
}

internal sealed class ExpertStoreClosedHandler(ISelectedCharacterCurrentViewDomainService views,
    ISelectedSessionDomainService sessions, ITownAreaDomainService areas) : INotificationHandler<ExpertStoreClosed>
{
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient, ExpertStoreClosed notice, CancellationToken ct)
    {
        var store = notice.Store;
        if (recipient.ExpectedSession is not { } exact || !sessions.IsInWorld(exact)) return [];
        var close = new ExpertStoreClosedMessage(store.Owner.WireUserId);
        if (exact != store.Owner) return [close];
        if (!notice.IncludeOwner) return [];
        if (areas.Find(exact.CharacterId)?.Area != store.Area || await views.TryLoadAsync(exact, ct).ConfigureAwait(false) is not { } view)
            return [close];
        var equipment = view.CharacterInventory.Containers.SingleOrDefault(c => c.ContainerType == InventoryContainerType.Equipment)?.Items ?? [];
        return [close, new CurrentUserStateMessage(view.Character, view.CharacterEntry.Public, equipment, view.CharacterEntry.Features.Pets)];
    }
}
