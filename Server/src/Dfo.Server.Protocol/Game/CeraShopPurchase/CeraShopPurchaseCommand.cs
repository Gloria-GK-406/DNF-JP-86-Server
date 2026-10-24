using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.CeraShopPurchase;

public sealed record CeraShopPurchaseCommand(OnlineUserId OnlineUserId, SelectedSessionIdentity Session, CeraShopPurchaseInput Input)
    : ICommand<CeraShopPurchaseResult>, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}

public sealed class CeraShopPurchaseCommandHandler(ICeraShopDomainService shops, TimeProvider clock)
    : ICommandHandler<CeraShopPurchaseCommand, CeraShopPurchaseResult>
{
    public ValueTask<CeraShopPurchaseResult> HandleAsync(CommandExecutionContext context,
        CeraShopPurchaseCommand command, CancellationToken cancellationToken) =>
        shops.PurchaseAsync(command.Session, command.Input, clock.GetUtcNow(), context.LegacyFootprint, cancellationToken);
}
