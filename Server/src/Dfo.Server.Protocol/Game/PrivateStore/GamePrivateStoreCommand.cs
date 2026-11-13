using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.PrivateStore;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Mail;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.PrivateStore;

public sealed record GamePrivateStoreCommand(NetworkDispatchContext Dispatch, IPrivateStoreRequest Request) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Request;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Request.Metadata);
}

/// <summary>Every stall request: one private store call, its reply, and what the area and the visitors are told.</summary>
/// <remarks>
/// Every request is answered: a request left unanswered hangs the client's stall window (seen
/// 2026-11-14 when 0x56 went unanswered). Notices go to the town area the stall stands in -- 0x4E
/// when it is put up, 0x51 when its goods are laid out, 0x4F when it comes down -- and to the
/// stall's visitors (0x53 after a sale, 0x50 when it closes under them).
/// </remarks>
public sealed class GamePrivateStoreCommandHandler(
    ISelectedSessionDomainService sessions,
    IPrivateStoreDomainService stores,
    IPrivateStoreQueryDomainService storeQuery,
    ITownAreaDomainService townAreas,
    IMailboxAssetDomainService assets,
    ICommandWorkUnits units,
    IGameCommandOutbox outbox,
    ICommandProjections projections,
    ILogger<GamePrivateStoreCommandHandler> logger) : ICommandHandler<GamePrivateStoreCommand, bool>
{
    // 70 error codes (ENUM_* in df_game_r): 62 cannot create, 63 no permission, 57 no such slot,
    // 0x0A not enough gold, 0x16 the seller cannot hold the gold.
    private const byte CannotCreate = 62;
    private const byte NoPermission = 63;
    private const byte NoSuchLine = 57;
    private const byte NotEnoughGold = 0x0A;
    private const byte SellerFull = 0x16;
    private const byte Failed = 20;

    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, GamePrivateStoreCommand command, CancellationToken ct)
    {
        var request = command.Request;
        var connection = command.Dispatch.Connection.ConnectionId;
        var replyType = (ServerMessageType)(int)request.MessageType;
        var session = sessions.FindByConnection(connection);
        if (session is null || !sessions.IsInWorld(session) || request.Malformed)
        {
            await outbox.RegisterCmdAsync(connection, new PrivateStoreReply(replyType, null, Failed), ct).ConfigureAwait(false);
            return false;
        }
        var recipient = new SelectedSessionRecipient(session, command.Dispatch.Connection, request.Metadata);
        try
        {
            switch (request)
            {
                case PrivateStoreCreateRequest create:
                    await CreateAsync(context, recipient, create, ct).ConfigureAwait(false);
                    break;
                case PrivateStoreDisplayRequest display:
                    await DisplayAsync(context, recipient, display, ct).ConfigureAwait(false);
                    break;
                case PrivateStoreSimpleRequest { Type: ClientMessageType.GamePrivateStoreRemove or ClientMessageType.GamePrivateStoreMoveToGate } close:
                    await CloseAsync(context, recipient, replyType, ct).ConfigureAwait(false);
                    break;
                case PrivateStoreSimpleRequest { Type: ClientMessageType.GamePrivateStoreEnter } enter:
                    await EnterAsync(session, enter.Owner, ct).ConfigureAwait(false);
                    break;
                case PrivateStoreSimpleRequest { Type: ClientMessageType.GamePrivateStoreExit }:
                    storeQuery.Leave(session.CharacterId);
                    await outbox.RegisterCmdAsync(connection, new PrivateStoreReply(replyType, []), ct).ConfigureAwait(false);
                    break;
                case PrivateStoreLineRequest { Buy: false } verify:
                    await VerifyAsync(session, verify, ct).ConfigureAwait(false);
                    break;
                case PrivateStoreLineRequest buy:
                    await BuyAsync(context, recipient, buy, ct).ConfigureAwait(false);
                    break;
                default:
                    await outbox.RegisterCmdAsync(connection, new PrivateStoreReply(replyType, null, Failed), ct).ConfigureAwait(false);
                    break;
            }
            return true;
        }
        catch (PrivateStoreRefusedException refused)
        {
            logger.LogInformation("Private store {Request} character={Character} refused: {Refusal}",
                request.MessageType, session.CharacterId.Value, refused.Refusal);
            await outbox.RegisterCmdAsync(connection, new PrivateStoreReply(replyType, null, CodeOf(refused.Refusal)), ct).ConfigureAwait(false);
            return false;
        }
    }

    private async ValueTask CreateAsync(CommandExecutionContext context, SelectedSessionRecipient recipient, PrivateStoreCreateRequest create,
        CancellationToken ct)
    {
        var session = recipient.Session;
        var view = await stores.OpenAsync(session, new(0, create.TownId, create.AreaId, create.X, create.Y), create.Doll, ct).ConfigureAwait(false);
        logger.LogInformation("Private store opened character={Character} town={Town} area={Area} doll={Doll}",
            session.CharacterId.Value, view.Spot.TownId, view.Spot.AreaId, view.Doll);
        await outbox.RegisterCmdAsync(session.ConnectionId, new PrivateStoreReply(ServerMessageType.GamePrivateStoreCreate, []), ct).ConfigureAwait(false);
        await ToAreaAsync(view, new PrivateStoreNotice(ServerMessageType.GamePrivateStoreOpened,
            PrivateStorePayloadCodec.Opened(session.WireUserId, view.Spot, view.Doll)), ct).ConfigureAwait(false);
    }

    private async ValueTask DisplayAsync(CommandExecutionContext context, SelectedSessionRecipient recipient, PrivateStoreDisplayRequest display,
        CancellationToken ct)
    {
        var session = recipient.Session;
        var (view, projection, refusal) = await units.WriteAsync(context, new("private-store-display", [recipient]), async (unit, token) =>
        {
            try
            {
                var (shown, changes) = await stores.DisplayAsync(session, display.Title, display.Lines, token).ConfigureAwait(false);
                return ((PrivateStoreView?)shown, (MailboxAssetProjection?)await assets.PrepareAsync(session.CharacterId, changes, token).ConfigureAwait(false),
                    PrivateStoreRefusal.None);
            }
            catch (PrivateStoreRefusedException refused)
            {
                unit.RequireRollback(refused);
                return ((PrivateStoreView?)null, (MailboxAssetProjection?)null, refused.Refusal);
            }
        }, ct).ConfigureAwait(false);
        if (refusal != PrivateStoreRefusal.None) throw new PrivateStoreRefusedException(refusal);
        logger.LogInformation("Private store displayed character={Character} lines={Lines}", session.CharacterId.Value, view!.Lines.Count);
        _ = await projections.ProjectAsync(new NotificationAudience.TheseCharacters([session.CharacterId]),
            new PrivateStoreCompleted(session, new PrivateStoreReply(ServerMessageType.GamePrivateStoreCompleteDisplay, [0]), projection!), ct)
            .ConfigureAwait(false);
        await ToAreaAsync(view, new PrivateStoreNotice(ServerMessageType.GamePrivateStoreTitle,
            PrivateStorePayloadCodec.Title(session.WireUserId, view.Title!)), ct).ConfigureAwait(false);
    }

    private async ValueTask CloseAsync(CommandExecutionContext context, SelectedSessionRecipient recipient, ServerMessageType replyType,
        CancellationToken ct)
    {
        var session = recipient.Session;
        var visitorsBefore = storeQuery.VisitorsOf(session.CharacterId);
        var (closure, projection) = await units.WriteAsync(context, new("private-store-close", [recipient]), async (_, token) =>
        {
            var closed = await stores.CloseAsync(session, token).ConfigureAwait(false);
            var shown = closed is { Changes.Count: > 0 } ? await assets.PrepareAsync(session.CharacterId, closed.Changes, token).ConfigureAwait(false) : null;
            return (closed, shown);
        }, ct).ConfigureAwait(false);
        var reply = new PrivateStoreReply(replyType, []);
        if (projection is not null)
            _ = await projections.ProjectAsync(new NotificationAudience.TheseCharacters([session.CharacterId]),
                new PrivateStoreCompleted(session, reply, projection), ct).ConfigureAwait(false);
        else
            await outbox.RegisterCmdAsync(session.ConnectionId, reply, ct).ConfigureAwait(false);
        if (closure is null) return;
        logger.LogInformation("Private store closed character={Character} mailed={Mailed}", session.CharacterId.Value, closure.Mailed);
        if (visitorsBefore.Count > 0)
            _ = await projections.ProjectAsync(new NotificationAudience.TheseCharacters(visitorsBefore),
                new PrivateStoreBroadcast(new PrivateStoreNotice(ServerMessageType.GamePrivateStoreForceClosed, [])), ct).ConfigureAwait(false);
        await ToAreaAsync(closure.Store, new PrivateStoreNotice(ServerMessageType.GamePrivateStoreClosed,
            PrivateStorePayloadCodec.Closed(session.WireUserId)), ct).ConfigureAwait(false);
    }

    private async ValueTask EnterAsync(SelectedSessionIdentity visitor, ushort ownerWireId, CancellationToken ct)
    {
        if (!sessions.TryGetInWorld(ownerWireId, out var owner) || storeQuery.Find(owner.CharacterId) is not { Displayed: true } store
            || townAreas.Find(visitor.CharacterId) is not { } here || here.Area.ChannelId != store.Spot.ChannelId
            || here.Area.TownId != store.Spot.TownId || here.Area.AreaId != store.Spot.AreaId)
            throw new PrivateStoreRefusedException(PrivateStoreRefusal.NoSuchStore);
        storeQuery.Enter(visitor.CharacterId, owner.CharacterId);
        await outbox.RegisterCmdAsync(visitor.ConnectionId, new PrivateStoreReply(ServerMessageType.GamePrivateStoreEnter,
            PrivateStorePayloadCodec.Lines(ownerWireId, store.Lines)), ct).ConfigureAwait(false);
    }

    private async ValueTask VerifyAsync(SelectedSessionIdentity visitor, PrivateStoreLineRequest verify, CancellationToken ct)
    {
        if (storeQuery.Visiting(visitor.CharacterId) is not { } owner || storeQuery.Find(owner) is not { } store
            || store.Lines.FirstOrDefault(line => line.DisplaySlot == verify.DisplaySlot) is not { } line)
            throw new PrivateStoreRefusedException(PrivateStoreRefusal.NoSuchItem);
        await outbox.RegisterCmdAsync(visitor.ConnectionId, new PrivateStoreReply(ServerMessageType.GamePrivateStoreVerify,
            PrivateStorePayloadCodec.Line(line)), ct).ConfigureAwait(false);
    }

    private async ValueTask BuyAsync(CommandExecutionContext context, SelectedSessionRecipient recipient, PrivateStoreLineRequest buy,
        CancellationToken ct)
    {
        var buyer = recipient.Session;
        var seller = storeQuery.Visiting(buyer.CharacterId) ?? throw new PrivateStoreRefusedException(PrivateStoreRefusal.NoSuchStore);
        var (bought, shown, refusal) = await units.WriteAsync(context, new("private-store-buy", [recipient]), async (unit, token) =>
        {
            try
            {
                var sale = await stores.BuyAsync(buyer, seller, buy.DisplaySlot, buy.Count, token).ConfigureAwait(false);
                return ((PrivateStorePurchase?)sale,
                    (MailboxAssetProjection?)await assets.PrepareAsync(buyer.CharacterId, sale.BuyerChanges, token).ConfigureAwait(false),
                    PrivateStoreRefusal.None);
            }
            catch (PrivateStoreRefusedException refused)
            {
                unit.RequireRollback(refused);
                return ((PrivateStorePurchase?)null, (MailboxAssetProjection?)null, refused.Refusal);
            }
        }, ct).ConfigureAwait(false);
        if (refusal != PrivateStoreRefusal.None) throw new PrivateStoreRefusedException(refusal);
        var purchase = bought!;
        var projection = shown!;
        var landed = purchase.BuyerChanges.FirstOrDefault(change => change.Slot is not null)?.Slot;
        var destination = landed is { } slot ? (ushort)A21InventoryLayout.ToGameSlot(slot).Index : (ushort)0;
        logger.LogInformation("Private store sale seller={Seller} buyer={Buyer} slot={Slot} count={Count} price={Price} fee={Fee}",
            seller.Value, buyer.CharacterId.Value, purchase.DisplaySlot, purchase.Count, purchase.Price, purchase.Fee);
        _ = await projections.ProjectAsync(new NotificationAudience.TheseCharacters([buyer.CharacterId]),
            new PrivateStoreCompleted(buyer, new PrivateStoreReply(ServerMessageType.GamePrivateStoreBuy,
                PrivateStorePayloadCodec.Bought(purchase.BuyerGold, destination, purchase.Bought)), projection), ct).ConfigureAwait(false);
        var left = new PrivateStoreNotice(ServerMessageType.GamePrivateStoreLineLeft,
            PrivateStorePayloadCodec.LineLeft(purchase.DisplaySlot, purchase.RemainingCount));
        var watchers = storeQuery.VisitorsOf(seller).Append(seller).Distinct().ToArray();
        _ = await projections.ProjectAsync(new NotificationAudience.TheseCharacters(watchers), new PrivateStoreBroadcast(left), ct).ConfigureAwait(false);
        if (purchase.SellerGold is { } gold)
            _ = await projections.ProjectAsync(new NotificationAudience.TheseCharacters([seller]),
                new PrivateStoreBroadcast(GameInventoryDelivery.CarriedGoldNotification(gold, DateTimeOffset.UtcNow)), ct).ConfigureAwait(false);
    }

    /// <summary>Tells everyone else standing in the stall's town area.</summary>
    private async ValueTask ToAreaAsync(PrivateStoreView store, IGameServerMessage notice, CancellationToken ct)
    {
        var area = townAreas.Find(new TownAreaKey(store.Spot.ChannelId, store.Spot.TownId, store.Spot.AreaId));
        var others = area?.Members.Select(member => member.CharacterId).Where(id => id != store.Owner.CharacterId).ToArray() ?? [];
        if (others.Length > 0)
            _ = await projections.ProjectAsync(new NotificationAudience.TheseCharacters(others), new PrivateStoreBroadcast(notice), ct)
                .ConfigureAwait(false);
    }

    private static byte CodeOf(PrivateStoreRefusal refusal) => refusal switch
    {
        PrivateStoreRefusal.NotPermitted => NoPermission,
        PrivateStoreRefusal.NotHere or PrivateStoreRefusal.AlreadyOpen => CannotCreate,
        PrivateStoreRefusal.NoSuchItem or PrivateStoreRefusal.NoSuchStore => NoSuchLine,
        PrivateStoreRefusal.InsufficientFunds => NotEnoughGold,
        PrivateStoreRefusal.SellerGoldLimit => SellerFull,
        _ => Failed,
    };
}

