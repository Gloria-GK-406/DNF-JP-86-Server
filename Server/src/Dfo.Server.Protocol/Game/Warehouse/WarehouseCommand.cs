using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.Warehouse;

public sealed record WarehouseCommand(OnlineUserId OnlineUserId, SelectedSessionIdentity Session, ushort Type, int Value, BusinessSlot? ItemSlot = null)
    : ICommand<InventoryExpansionResult>, IRequestSettlementCommand, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}
public sealed class WarehouseCommandHandler(IInventoryExpansionDomainService expansion,
    TimeProvider clock) : ICommandHandler<WarehouseCommand, InventoryExpansionResult>
{
    public ValueTask<InventoryExpansionResult> HandleAsync(CommandExecutionContext context, WarehouseCommand command, CancellationToken ct)
    {
        var at = clock.GetUtcNow();
        if (command.Type is 0x0131 or 0x0132)
            return expansion.UpgradeAccountAsync(command.Session, command.Type == 0x0131, at, ct);
        if (command.Type is 0x0133 or 0x0134)
            return expansion.TransferGoldAsync(command.Session, command.Type == 0x0133, command.Value, at, ct);
        if (command.Type is not (0x0198 or 0x01AF)) return ValueTask.FromResult(new InventoryExpansionResult(InventoryExpansionRejection.InvalidRequest));
        if (command.ItemSlot is not { } slot)
            return ValueTask.FromResult(new InventoryExpansionResult(InventoryExpansionRejection.InvalidRequest));
        return expansion.UseItemAsync(command.Session, slot, command.Type == 0x0198
            ? InventoryExpansionKind.PersonalCargo : InventoryExpansionKind.Main, at, ct);
    }
}
