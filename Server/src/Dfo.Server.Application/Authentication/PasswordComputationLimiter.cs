using Dfo.Server.Abstractions.Execution;

namespace Dfo.Server.Application.Authentication;

/// <summary>
/// RQ-007: the one way to hash or verify a password. At most one computation per processor runs at a
/// time; the rest wait in arrival order and are never refused. Calling it from a gameplay job is a
/// program error, so no job ever waits here.
/// </summary>
/// <remarks>
/// The computation runs on the caller's thread, as it did before. A caller holds a slot only while it
/// computes, never while it then waits for its CMD.
/// </remarks>
internal sealed class PasswordComputationLimiter
{
    private readonly SemaphoreSlim slots;

    public PasswordComputationLimiter() : this(Environment.ProcessorCount) { }

    internal PasswordComputationLimiter(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Capacity = capacity;
        slots = new SemaphoreSlim(capacity, capacity);
    }

    internal int Capacity { get; }

    public async ValueTask<T> RunAsync<T>(Func<T> compute, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compute);
        GameplayJob.AssertNotOnExecutor("Password hashing");
        await slots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return compute();
        }
        finally
        {
            slots.Release();
        }
    }
}
