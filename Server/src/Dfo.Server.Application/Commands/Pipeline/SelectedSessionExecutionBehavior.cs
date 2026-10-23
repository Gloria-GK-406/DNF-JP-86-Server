using Dfo.Server.Application.Effects;

namespace Dfo.Server.Application.Commands.Pipeline;

internal sealed class SelectedSessionExecutionBehavior<TResult>(
    SelectedSessionSafety safety, SelectedSessionCommandScope scope, EffectOperationContext operations) : ICommandPipelineBehavior<TResult> where TResult : notnull
{
    public int Order => 45;
    public async ValueTask<TResult> ExecuteAsync(CommandExecutionContext context, ICommand command,
        CommandExecutionDelegate<TResult> next, CancellationToken ct)
    {
        using var main = operations.BeginMain(context, command);
        operations.DemandRecipient(context, command, safety);
        if (command is not ISelectedSessionCommand { Session: { } session }) return await next().ConfigureAwait(false);
        var recipient = safety.Capture(session);
        if (command is IAccountCommand owner && owner.AccountId != session.AccountId)
            throw new InvalidOperationException("Command account differs from its selected session.");
        var targets = command is IMultiSelectedSessionCommand multi ? multi.Sessions : [session];
        if (!targets.Contains(session) || targets.Count == 0)
            throw new InvalidOperationException("The reporting session must belong to the frozen participant set.");
        try
        {
            foreach (var target in targets) safety.DemandCurrent(target);
            safety.DemandCurrent(recipient);
            operations.DemandRecipient(context, command, safety);
            using var selected = scope.Enter(recipient);
            return await next().ConfigureAwait(false);
        }
        catch (Exception error) when (Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException.Contains(error))
        {
            foreach (var target in targets)
                await safety.BlockAsync(new(target, null), error, context.CommandType.Name).ConfigureAwait(false);
            throw;
        }
    }
}

internal sealed class SelectedSessionCommandScope
{
    private readonly AsyncLocal<SelectedSessionRecipient?> current = new();
    public SelectedSessionRecipient? Current => current.Value;
    public IDisposable Enter(SelectedSessionRecipient recipient)
    {
        var previous = current.Value; current.Value = recipient;
        return new Scope(() => current.Value = previous);
    }
    private sealed class Scope(Action dispose) : IDisposable { public void Dispose() => dispose(); }
}
