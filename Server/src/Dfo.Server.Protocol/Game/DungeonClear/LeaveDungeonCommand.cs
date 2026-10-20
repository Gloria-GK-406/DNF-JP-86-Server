using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.DungeonClear;

public sealed record LeaveDungeonCommand(SelectedSessionIdentity Session,
    IReadOnlyList<SelectedSessionIdentity> Sessions, DungeonRunId RunId)
    : ICommand<LeaveDungeonResult>, IMultiSelectedSessionCommand, IMultiAccountCommand
{
    public IReadOnlyList<AccountId> AccountIds => Sessions.Select(x => x.AccountId).Distinct().ToArray();
}

public sealed record LeaveDungeonResult(IReadOnlyList<CharacterId> EndedCharacters);

public sealed class LeaveDungeonCommandHandler(IDungeonRunDomainService runs)
    : ICommandHandler<LeaveDungeonCommand, LeaveDungeonResult>
{
    public async ValueTask<LeaveDungeonResult> HandleAsync(CommandExecutionContext context,
        LeaveDungeonCommand command, CancellationToken cancellationToken)
    {
        var ended = new List<CharacterId>();
        foreach (var session in command.Sessions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await runs.EndAsync(session.CharacterId, command.RunId).ConfigureAwait(false))
                ended.Add(session.CharacterId);
        }
        return new(ended);
    }
}
