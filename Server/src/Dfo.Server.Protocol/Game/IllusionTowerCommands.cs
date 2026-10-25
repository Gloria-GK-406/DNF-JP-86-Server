using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game;

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
