using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.World;
using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Protocol.Game;

public sealed record EnterBossTowerCommand(
    IReadOnlyList<TowerEntryMember> Members, SelectedSessionIdentity Leader, uint Seed, byte RequestMode, byte Difficulty, byte PracticeMode)
    : ICommand<BossTowerEntryResult>, IMultiSelectedSessionCommand
{
    public SelectedSessionIdentity Session => Leader;
    public IReadOnlyList<SelectedSessionIdentity> Sessions => Members.Select(value => value.Session).ToArray();
}

public sealed class EnterBossTowerCommandHandler(IBossTowerDomainService towers)
    : ICommandHandler<EnterBossTowerCommand, BossTowerEntryResult>
{
    public ValueTask<BossTowerEntryResult> HandleAsync(CommandExecutionContext context,
        EnterBossTowerCommand command, CancellationToken token) =>
        towers.EnterAsync(command.Members, command.Leader, command.Seed, token, command.RequestMode, command.Difficulty, command.PracticeMode);
}

public abstract record BossTowerActionCommand(BossTowerActionContext Context) : IMultiSelectedSessionCommand
{
    public SelectedSessionIdentity Session => Context.Session;
    public IReadOnlyList<SelectedSessionIdentity> Sessions { get; init; } = [Context.Session];
}

public sealed record LoadBossTowerCommand(BossTowerActionContext Context) : BossTowerActionCommand(Context), ICommand<BossTowerActionResult>;
public sealed class LoadBossTowerCommandHandler(IBossTowerDomainService towers)
    : ICommandHandler<LoadBossTowerCommand, BossTowerActionResult>
{
    public ValueTask<BossTowerActionResult> HandleAsync(CommandExecutionContext context,
        LoadBossTowerCommand command, CancellationToken token) => towers.LoadAsync(command.Context, token);
}

public sealed record StartBossTowerCommand(BossTowerActionContext Context) : BossTowerActionCommand(Context), ICommand<BossTowerActionResult>;
public sealed class StartBossTowerCommandHandler(IBossTowerDomainService towers)
    : ICommandHandler<StartBossTowerCommand, BossTowerActionResult>
{
    public ValueTask<BossTowerActionResult> HandleAsync(CommandExecutionContext context,
        StartBossTowerCommand command, CancellationToken token) => towers.StartAsync(command.Context, token);
}

public sealed record KillBossTowerActorCommand(BossTowerActionContext Context, ushort ActorUid)
    : BossTowerActionCommand(Context), ICommand<BossTowerActionResult>;
public sealed class KillBossTowerActorCommandHandler(IBossTowerDomainService towers)
    : ICommandHandler<KillBossTowerActorCommand, BossTowerActionResult>
{
    public ValueTask<BossTowerActionResult> HandleAsync(CommandExecutionContext context,
        KillBossTowerActorCommand command, CancellationToken token) =>
        towers.KillAsync(command.Context, command.ActorUid, token);
}

public sealed record AdvanceBossTowerCommand(BossTowerActionContext Context, byte TargetX, byte TargetY) : BossTowerActionCommand(Context), ICommand<BossTowerActionResult>;
public sealed class AdvanceBossTowerCommandHandler(IBossTowerDomainService towers)
    : ICommandHandler<AdvanceBossTowerCommand, BossTowerActionResult>
{
    public ValueTask<BossTowerActionResult> HandleAsync(CommandExecutionContext context,
        AdvanceBossTowerCommand command, CancellationToken token) => towers.NextLevelAsync(command.Context, command.TargetX, command.TargetY, token);
}

public sealed record DieInBossTowerCommand(BossTowerActionContext Context) : BossTowerActionCommand(Context), ICommand<BossTowerActionResult>;
public sealed class DieInBossTowerCommandHandler(IBossTowerDomainService towers)
    : ICommandHandler<DieInBossTowerCommand, BossTowerActionResult>
{
    public ValueTask<BossTowerActionResult> HandleAsync(CommandExecutionContext context,
        DieInBossTowerCommand command, CancellationToken token) => towers.DieAsync(command.Context, token);
}

public sealed record LeaveBossTowerCommand(BossTowerActionContext Context, bool Disconnected)
    : BossTowerActionCommand(Context), ICommand<BossTowerActionResult>;
public sealed class LeaveBossTowerCommandHandler(IBossTowerDomainService towers)
    : ICommandHandler<LeaveBossTowerCommand, BossTowerActionResult>
{
    public ValueTask<BossTowerActionResult> HandleAsync(CommandExecutionContext context,
        LeaveBossTowerCommand command, CancellationToken token) =>
        towers.LeaveAsync(command.Context, command.Disconnected, token);
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

public sealed record SelectBossTowerResultCommand(BossTowerActionContext Context, byte Option)
    : BossTowerActionCommand(Context), ICommand<BossTowerActionResult>;
public sealed class SelectBossTowerResultCommandHandler(IBossTowerDomainService towers)
    : ICommandHandler<SelectBossTowerResultCommand, BossTowerActionResult>
{
    public ValueTask<BossTowerActionResult> HandleAsync(CommandExecutionContext context,
        SelectBossTowerResultCommand command, CancellationToken token) =>
        towers.SelectResultAsync(command.Context, command.Option, token);
}

public sealed record PickupBossTowerItemCommand(BossTowerActionContext Context, ushort GroundUid) : BossTowerActionCommand(Context), ICommand<BossTowerPickupResult>;
public sealed class PickupBossTowerItemCommandHandler(IBossTowerDomainService towers)
    : ICommandHandler<PickupBossTowerItemCommand, BossTowerPickupResult>
{
    public ValueTask<BossTowerPickupResult> HandleAsync(CommandExecutionContext context, PickupBossTowerItemCommand command, CancellationToken token) =>
        towers.PickupAsync(command.Context, command.GroundUid, token);
}

public sealed record ReleaseBossTowerSelectionCommand(BossTowerActionContext Context, int DungeonId) : BossTowerActionCommand(Context), ICommand<BossTowerActionResult>;
public sealed class ReleaseBossTowerSelectionCommandHandler(IBossTowerDomainService towers)
    : ICommandHandler<ReleaseBossTowerSelectionCommand, BossTowerActionResult>
{
    public ValueTask<BossTowerActionResult> HandleAsync(CommandExecutionContext context,
        ReleaseBossTowerSelectionCommand command, CancellationToken token) =>
        towers.ReleaseForSelectionAsync(command.Context, command.DungeonId, token);
}
