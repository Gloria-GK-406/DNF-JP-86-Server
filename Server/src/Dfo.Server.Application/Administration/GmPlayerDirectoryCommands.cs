using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Administration;

namespace Dfo.Server.Application.Administration;

public sealed record QueryGmAccountsCommand(GmPlayerFilter Filter) : ICommand<GmPage<GmAccountView>>;
public sealed record QueryGmCharactersCommand(GmPlayerFilter Filter) : ICommand<GmPage<GmCharacterView>>;
public sealed record FindGmAccountCommand(int Id) : ICommand<GmAccountLookup>;
public sealed record FindGmCharacterCommand(int Id) : ICommand<GmCharacterLookup>;
public sealed record QueryGmOverviewCommand : ICommand<GmOverview>;

internal sealed class QueryGmAccountsCommandHandler(IGmPlayerDirectoryDomainService players) : ICommandHandler<QueryGmAccountsCommand, GmPage<GmAccountView>>
{
    public ValueTask<GmPage<GmAccountView>> HandleAsync(CommandExecutionContext context, QueryGmAccountsCommand command, CancellationToken token) => players.AccountsAsync(command.Filter, token);
}
internal sealed class QueryGmCharactersCommandHandler(IGmPlayerDirectoryDomainService players) : ICommandHandler<QueryGmCharactersCommand, GmPage<GmCharacterView>>
{
    public ValueTask<GmPage<GmCharacterView>> HandleAsync(CommandExecutionContext context, QueryGmCharactersCommand command, CancellationToken token) => players.CharactersAsync(command.Filter, token);
}
internal sealed class FindGmAccountCommandHandler(IGmPlayerDirectoryDomainService players) : ICommandHandler<FindGmAccountCommand, GmAccountLookup>
{
    public ValueTask<GmAccountLookup> HandleAsync(CommandExecutionContext context, FindGmAccountCommand command, CancellationToken token) => players.AccountAsync(command.Id, token);
}
internal sealed class FindGmCharacterCommandHandler(IGmPlayerDirectoryDomainService players) : ICommandHandler<FindGmCharacterCommand, GmCharacterLookup>
{
    public ValueTask<GmCharacterLookup> HandleAsync(CommandExecutionContext context, FindGmCharacterCommand command, CancellationToken token) => players.CharacterAsync(command.Id, token);
}
internal sealed class QueryGmOverviewCommandHandler(IGmPlayerDirectoryDomainService players) : ICommandHandler<QueryGmOverviewCommand, GmOverview>
{
    public ValueTask<GmOverview> HandleAsync(CommandExecutionContext context, QueryGmOverviewCommand command, CancellationToken token) => players.OverviewAsync(token);
}
