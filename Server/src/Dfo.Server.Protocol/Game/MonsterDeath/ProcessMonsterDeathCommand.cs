using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.MonsterDeath;

public sealed record ProcessMonsterDeathCommand(MonsterDeathSettlementRequest Request)
    : ICommand<MonsterDeathSettlementResult>, IMultiSelectedSessionCommand
{
    public SelectedSessionIdentity Session => Request.Reporter;
    public IReadOnlyList<SelectedSessionIdentity> Sessions => Request.Participants;
}

public sealed class ProcessMonsterDeathCommandHandler(IMonsterDeathDomainService deaths)
    : ICommandHandler<ProcessMonsterDeathCommand, MonsterDeathSettlementResult>
{
    public ValueTask<MonsterDeathSettlementResult> HandleAsync(CommandExecutionContext context,
        ProcessMonsterDeathCommand command, CancellationToken ct) => deaths.SettleAsync(command.Request, context.LegacyFootprint, ct);
}
