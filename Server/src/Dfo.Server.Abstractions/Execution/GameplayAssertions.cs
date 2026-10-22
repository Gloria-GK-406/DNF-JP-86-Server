using System.Diagnostics;

namespace Dfo.Server.Abstractions.Execution;

/// <summary>
/// Plan transition rule 2, removed with the gates in T-005: a gate of spec appendix F's "delete"
/// column is taken only by a job, and a job never waits on one. Taking it off the executor, or on
/// the executor when it would have to wait, is a program error; on the executor it sticks to the job.
/// </summary>
/// <remarks>
/// Every violation is also written to <see cref="DiagnosticSourceName"/> before it is thrown, because
/// an off-executor violation has no job to stick to and a caller's <c>catch</c> may swallow it. With
/// no subscriber the write is skipped; it is evidence, not a switch.
/// </remarks>
public static class GameplayGateAssertion
{
    public const string DiagnosticSourceName = "Dfo.Server.Gameplay.Assertions";
    public const string ViolationEvent = "Violation";
    public const string OffExecutorKind = "gate-off-executor";
    public const string WaitKind = "gate-wait";

    private static readonly DiagnosticListener Diagnostics = new(DiagnosticSourceName);

    /// <summary>Throws <see cref="GameplayOffExecutorException"/> unless a job is running on this flow.</summary>
    public static void Entering(string gate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gate);
        if (GameplayJob.Active is not null) return;
        throw OffExecutor(gate);
    }

    /// <summary>The gate cannot be entered at once: records the violation on the job and returns it to throw.</summary>
    public static Exception WouldWait(string gate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gate);
        if (GameplayJob.Active is not { } token) return OffExecutor(gate);
        Report(WaitKind, gate);
        return token.Fault(new GameplayGateWaitException(gate, token.Chain));
    }

    /// <summary>Takes <paramref name="gate"/> without waiting, in place of <c>await gate.WaitAsync(ct)</c>.</summary>
    public static void Enter(SemaphoreSlim gate, string name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(gate);
        // Cancellation keeps its old precedence: an already cancelled caller is not a violation.
        cancellationToken.ThrowIfCancellationRequested();
        Entering(name);
        if (!gate.Wait(0)) throw WouldWait(name);
    }

    private static GameplayOffExecutorException OffExecutor(string gate)
    {
        var operation = $"Acquiring gate {gate}";
        Report(OffExecutorKind, operation);
        return new GameplayOffExecutorException(operation);
    }

    private static void Report(string kind, string operation)
    {
        if (Diagnostics.IsEnabled(ViolationEvent))
            Diagnostics.Write(ViolationEvent, new GameplayAssertionViolation(kind, operation));
    }
}

/// <summary>The payload of one <see cref="GameplayGateAssertion.ViolationEvent"/>.</summary>
public sealed record GameplayAssertionViolation(string Kind, string Operation);

/// <summary>Transition rule 2: a job reached a gate it could not enter at once.</summary>
public sealed class GameplayGateWaitException(string gate, GameplayCallChain chain)
    : InvalidOperationException(
        $"Gate {gate} would wait on the gameplay executor (root {chain.RootId}, depth {chain.Depth}: {chain.Describe()}).")
{
    public string Gate { get; } = gate;

    public GameplayCallChain Chain { get; } = chain;
}
