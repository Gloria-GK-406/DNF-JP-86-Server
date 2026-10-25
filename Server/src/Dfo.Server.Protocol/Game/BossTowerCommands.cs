using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.World;
using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Protocol.Game;

public abstract record BossTowerActionCommand(BossTowerActionContext Context) : IMultiSelectedSessionCommand
{
    public SelectedSessionIdentity Session => Context.Session;
    public IReadOnlyList<SelectedSessionIdentity> Sessions { get; init; } = [Context.Session];
}

public sealed record StartBossTowerCommand(BossTowerActionContext Context) : BossTowerActionCommand(Context), ICommand<BossTowerActionResult>;
public sealed class StartBossTowerCommandHandler(IBossTowerDomainService towers)
    : ICommandHandler<StartBossTowerCommand, BossTowerActionResult>
{
    public ValueTask<BossTowerActionResult> HandleAsync(CommandExecutionContext context,
        StartBossTowerCommand command, CancellationToken token) => towers.StartAsync(command.Context, token);
}

public sealed record LeaveBossTowerCommand(BossTowerActionContext Context, bool Disconnected)
    : BossTowerActionCommand(Context), ICommand<BossTowerActionResult>;
public sealed class LeaveBossTowerCommandHandler(IBossTowerDomainService towers)
    : ICommandHandler<LeaveBossTowerCommand, BossTowerActionResult>
{
    public ValueTask<BossTowerActionResult> HandleAsync(CommandExecutionContext context,
        LeaveBossTowerCommand command, CancellationToken token) =>
        towers.LeaveAsync(command.Context, command.Disconnected, context.LegacyFootprint, token);
}

public sealed record SynchronizeBossTowerLeaderCommand(
    BossTowerActionContext Context,
    SelectedSessionIdentity Leader) : BossTowerActionCommand(Context), ICommand<BossTowerActionResult>;

public sealed class SynchronizeBossTowerLeaderCommandHandler(IBossTowerDomainService towers)
    : ICommandHandler<SynchronizeBossTowerLeaderCommand, BossTowerActionResult>
{
    public ValueTask<BossTowerActionResult> HandleAsync(CommandExecutionContext context,
        SynchronizeBossTowerLeaderCommand command, CancellationToken token) =>
        towers.SynchronizeLeaderAsync(command.Context, command.Leader, token);
}
