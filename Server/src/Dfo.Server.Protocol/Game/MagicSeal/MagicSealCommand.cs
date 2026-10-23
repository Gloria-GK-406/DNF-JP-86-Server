using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.MagicSeal;

public sealed record MagicSealCommand(OnlineUserId OnlineUserId,
    SelectedSessionIdentity Session, bool Change, BusinessSlot Target,
    BusinessSlot? Material, byte OptionIndex)
    : ICommand<EquipmentMagicSealResult>, IRequestSettlementCommand, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}

public sealed class MagicSealCommandHandler(IEquipmentMagicSealDomainService seals)
    : ICommandHandler<MagicSealCommand, EquipmentMagicSealResult>
{
    public ValueTask<EquipmentMagicSealResult> HandleAsync(CommandExecutionContext context,
        MagicSealCommand command, CancellationToken ct) => command.Change
        ? seals.ChangeAsync(command.Session, command.Target, command.Material,
            command.OptionIndex, ct)
        : seals.UnsealAsync(command.Session, command.Target, command.Material, ct);
}
