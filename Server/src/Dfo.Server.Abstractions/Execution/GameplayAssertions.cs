using System.Diagnostics;

namespace Dfo.Server.Abstractions.Execution;

/// <summary>
/// The one diagnostic source every gameplay assertion reports to: the write, network-wait and leaked-flow
/// assertions below. Plan transition rule 2's gate assertion, which reported here too, was deleted with the
/// two party delivery gates in T-006; the source's name and event name did not change.
/// </summary>
/// <remarks>
/// Every violation is written here before it is thrown, because an off-executor violation has no job to
/// stick to and a caller's <c>catch</c> may swallow it. With no subscriber the write is skipped; it is
/// evidence, not a switch.
/// </remarks>
public static class GameplayAssertionDiagnostics
{
    public const string DiagnosticSourceName = "Dfo.Server.Gameplay.Assertions";
    public const string ViolationEvent = "Violation";

    private static readonly DiagnosticListener Diagnostics = new(DiagnosticSourceName);

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
/// finished job's token counts as off the executor.
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
        GameplayAssertionDiagnostics.Report(OffExecutorKind, operation);
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
        GameplayAssertionDiagnostics.Report(NetworkWaitKind, operation);
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
        GameplayAssertionDiagnostics.Report(LeakedFlowKind, operation);
        return new GameplayLeakedFlowException(operation, finished.Chain);
    }
}

/// <summary>The payload of one <see cref="GameplayAssertionDiagnostics.ViolationEvent"/>.</summary>
public sealed record GameplayAssertionViolation(string Kind, string Operation);

/// <summary>F-58: <see cref="GameplayLeakAssertion"/> found a flow leaked from a finished job.</summary>
public sealed class GameplayLeakedFlowException(string operation, GameplayCallChain chain)
    : InvalidOperationException(
        $"{operation} was called on a flow leaked from a finished gameplay job (root {chain.RootId}, depth {chain.Depth}: {chain.Describe()}).")
{
    public string Operation { get; } = operation;

    public GameplayCallChain Chain { get; } = chain;
}
