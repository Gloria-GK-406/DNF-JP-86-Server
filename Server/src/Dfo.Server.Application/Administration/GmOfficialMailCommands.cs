using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Administration;

namespace Dfo.Server.Application.Administration;

public sealed class QueryGmMailCommand(string secret, GmMailQuery kind, GmPlayerFilter? filter = null, Guid id = default, GmMailInput? input = null) : ICommand<GmMailQueryResult>
{
    public string Secret { get; } = secret;
    public GmMailQuery Kind { get; } = kind;
    public GmPlayerFilter Filter { get; } = filter ?? new();
    public Guid Id { get; } = id;
    public GmMailInput? Input { get; } = input;
}
public sealed class EditGmMailCommand(string secret, GmMailEdit kind, Guid id, GmMailEditorRequest input) : ICommand<GmMailDocument>
{
    public string Secret { get; } = secret;
    public GmMailEdit Kind { get; } = kind;
    public Guid Id { get; } = id;
    public GmMailEditorRequest Input { get; } = input;
}
public sealed class SendGmMailCommand(string secret, GmMailPreparation preparation) : ICommand<GmMailSendResult>, IMultiAccountCommand
{
    public string Secret { get; } = secret;
    public GmMailPreparation Preparation { get; } = preparation;
    public IReadOnlyList<AccountId> AccountIds => Preparation.Accounts;
}
public sealed class RecordGmMailFailureCommand(string secret, Guid id, string outcome, string reason) : ICommand<bool>
{
    public string Secret { get; } = secret;
    public Guid Id { get; } = id;
    public string Outcome { get; } = outcome;
    public string Reason { get; } = reason;
}
internal sealed class QueryGmMailCommandHandler(IGmOfficialMailDomainService mail) : ICommandHandler<QueryGmMailCommand, GmMailQueryResult>
{
    public ValueTask<GmMailQueryResult> HandleAsync(CommandExecutionContext context, QueryGmMailCommand command, CancellationToken token) => mail.QueryAsync(command.Secret, command.Kind, command.Filter, command.Id, command.Input, token);
}
internal sealed class EditGmMailCommandHandler(IGmOfficialMailDomainService mail) : ICommandHandler<EditGmMailCommand, GmMailDocument>
{
    public ValueTask<GmMailDocument> HandleAsync(CommandExecutionContext context, EditGmMailCommand command, CancellationToken token) => mail.EditAsync(command.Secret, command.Kind, command.Id, command.Input, token);
}
internal sealed class SendGmMailCommandHandler(IGmOfficialMailDomainService mail) : ICommandHandler<SendGmMailCommand, GmMailSendResult>
{
    public ValueTask<GmMailSendResult> HandleAsync(CommandExecutionContext context, SendGmMailCommand command, CancellationToken token) => mail.SendAsync(command.Secret, command.Preparation, token);
}
internal sealed class RecordGmMailFailureCommandHandler(IGmOfficialMailDomainService mail) : ICommandHandler<RecordGmMailFailureCommand, bool>
{
    public ValueTask<bool> HandleAsync(CommandExecutionContext context, RecordGmMailFailureCommand command, CancellationToken token) => mail.RecordFailureAsync(command.Secret, command.Id, command.Outcome, command.Reason, token);
}
