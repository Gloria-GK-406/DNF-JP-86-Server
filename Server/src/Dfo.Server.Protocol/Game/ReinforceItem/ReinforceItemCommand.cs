using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.ReinforceItem;

public sealed record ReinforceItemCommand(
    OnlineUserId OnlineUserId,
    SelectedSessionIdentity Session,
    BusinessSlot TargetSlot,
    int ExpectedItemId,
    BusinessSlot? MaterialSlot,
    int? AccountMaterialItemId,
    BusinessSlot? ProtectionSlot)
    : ICommand<EquipmentReinforcementResult>, IOnlineUserCommand, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}

public sealed class ReinforceItemCommandHandler(IEquipmentReinforcementDomainService reinforcement)
    : ICommandHandler<ReinforceItemCommand, EquipmentReinforcementResult>
{
    public ValueTask<EquipmentReinforcementResult> HandleAsync(
        CommandExecutionContext context, ReinforceItemCommand command,
        CancellationToken cancellationToken) =>
        reinforcement.ReinforceAsync(command.Session, command.TargetSlot,
            command.ExpectedItemId, command.MaterialSlot, command.AccountMaterialItemId,
            command.ProtectionSlot,
            cancellationToken);
}
