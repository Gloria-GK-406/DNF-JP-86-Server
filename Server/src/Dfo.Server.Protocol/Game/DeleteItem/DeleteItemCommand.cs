using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.DeleteItem;

public sealed record DeleteItemCommand(
    OnlineUserId OnlineUserId,
    SelectedSessionIdentity Session,
    InventoryContainerType Container,
    byte EntryCount,
    ushort OperationType,
    int SlotIndex,
    int ItemId,
    int Count) : ICommand<ItemDiscardResult>, IOnlineUserCommand, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}

public sealed class DeleteItemCommandHandler(IItemDiscardDomainService discards, TimeProvider clock)
    : ICommandHandler<DeleteItemCommand, ItemDiscardResult>
{
    public ValueTask<ItemDiscardResult> HandleAsync(
        CommandExecutionContext context,
        DeleteItemCommand command,
        CancellationToken cancellationToken) =>
        discards.DiscardAsync(command.Session, command.Container, command.EntryCount,
            command.OperationType, command.SlotIndex, command.ItemId, command.Count,
            clock.GetUtcNow(), cancellationToken);
}
