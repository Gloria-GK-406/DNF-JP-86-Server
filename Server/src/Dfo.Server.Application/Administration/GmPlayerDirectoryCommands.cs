using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.HttpOperations;
using Dfo.Server.Data.Administration;

namespace Dfo.Server.Application.Administration;

public sealed record QueryGmOverviewCommand : HttpOperationCommand;
public sealed record QueryGmAccountsCommand(GmPlayerFilter Filter) : HttpOperationCommand;
public sealed record QueryGmCharactersCommand(GmPlayerFilter Filter) : HttpOperationCommand;
public sealed record FindGmAccountCommand(int Id) : HttpOperationCommand;
public sealed record FindGmCharacterCommand(int Id) : HttpOperationCommand;

internal sealed class GmPlayerDirectoryCommandHandler(IGmPlayerDirectoryDomainService players, ICommandWorkUnits units)
    : ICommandHandler<QueryGmOverviewCommand, HttpRouteReply>, ICommandHandler<QueryGmAccountsCommand, HttpRouteReply>,
      ICommandHandler<QueryGmCharactersCommand, HttpRouteReply>, ICommandHandler<FindGmAccountCommand, HttpRouteReply>,
      ICommandHandler<FindGmCharacterCommand, HttpRouteReply>
{
    public ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, QueryGmOverviewCommand command, CancellationToken token) =>
        Write(context, "query-gm-overview", players.OverviewAsync, token);
    public ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, QueryGmAccountsCommand command, CancellationToken token) =>
        Write(context, "query-gm-accounts", t => players.AccountsAsync(command.Filter, t), token);
    public ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, QueryGmCharactersCommand command, CancellationToken token) =>
        Write(context, "query-gm-characters", t => players.CharactersAsync(command.Filter, t), token);
    public ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, FindGmAccountCommand command, CancellationToken token) =>
        Write(context, "find-gm-account", t => players.AccountAsync(command.Id, t), token);
    public ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, FindGmCharacterCommand command, CancellationToken token) =>
        Write(context, "find-gm-character", t => players.CharacterAsync(command.Id, t), token);

    private ValueTask<HttpRouteReply> Write<T>(CommandExecutionContext context, string name, Func<CancellationToken, ValueTask<T>> action,
        CancellationToken token) where T : notnull
    {
        HttpOperationWork.DemandOrigin(context, GameplayCallSource.Gm);
        return HttpOperationWork.ReplyAsync(context, async () => await HttpOperationWork.WriteAsync(units, context, name, action, token).ConfigureAwait(false));
    }
}
