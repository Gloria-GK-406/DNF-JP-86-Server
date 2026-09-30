using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.Forging;

public sealed record ForgingCommand(OnlineUserId OnlineUserId,
    SelectedSessionIdentity Session, BusinessSlot Target, int ExpectedTargetItemId,
    BusinessSlot Material) : ICommand<EquipmentForgingResult>, IOnlineUserCommand,
    IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}

public sealed class ForgingCommandHandler(IEquipmentForgingDomainService forging)
    : ICommandHandler<ForgingCommand, EquipmentForgingResult>
{
    public ValueTask<EquipmentForgingResult> HandleAsync(CommandExecutionContext context,
        ForgingCommand command, CancellationToken ct) => forging.ForgeAsync(
            command.Session, command.Target, command.ExpectedTargetItemId, command.Material, ct);
}
