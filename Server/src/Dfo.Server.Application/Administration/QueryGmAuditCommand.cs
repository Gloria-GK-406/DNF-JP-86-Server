using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Administration;

namespace Dfo.Server.Application.Administration;
public sealed class QueryGmAuditCommand(int page, int pageSize) : ICommand<GmAuditPage>
{
    public int Page { get; } = page;
    public int PageSize { get; } = pageSize;
}
internal sealed class QueryGmAuditCommandHandler(IGmAuditDomainService audit) : ICommandHandler<QueryGmAuditCommand, GmAuditPage>
{
    public ValueTask<GmAuditPage> HandleAsync(CommandExecutionContext context, QueryGmAuditCommand command, CancellationToken cancellationToken) =>
        audit.QueryAsync(command.Page, command.PageSize, cancellationToken);
}
