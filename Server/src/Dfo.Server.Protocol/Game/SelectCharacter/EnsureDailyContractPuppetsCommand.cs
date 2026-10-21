using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.SelectCharacter;

public sealed record EnsureDailyContractPuppetsCommand(SelectedSessionIdentity Session) : ICommand<bool>, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}

public sealed class EnsureDailyContractPuppetsCommandHandler(
    ISelectedSessionDomainService sessions, IQuestHelperPuppetDomainService puppets)
    : ICommandHandler<EnsureDailyContractPuppetsCommand, bool>
{
    public ValueTask<bool> HandleAsync(CommandExecutionContext context, EnsureDailyContractPuppetsCommand command,
        CancellationToken token)
    {
        if (!sessions.IsSelected(command.Session))
            throw new InvalidOperationException("Selected session changed before puppet delivery.");
        return puppets.EnsureDailyAsync(command.AccountId, command.Session.CharacterId, token);
    }
}
