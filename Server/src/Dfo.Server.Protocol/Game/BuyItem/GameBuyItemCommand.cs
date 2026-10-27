using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.BuyItem;

public sealed record GameBuyItemCommand(NetworkDispatchContext Dispatch, BuyItemRequest Input) : IProtocolCommand<BuyItemOutcome>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed record BuyItemOutcome(NpcShopPurchaseResult Purchase, QuestProgressSnapshot? QuestProgress)
{
    public NpcShopPurchaseReceipt? Receipt => Purchase.Receipt;
    public NpcShopRejection Rejection => Purchase.Rejection;

    public static BuyItemOutcome Refused(NpcShopRejection rejection) =>
        new(NpcShopPurchaseResult.Refused(rejection), null);
}

public sealed class GameBuyItemCommandHandler(ISelectedSessionDomainService sessions,
    IOnlineUserQueryDomainService onlineUsers, IMarriageQueryDomainService marriage,
    INpcShopDomainService shops, IMarriageHousingDomainService housing, IQuestProgressDomainService quests,
    TimeProvider clock, ICommandWorkUnits units, IGameCommandOutbox outbox,
    ICommandProjections projections, ILogger<GameBuyItemCommandHandler> logger)
    : ICommandHandler<GameBuyItemCommand, BuyItemOutcome>
{
    public async ValueTask<BuyItemOutcome> HandleAsync(CommandExecutionContext context, GameBuyItemCommand command, CancellationToken ct)
    {
        var message = command.Input;
        var request = context.Request ?? throw new InvalidOperationException("Buy requires its explicit request.");
        var session = sessions.FindByConnection(command.Dispatch.Connection.ConnectionId);
        BuyItemOutcome result;
        if (session is null || !onlineUsers.TryGetByConnection(command.Dispatch.Connection.ConnectionId, out var online)
            || online is null || request is CommandRequestContext.ProtocolEffectRejected)
            result = BuyItemOutcome.Refused(NpcShopRejection.NoSession);
        else
        {
            var accounts = message.NpcId == 20011
                ? await marriage.CoupleAccountsAsync(session, ct).ConfigureAwait(false) : [session.AccountId];
            var selected = request as CommandRequestContext.ProtocolEffectSelected;
            if (message.GetType() == typeof(HousingBuyItemRequest)
                ? selected is null || session != selected.Recipient.Session
                : message.GetType() != typeof(BuyItemRequest) || request is not CommandRequestContext.ProtocolNonEffect)
                throw new InvalidOperationException("Purchase requires its initially captured exact request shape and actor.");
            var actor = selected?.Recipient ?? new SelectedSessionRecipient(session, command.Dispatch.Connection, message.Metadata);
            var declaration = selected is not null
                ? new CommandWorkUnitDeclaration("housing-purchase", [actor], CommandWorkUnitKind.MainSettlement)
                : new CommandWorkUnitDeclaration("npc-purchase", [actor], Footprint: CommandFootprintMode.NonRequest);
            result = await units.WriteAsync(context, declaration, async (unit, token) =>
            {
                try
                {
                    var purchase = message.NpcId == 20011
                        ? await housing.PurchaseAsync(session, message.ItemId, message.RawCount, message.ShopId, accounts, token).ConfigureAwait(false)
                        : await shops.PurchaseAsync(session, message.ItemId, message.RawCount, message.ShopId, message.NpcId,
                            clock.GetUtcNow(), unit.Footprint, token).ConfigureAwait(false);
                    if (purchase.Receipt is null) return new BuyItemOutcome(purchase, null);
                    var progress = await quests.ReportAsync(session.AccountId, session.CharacterId,
                        [new QuestHoldingsChanged()], token).ConfigureAwait(false);
                    return new BuyItemOutcome(purchase, progress.NotificationSnapshot);
                }
                catch (NpcShopPurchaseRejectedException rejected)
                {
                    unit.RequireRollback();
                    return BuyItemOutcome.Refused(rejected.Reason);
                }
            }, ct).ConfigureAwait(false);
        }
        if (result.Receipt is { } receipt)
        {
            logger.LogInformation("NPC purchase character={CharacterId} npc={NpcId} shop={ShopId} item={ItemId} count={Count} gold={Gold}",
                receipt.Session.CharacterId.Value, message.NpcId, message.ShopId, receipt.ItemId, receipt.Count, receipt.Gold);
            await projections.ProjectAsync(new NotificationAudience.TheseCharacters(receipt.HousingAudience.Count > 0 ? receipt.HousingAudience : [receipt.Session.CharacterId]),
                new NpcShopPurchaseCompleted(receipt, result.QuestProgress), ct).ConfigureAwait(false);
        }
        else
        {
            logger.LogInformation("NPC purchase {ConnectionId} npc={NpcId} shop={ShopId} item={ItemId} rawCount={Count} refused={Reason}",
                command.Dispatch.Connection.ConnectionId, message.NpcId, message.ShopId, message.ItemId, message.RawCount, result.Rejection);
            await outbox.RegisterCmdAsync(command.Dispatch.Connection.ConnectionId,
                new BuyItemResponse(null, ErrorCode(result.Rejection)), ct).ConfigureAwait(false);
        }
        return result;
    }

    internal static byte ErrorCode(NpcShopRejection rejection) => rejection switch
    {
        NpcShopRejection.NoRoom => 4,
        NpcShopRejection.InsufficientGold => 10,
        NpcShopRejection.InsufficientMaterials => 1,
        NpcShopRejection.PurchaseLimitReached => 0x85,
        NpcShopRejection.ItemNotSold => 17,
        NpcShopRejection.NoSession or NpcShopRejection.ShopUnavailable
            or NpcShopRejection.InvalidCount or NpcShopRejection.UnsupportedItem => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(rejection), rejection, "A refusal needs a known client error mapping."),
    };
}
