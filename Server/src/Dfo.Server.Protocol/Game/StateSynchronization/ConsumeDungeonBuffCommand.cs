using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.StateSynchronization;

public sealed record ConsumeDungeonBuffCommand(SelectedSessionIdentity Session, DungeonRunId? RunId)
    : ICommand<PremiumSnapshotResult>, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}

public sealed class ConsumeDungeonBuffCommandHandler(ISelectedSessionDomainService sessions,
    IDungeonRunDomainService runs, IPremiumServiceDomainService premiums,
    IPremiumServiceUsageDomainService usage, TimeProvider clock)
    : ICommandHandler<ConsumeDungeonBuffCommand, PremiumSnapshotResult>
{
    public async ValueTask<PremiumSnapshotResult> HandleAsync(CommandExecutionContext context,
        ConsumeDungeonBuffCommand command, CancellationToken token)
    {
        var session = command.Session;
        if (!sessions.IsInWorld(session))
            return new(new([]), [], clock.GetUtcNow(), false);
        if (command.RunId is { } runId && runs.Find(session.CharacterId)?.RunId == runId
            && !runs.HasUsedDungeonBuff(session.CharacterId, runId)
            && await usage.TryUseDungeonBuffAsync(session.AccountId, session.CharacterId, token).ConfigureAwait(false))
        {
            if (!runs.StageDungeonBuff(session.CharacterId, runId))
                throw new InvalidOperationException("Dungeon changed during buff consumption.");
        }
        return new(new(await premiums.For(session.AccountId).ReadAsync(token).ConfigureAwait(false)),
            await usage.ReadAsync(session.CharacterId, token).ConfigureAwait(false), clock.GetUtcNow());
    }
}
