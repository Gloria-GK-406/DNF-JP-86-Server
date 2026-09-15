namespace Dfo.Server.Application.Commands.Pipeline;

/// <summary>Admits one transactional command at a time.</summary>
/// <remarks>
/// SQLite allows concurrent readers but only one writer for a database file. A transactional
/// command may read before its first write, so admission has to happen outside the unit of work:
/// waiting after a deferred transaction has taken its read snapshot can leave that transaction
/// unable to upgrade safely when another writer commits first.
///
/// This is deliberately a command-pipeline policy rather than a party or dungeon rule. Domain
/// code expresses the durable effects it needs; the current persistence deployment decides that
/// those effects enter their transactions serially. A storage engine that supports the required
/// concurrent writers can replace this policy without changing those domains.
/// </remarks>
internal sealed class TransactionalExecutionBehavior<TResult>(TransactionalExecutionGate gate)
    : ICommandPipelineBehavior<TResult>
    where TResult : notnull
{
    // The account lease remains outside this gate, and the unit of work remains inside it. No
    // transaction therefore exists while the command waits for its turn.
    public int Order => 57;

    public async ValueTask<TResult> ExecuteAsync(
        CommandExecutionContext context,
        ICommand command,
        CommandExecutionDelegate<TResult> next,
        CancellationToken cancellationToken)
    {
        if (context.ExecutionKind != CommandExecutionKind.Transactional)
        {
            return await next().ConfigureAwait(false);
        }

        await using var lease = await gate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        return await next().ConfigureAwait(false);
    }
}

/// <summary>The process-wide admission gate shared by every closed pipeline result type.</summary>
internal sealed class TransactionalExecutionGate : IDisposable
{
    private readonly SemaphoreSlim gate = new(initialCount: 1, maxCount: 1);

    public async ValueTask<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        await this.gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Lease(this.gate);
    }

    public void Dispose() => this.gate.Dispose();

    private sealed class Lease(SemaphoreSlim gate) : IAsyncDisposable
    {
        private SemaphoreSlim? gate = gate;

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref this.gate, null)?.Release();
            return ValueTask.CompletedTask;
        }
    }
}
