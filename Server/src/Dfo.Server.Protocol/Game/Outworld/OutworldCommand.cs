using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.Outworld;

public sealed record OutworldCommand(OnlineUserId OnlineUserId,
    SelectedSessionIdentity Session, EquipmentOutworldAction Action,
    BusinessSlot Target, int ExpectedTargetItemId,
    BusinessSlot Material, int ExpectedMaterialItemId,
    byte SelectedOption) : ICommand<EquipmentOutworldResult>, IRequestSettlementCommand, IOnlineUserCommand, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}

public sealed class OutworldCommandHandler(IEquipmentOutworldDomainService outworld)
    : ICommandHandler<OutworldCommand, EquipmentOutworldResult>
{
    public ValueTask<EquipmentOutworldResult> HandleAsync(CommandExecutionContext context,
        OutworldCommand command, CancellationToken ct) => outworld.ApplyAsync(
            command.Session, command.Action, command.Target, command.ExpectedTargetItemId,
            command.Material, command.ExpectedMaterialItemId, command.SelectedOption, ct);
}
