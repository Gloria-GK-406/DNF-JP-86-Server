using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.EnchantByBead;

public sealed record EnchantByBeadCommand(
    OnlineUserId OnlineUserId, SelectedSessionIdentity Session,
    BusinessSlot BeadSlot, BusinessSlot TargetSlot)
    : ICommand<EquipmentEnchantResult>, IRequestSettlementCommand, IOnlineUserCommand, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}

public sealed class EnchantByBeadCommandHandler(IEquipmentEnchantDomainService enchant)
    : ICommandHandler<EnchantByBeadCommand, EquipmentEnchantResult>
{
    public ValueTask<EquipmentEnchantResult> HandleAsync(
        CommandExecutionContext context, EnchantByBeadCommand command,
        CancellationToken ct) => enchant.EnchantAsync(
            command.Session, command.BeadSlot, command.TargetSlot, ct);
}
