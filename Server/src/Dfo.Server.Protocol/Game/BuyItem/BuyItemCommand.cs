using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.BuyItem;

public sealed record BuyItemCommand(
    OnlineUserId OnlineUserId, SelectedSessionIdentity Session, int ItemId, int RawCount, int ShopId, int NpcId)
    : ICommand<BuyItemOutcome>, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
    public IReadOnlyList<AccountId> AccountIds { get; init; } = [Session.AccountId];
}

public sealed record BuyItemOutcome(NpcShopPurchaseResult Purchase, QuestProgressSnapshot? QuestProgress)
{
    public NpcShopPurchaseReceipt? Receipt => Purchase.Receipt;
    public NpcShopRejection Rejection => Purchase.Rejection;

    public static BuyItemOutcome Refused(NpcShopRejection rejection) =>
        new(NpcShopPurchaseResult.Refused(rejection), null);
}

public sealed class BuyItemCommandHandler(
    INpcShopDomainService shops,
    IQuestProgressDomainService quests,
    TimeProvider clock, Dfo.Server.Abstractions.Marriage.IMarriageHousingDomainService housing)
    : ICommandHandler<BuyItemCommand, BuyItemOutcome>, ICommandHandler<HousingBuyItemCommand, BuyItemOutcome>
{
    public ValueTask<BuyItemOutcome> HandleAsync(CommandExecutionContext context, HousingBuyItemCommand command, CancellationToken ct) =>
        HandleAsync(context, command.Purchase, ct);
    public async ValueTask<BuyItemOutcome> HandleAsync(
        CommandExecutionContext context, BuyItemCommand command, CancellationToken cancellationToken)
    {
        var purchase = command.NpcId == 20011
            ? await housing.PurchaseAsync(command.Session, command.ItemId, command.RawCount, command.ShopId, command.AccountIds, cancellationToken).ConfigureAwait(false)
            : await shops.PurchaseAsync(
            command.Session, command.ItemId, command.RawCount, command.ShopId, command.NpcId,
            clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        if (purchase.Receipt is null)
            return new BuyItemOutcome(purchase, null);

        var progress = await quests.ReportAsync(
            command.Session.AccountId, command.Session.CharacterId,
            [new QuestHoldingsChanged()], cancellationToken).ConfigureAwait(false);
        return new BuyItemOutcome(purchase, progress.NotificationSnapshot);
    }
}

public sealed record HousingBuyItemCommand(BuyItemCommand Purchase) : ICommand<BuyItemOutcome>, IRequestSettlementCommand
{
    public SelectedSessionIdentity? Session => Purchase.Session;
}
