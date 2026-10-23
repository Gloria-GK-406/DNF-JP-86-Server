using System.Diagnostics;

namespace Dfo.Server.Abstractions.Execution;

/// <summary>
/// Plan transition rule 2, removed with the two party delivery gates in T-006: a gate of spec appendix F's "delete"
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

    /// <summary>The one diagnostic source every gameplay assertion reports to.</summary>
    internal static void Report(string kind, string operation)
    {
        if (Diagnostics.IsEnabled(ViolationEvent))
            Diagnostics.Write(ViolationEvent, new GameplayAssertionViolation(kind, operation));
    }
}

/// <summary>
/// RQ-061, the half that does not wait for the outbox: a write unit of work, and every write member of
/// the spec appendix F "move onto the executor" types, refuses a caller off the executor. Reads are not
/// asserted. On a job it is one check of the running token and nothing else; a flow still carrying a
/// finished job's token counts as off the executor, as it does for the gates.
/// </summary>
public static class GameplayWriteAssertion
{
    public const string OffExecutorKind = "write-off-executor";

    /// <summary>Throws <see cref="GameplayOffExecutorException"/> unless a job is running on this flow.</summary>
    /// <param name="entry">The asserted entry, written as <c>Type.Member</c>.</param>
    public static void Writing(string entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entry);
        if (GameplayJob.Active is not null) return;
        var operation = $"Writing {entry}";
        GameplayGateAssertion.Report(OffExecutorKind, operation);
        throw new GameplayOffExecutorException(operation);
    }
}

/// <summary>
/// RQ-003, the network half: a job never writes to or closes a transport connection, because doing so
/// would wait on the network. The outbound sender's loops run detached and never carry a token, so in
/// production this cannot fire; it guards against a job touching a resolved connection directly.
/// </summary>
public static class GameplayNetworkWaitAssertion
{
    public const string NetworkWaitKind = "network-wait";

    /// <summary>Throws a self-wait that sticks to the job when a job is running on this flow.</summary>
    /// <param name="operation">The transport operation, e.g. <c>OutboundConnection.SendAsync</c>.</param>
    public static void Touching(string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        if (GameplayJob.Active is not { } token) return;
        GameplayGateAssertion.Report(NetworkWaitKind, operation);
        throw token.Fault(new GameplaySelfWaitException($"Waiting for the network ({operation})", token.Chain));
    }
}

/// <summary>
/// F-58: a flow that still carries the token of a job that has ended (a continuation started inside the
/// job without suppressing the flow) is a leak, not an off-executor caller, and must not reach the bus
/// as a fresh root. <see cref="GameplayJob.RunDetached(Action)"/> clears the token, so a detached call is
/// not a leak. To be reviewed when T-008 turns the frame into explicit data.
/// </summary>
public static class GameplayLeakAssertion
{
    public const string LeakedFlowKind = "bus-leaked-flow";

    /// <summary>Reports and returns the error to throw when this flow leaked from a finished job; otherwise null.</summary>
    public static Exception? LeakedFrom(string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        if (GameplayJob.Running.Value is not { IsActive: false } finished) return null;
        GameplayGateAssertion.Report(LeakedFlowKind, operation);
        return new GameplayLeakedFlowException(operation, finished.Chain);
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

/// <summary>F-58: <see cref="GameplayLeakAssertion"/> found a flow leaked from a finished job.</summary>
public sealed class GameplayLeakedFlowException(string operation, GameplayCallChain chain)
    : InvalidOperationException(
        $"{operation} was called on a flow leaked from a finished gameplay job (root {chain.RootId}, depth {chain.Depth}: {chain.Describe()}).")
{
    public string Operation { get; } = operation;

    public GameplayCallChain Chain { get; } = chain;
}
