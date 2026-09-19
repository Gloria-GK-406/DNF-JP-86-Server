using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.RepairItem;

public sealed record RepairItemCommand(OnlineUserId OnlineUserId, SelectedSessionIdentity Session,
    BusinessSlot? Slot)
    : ICommand<EquipmentRepairResult>, IOnlineUserCommand, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}

public sealed class RepairItemCommandHandler(IEquipmentRepairDomainService repairs)
    : ICommandHandler<RepairItemCommand, EquipmentRepairResult>
{
    public ValueTask<EquipmentRepairResult> HandleAsync(CommandExecutionContext context, RepairItemCommand command,
        CancellationToken cancellationToken) =>
        repairs.RepairAsync(command.Session, command.Slot, cancellationToken);
}
