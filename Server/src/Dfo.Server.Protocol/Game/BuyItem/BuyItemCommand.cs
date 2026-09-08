using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.BuyItem;

public sealed record BuyItemCommand(
    OnlineUserId OnlineUserId, SelectedSessionIdentity Session, int ItemId, int RawCount, int ShopId, int NpcId)
    : ICommand<NpcShopPurchaseResult>, IOnlineUserCommand, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}

public sealed class BuyItemCommandHandler(INpcShopDomainService shops, TimeProvider clock)
    : ICommandHandler<BuyItemCommand, NpcShopPurchaseResult>
{
    public ValueTask<NpcShopPurchaseResult> HandleAsync(
        CommandExecutionContext context, BuyItemCommand command, CancellationToken cancellationToken) =>
        shops.PurchaseAsync(command.Session, command.ItemId, command.RawCount, command.ShopId, command.NpcId,
            clock.GetUtcNow(), cancellationToken);
}
