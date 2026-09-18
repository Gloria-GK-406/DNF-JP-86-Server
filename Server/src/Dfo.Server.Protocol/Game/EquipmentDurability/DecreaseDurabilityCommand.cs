using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.EquipmentDurability;

public sealed record DecreaseDurabilityCommand(OnlineUserId OnlineUserId,
    SelectedSessionIdentity Session, byte SlotIndex)
    : ICommand<EquipmentDurabilityResult>, IOnlineUserCommand, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}

public sealed class DecreaseDurabilityCommandHandler(IEquipmentDurabilityDomainService durability)
    : ICommandHandler<DecreaseDurabilityCommand, EquipmentDurabilityResult>
{
    public ValueTask<EquipmentDurabilityResult> HandleAsync(CommandExecutionContext context,
        DecreaseDurabilityCommand command, CancellationToken cancellationToken) =>
        durability.DecreaseAsync(command.Session, command.SlotIndex, cancellationToken);
}
