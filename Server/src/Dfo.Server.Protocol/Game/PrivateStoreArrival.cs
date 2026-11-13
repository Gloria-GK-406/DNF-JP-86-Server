using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.PrivateStore;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.PrivateStore;

namespace Dfo.Server.Protocol.Game;

/// <summary>A character arrived in a town area: tell it about the stalls already standing there (NOTI 0x54).</summary>
/// <remarks>
/// Raised where a newcomer's own town frames are registered -- character selection, a return to
/// town and an area transfer -- so it lives at the Game layer rather than in one slice. The stalls
/// are read when the notice is composed, so the newcomer gets them as they stand.
/// </remarks>
public sealed record PrivateStoreArrival(SelectedSessionIdentity Session) : INotification<PrivateStoreArrival>;

public sealed class PrivateStoreArrivalHandler(ISelectedSessionDomainService sessions, ITownAreaDomainService townAreas,
    IPrivateStoreQueryDomainService stores) : INotificationHandler<PrivateStoreArrival>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient, PrivateStoreArrival notice,
        CancellationToken token)
    {
        if (recipient.CharacterId != notice.Session.CharacterId || !sessions.IsInWorld(notice.Session)
            || townAreas.Find(notice.Session.CharacterId) is not { } member)
            return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([]);
        var listings = stores.In(member.Area.ChannelId, member.Area.TownId, member.Area.AreaId)
            .Where(store => store.Owner.CharacterId != notice.Session.CharacterId)
            .Select(store => (IServerNetworkMessage)new PrivateStoreNotice(ServerMessageType.GamePrivateStoreAreaList,
                PrivateStorePayloadCodec.AreaListing(store.Owner.WireUserId, store)))
            .ToArray();
        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(listings);
    }
}