/// <summary>A committed stall change for the actor: its reply, then the changed slots and gold.</summary>
public sealed record PrivateStoreCompleted(SelectedSessionIdentity Session, PrivateStoreReply Reply, MailboxAssetProjection Assets)
    : INotification<PrivateStoreCompleted>;

public sealed class PrivateStoreCompletedHandler(ISelectedSessionDomainService sessions) : INotificationHandler<PrivateStoreCompleted>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient, PrivateStoreCompleted notice,
        CancellationToken token)
    {
        if (recipient.CharacterId != notice.Session.CharacterId || recipient.ConnectionId != notice.Session.ConnectionId
            || !sessions.IsInWorld(notice.Session))
            return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([]);
        var assets = notice.Assets;
        var frames = new List<IServerNetworkMessage> { notice.Reply };
        frames.AddRange(assets.Updates.GroupBy(x => A21InventoryLayout.ToGameSlot(x.Slot).Space)
            .Select(group => GameInventoryDelivery.Notification(new CommittedInventoryUpdateState(group.Key,
                group.Select(x => new InventoryItemState(x.Identity, A21InventoryLayout.ToGameSlot(x.Slot).Index, x.Core,
                    AvatarDetail: x.AvatarDetail)).ToArray(), assets.PreparedAt))));
        frames.AddRange(assets.Cleared.Select(x =>
        {
            var wire = A21InventoryLayout.ToGameSlot(x);
            return GameInventoryDelivery.ClearedSlotNotification(wire.Space, wire.Index, assets.PreparedAt);
        }));
        if (assets.Gold is { } gold) frames.Add(GameInventoryDelivery.CarriedGoldNotification(gold, assets.PreparedAt));
        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(frames);
    }
}

/// <summary>A stall notice for characters in the world.</summary>
public sealed record PrivateStoreBroadcast(IGameServerMessage Message) : INotification<PrivateStoreBroadcast>;

public sealed class PrivateStoreBroadcastHandler : INotificationHandler<PrivateStoreBroadcast>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient, PrivateStoreBroadcast notice,
        CancellationToken token) => ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([notice.Message]);
}

public sealed class PrivateStoreRequestHandler(ICommandBus commands) : ClientMessageHandler<IPrivateStoreRequest>,
    IProtocolCommandMessageHandler<GamePrivateStoreCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, IPrivateStoreRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GamePrivateStoreCommand(context, message), ct).ConfigureAwait(false);
}
