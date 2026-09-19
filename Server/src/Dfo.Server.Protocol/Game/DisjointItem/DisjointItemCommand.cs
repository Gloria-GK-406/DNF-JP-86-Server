using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.DisjointItem;

public sealed record DisjointItemCommand(
    OnlineUserId OnlineUserId,
    SelectedSessionIdentity Session,
    BusinessSlot TargetSlot,
    short DisjointItemSlotIndex,
    int ContextValue,
    EquipmentDisjointSettings Settings)
    : ICommand<EquipmentDisjointResult>, IOnlineUserCommand, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}

public sealed class DisjointItemCommandHandler(IEquipmentDisjointDomainService disjoint)
    : ICommandHandler<DisjointItemCommand, EquipmentDisjointResult>
{
    public ValueTask<EquipmentDisjointResult> HandleAsync(
        CommandExecutionContext context,
        DisjointItemCommand command,
        CancellationToken cancellationToken) =>
        disjoint.DisjointAsync(command.Session, command.TargetSlot,
            command.DisjointItemSlotIndex, command.ContextValue, command.Settings,
            cancellationToken);
}
