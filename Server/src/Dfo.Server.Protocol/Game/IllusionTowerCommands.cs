using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game;

public sealed record DieInIllusionTowerCommand(TowerActionContext Context) : ICommand<TowerActionResult>;
public sealed class DieInIllusionTowerCommandHandler(IIllusionTowerDomainService towers)
    : ICommandHandler<DieInIllusionTowerCommand, TowerActionResult>
{
    public ValueTask<TowerActionResult> HandleAsync(CommandExecutionContext context,
        DieInIllusionTowerCommand command, CancellationToken token) => towers.DieAsync(command.Context, context.LegacyFootprint, token);
}

public sealed record LeaveIllusionTowerCommand(TowerActionContext Context, bool Disconnected)
    : ICommand<TowerActionResult>;
public sealed class LeaveIllusionTowerCommandHandler(IIllusionTowerDomainService towers)
    : ICommandHandler<LeaveIllusionTowerCommand, TowerActionResult>
{
    public ValueTask<TowerActionResult> HandleAsync(CommandExecutionContext context,
        LeaveIllusionTowerCommand command, CancellationToken token) =>
        towers.LeaveAsync(command.Context, command.Disconnected, context.LegacyFootprint, token);
}

public sealed record SynchronizeIllusionTowerLeaderCommand(
    TowerActionContext Context,
    SelectedSessionIdentity Leader) : ICommand<TowerActionResult>;

public sealed class SynchronizeIllusionTowerLeaderCommandHandler(IIllusionTowerDomainService towers)
    : ICommandHandler<SynchronizeIllusionTowerLeaderCommand, TowerActionResult>
{
    public ValueTask<TowerActionResult> HandleAsync(CommandExecutionContext context,
        SynchronizeIllusionTowerLeaderCommand command, CancellationToken token) =>
        towers.SynchronizeLeaderAsync(command.Context, command.Leader, token);
}

public sealed record SelectIllusionTowerReturnCommand(TowerActionContext Context, byte Option)
    : ICommand<TowerActionResult>;
public sealed class SelectIllusionTowerReturnCommandHandler(IIllusionTowerDomainService towers)
    : ICommandHandler<SelectIllusionTowerReturnCommand, TowerActionResult>
{
    public ValueTask<TowerActionResult> HandleAsync(CommandExecutionContext context,
        SelectIllusionTowerReturnCommand command, CancellationToken token) =>
        towers.SelectReturnAsync(command.Context, command.Option, token);
}

public sealed record PickupTowerItemCommand(TowerActionContext Context, ushort GroundUid) : ICommand<TowerPickupResult>;
public sealed class PickupTowerItemCommandHandler(IIllusionTowerDomainService towers)
    : ICommandHandler<PickupTowerItemCommand, TowerPickupResult>
{
    public ValueTask<TowerPickupResult> HandleAsync(CommandExecutionContext context, PickupTowerItemCommand command, CancellationToken token) =>
        towers.PickupAsync(command.Context, command.GroundUid, token);
}
