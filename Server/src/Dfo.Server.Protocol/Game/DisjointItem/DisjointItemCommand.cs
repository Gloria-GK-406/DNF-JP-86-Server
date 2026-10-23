using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.DisjointItem;

public sealed record NpcDisjointItemCommand(
    OnlineUserId OnlineUserId,
    SelectedSessionIdentity Session,
    BusinessSlot TargetSlot)
    : ICommand<EquipmentDisjointResult>, IRequestSettlementCommand, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}

public sealed record PortableDisjointItemCommand(
    OnlineUserId OnlineUserId,
    SelectedSessionIdentity Session,
    BusinessSlot TargetSlot,
    BusinessSlot DisjointToolSlot)
    : ICommand<EquipmentDisjointResult>, IRequestSettlementCommand, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}

public sealed class NpcDisjointItemCommandHandler(IEquipmentDisjointDomainService disjoint)
    : ICommandHandler<NpcDisjointItemCommand, EquipmentDisjointResult>
{
    public ValueTask<EquipmentDisjointResult> HandleAsync(
        CommandExecutionContext context,
        NpcDisjointItemCommand command,
        CancellationToken cancellationToken) =>
        disjoint.DisjointAtNpcAsync(command.Session, command.TargetSlot,
            cancellationToken);
}

public sealed class PortableDisjointItemCommandHandler(IEquipmentDisjointDomainService disjoint)
    : ICommandHandler<PortableDisjointItemCommand, EquipmentDisjointResult>
{
    public ValueTask<EquipmentDisjointResult> HandleAsync(
        CommandExecutionContext context,
        PortableDisjointItemCommand command,
        CancellationToken cancellationToken) =>
        disjoint.DisjointWithPortableMachineAsync(
            command.Session, command.TargetSlot, command.DisjointToolSlot,
            cancellationToken);
}
