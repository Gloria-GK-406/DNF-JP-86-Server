using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Administration;

namespace Dfo.Server.Application.Administration;

public sealed class QueryGmCeraCommand(string secret, GmCeraQuery kind, GmPlayerFilter? filter = null, Guid id = default, GmCeraInput? input = null) : ICommand<GmCeraQueryResult>
{
    public string Secret { get; } = secret; public GmCeraQuery Kind { get; } = kind;
    public GmPlayerFilter Filter { get; } = filter ?? new(); public Guid Id { get; } = id; public GmCeraInput? Input { get; } = input;
}
public sealed class GrantGmCeraCommand(string secret, Guid id, GmCeraInput input) : ICommand<GmCeraGrantResult>
{
    public string Secret { get; } = secret; public Guid Id { get; } = id;
    // Freeze the execution scope independently of mutable transport arrays.
    private readonly GmCeraInput frozen = input with { AccountIds = (int[])input.AccountIds.Clone() };
    public GmCeraInput Input => frozen with { AccountIds = (int[])frozen.AccountIds.Clone() };
}
public sealed class RecordGmCeraFailureCommand(string secret, Guid id, string outcome, string reason) : ICommand<bool>
{
    public string Secret { get; } = secret; public Guid Id { get; } = id; public string Outcome { get; } = outcome; public string Reason { get; } = reason;
}
internal sealed class QueryGmCeraCommandHandler(IGmCeraDomainService cera) : ICommandHandler<QueryGmCeraCommand, GmCeraQueryResult>
{
    public ValueTask<GmCeraQueryResult> HandleAsync(CommandExecutionContext context, QueryGmCeraCommand command, CancellationToken token) => cera.QueryAsync(command.Secret, command.Kind, command.Filter, command.Id, command.Input, token);
}
internal sealed class GrantGmCeraCommandHandler(IGmCeraDomainService cera) : ICommandHandler<GrantGmCeraCommand, GmCeraGrantResult>
{
    public ValueTask<GmCeraGrantResult> HandleAsync(CommandExecutionContext context, GrantGmCeraCommand command, CancellationToken token) => cera.GrantAsync(command.Secret, command.Id, command.Input, token);
}
internal sealed class RecordGmCeraFailureCommandHandler(IGmCeraDomainService cera) : ICommandHandler<RecordGmCeraFailureCommand, bool>
{
    public ValueTask<bool> HandleAsync(CommandExecutionContext context, RecordGmCeraFailureCommand command, CancellationToken token) => cera.RecordFailureAsync(command.Secret, command.Id, command.Outcome, command.Reason, token);
}
