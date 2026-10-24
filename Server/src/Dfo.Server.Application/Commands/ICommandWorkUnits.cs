using Dfo.Server.Abstractions.Transactions;

namespace Dfo.Server.Application.Commands;

public enum CommandWorkUnitKind { Ordinary, MainSettlement }
public sealed record CommandWorkUnitDeclaration(string Name, IReadOnlyList<SelectedSessionRecipient> Participants,
    CommandWorkUnitKind Kind = CommandWorkUnitKind.Ordinary);

public sealed class CommandWorkUnit
{
    private readonly IDomainTransaction transaction;
    private readonly CommandRequestContext request;
    private bool open = true;
    internal CommandWorkUnit(IDomainTransaction transaction, CommandRequestContext request)
    { this.transaction = transaction; this.request = request; }
    public void RequireRollback()
    {
        request.DemandCurrent();
        if (!open) throw new InvalidOperationException("The command work unit has ended.");
        transaction.RequireRollback();
    }
    internal void Close() => open = false;
}

public interface ICommandWorkUnits
{
    ValueTask<TResult> WriteAsync<TResult>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
        Func<CommandWorkUnit, CancellationToken, ValueTask<TResult>> action, CancellationToken cancellationToken = default)
        where TResult : notnull;
    ValueTask<TResult> ReadOnlyAsync<TResult>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
        Func<CancellationToken, ValueTask<TResult>> action, CancellationToken cancellationToken = default)
        where TResult : notnull;
}
