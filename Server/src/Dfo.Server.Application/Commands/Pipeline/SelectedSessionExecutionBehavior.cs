using Dfo.Server.Application.Effects;
using Dfo.Server.Application.OnlineUsers;

namespace Dfo.Server.Application.Commands.Pipeline;

internal sealed class SelectedSessionExecutionBehavior<TResult>(SelectedSessionTransitionGate lifecycle,
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
        await using var lease = await lifecycle.BorrowOrAcquireSharedAsync(session.AccountId, ct).ConfigureAwait(false);
        safety.DemandCurrent(recipient);
        operations.DemandRecipient(context, command, safety);
        using var selected = scope.Enter(recipient);
        return await next().ConfigureAwait(false);
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
