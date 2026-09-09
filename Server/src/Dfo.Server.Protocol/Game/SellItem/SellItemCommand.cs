using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.SellItem;

public sealed record SellItemCommand(OnlineUserId OnlineUserId, SelectedSessionIdentity Session,
    InventoryContainerType Container, int SlotIndex, int Count, int NpcId)
    : ICommand<ItemSaleResult>, IOnlineUserCommand, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}

public sealed class SellItemCommandHandler(IItemSaleDomainService sales, TimeProvider clock)
    : ICommandHandler<SellItemCommand, ItemSaleResult>
{
    public ValueTask<ItemSaleResult> HandleAsync(CommandExecutionContext context, SellItemCommand command,
        CancellationToken cancellationToken) => sales.SellAsync(command.Session, command.Container,
            command.SlotIndex, command.Count, command.NpcId, clock.GetUtcNow(), cancellationToken);
}
