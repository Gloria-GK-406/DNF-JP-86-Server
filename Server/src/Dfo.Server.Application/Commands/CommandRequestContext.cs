using System.Runtime.ExceptionServices;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Effects;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Application.Commands;

public abstract class ProtocolFrameMetadata
{
    private ProtocolFrameMetadata() { }
    public sealed class Game(GameMessageMetadata value) : ProtocolFrameMetadata { public GameMessageMetadata Value { get; } = value; }
    public sealed class Channel(ChannelMessageMetadata value) : ProtocolFrameMetadata { public ChannelMessageMetadata Value { get; } = value; }
}

public interface IProtocolCommand : ICommand
{
    NetworkDispatchContext Dispatch { get; }
    IClientNetworkMessage Message { get; }
    ProtocolFrameMetadata Frame { get; }
}

public interface IProtocolCommand<TResult> : ICommand<TResult>, IProtocolCommand where TResult : notnull;

public abstract class CommandRequestContext
{
    internal TimerReadOnlyCancellationCapability TimerReadOnlyCancellation { get; set; }

    private readonly Guid job = GameplayJob.Current?.Id ?? throw new InvalidOperationException("A request begins on a gameplay job.");
    private readonly List<SelectedSessionRecipient> committed = [];
    private Exception? failure;
    private readonly List<(object Source, Exception Error)> failures = [];
    private readonly List<CommandRegistrationAttempt> attempts = [];
    private ScopeCleanupPhase? scopeCleanup;
    internal IReadOnlyCollection<CommandRegistrationExit> RegistrationExits { get; set; } = [];
    internal CommandSelfSettlementCapability SelfSettlement { get; set; }
    private object? pendingSelfSettlement;
    internal void BeginSelfSettlement(object call)
    {
        DemandCurrent();
        pendingSelfSettlement = call;
    }
    internal void DemandSelfSettlementCurrent(object? call)
    {
        var previous = failure;
        try
        {
            DemandRegistrationCurrentCore();
            if (!ReferenceEquals(pendingSelfSettlement, call))
                throw new InvalidOperationException("The self-settlement handoff belongs to another operation.");
            DemandNoIncompleteAttempt();
        }
        catch (Exception error)
        {
            // A real authority/current check throwing again is a new occurrence, even for the same object.
            if (previous is null) FailedIndependent(error, new object());
            throw;
        }
    }
    internal void EndSelfSettlement(object call)
    {
        DemandSelfSettlementCurrent(call);
        pendingSelfSettlement = null;
    }
    private bool closed;
    private bool unitOpen;
    private CommandWorkUnit? activeUnit;
    private object? selectedSessionCleanup;
    internal CommandSelectedSessionCleanupCapability SelectedSessionCleanup { get; set; }
    internal void AttachUnit(CommandWorkUnit unit) => activeUnit = unit;
    internal object BeginSelectedSessionCleanup(CommandWorkUnit? unit)
    {
        DemandSelectedSessionCleanupLifetime();
        if (selectedSessionCleanup is not null || pendingSelfSettlement is not null
            || (unit is null ? !OperationsCompleted : !ReferenceEquals(unit, activeUnit) || !unit.IsInAction))
            throw new InvalidOperationException("Selected-session cleanup requires its sole live action or completed operations.");
        // A first authority loss is a failure, never a way to finish the protocol successfully.
        try { DemandCurrent(); }
        catch (Exception error) { Failed(error); }
        var invocation = new object();
        if (Interlocked.CompareExchange(ref selectedSessionCleanup, invocation, null) is not null)
            throw new InvalidOperationException("Selected-session cleanup is already in progress.");
        return invocation;
    }
    private void DemandSelectedSessionCleanupLifetime()
    {
        if (closed || GameplayJob.Current?.Id != job || this is not ProtocolNonEffect
            || SelectedSessionCleanup != CommandSelectedSessionCleanupCapability.SelectedSessionLifecycle)
            throw new InvalidOperationException("The request has no live selected-session cleanup permission.");
    }
    internal void DemandSelectedSessionCleanup(object invocation, CommandWorkUnit? unit)
    {
        DemandSelectedSessionCleanupLifetime();
        if (!ReferenceEquals(selectedSessionCleanup, invocation) || unit is not null
            && (!ReferenceEquals(activeUnit, unit) || !unit.IsInAction))
            throw new InvalidOperationException("The selected-session cleanup invocation has ended.");
    }
    internal void EndSelectedSessionCleanup(object invocation)
    { Interlocked.CompareExchange(ref selectedSessionCleanup, null, invocation); }

    private readonly List<Task> operations = [];
    private CommandRequestContext() { }

    public bool IsClosed => closed;
    public void DemandCurrent()
    {
        DemandRegistrationCurrent();
        DemandNoIncompleteAttempt();
    }
    internal void DemandRegistrationCurrent()
    {
        if (pendingSelfSettlement is not null)
        {
            var error = new InvalidOperationException("Self settlement is pending complete cleanup.");
            FailedIndependent(error, new object());
            throw error;
        }
        var previous = failure;
        try { DemandRegistrationCurrentCore(); }
        catch (Exception error)
        {
            if (SelfSettlement == CommandSelfSettlementCapability.SelfSettledAction && previous is null)
                FailedIndependent(error, new object());
            throw;
        }
    }
    private void DemandRegistrationCurrentCore()
    {
        if (closed || GameplayJob.Current?.Id != job)
            throw new InvalidOperationException("The command request is closed or belongs to another job.");
        GameplayJob.DemandHealthy();
        if (failure is { } error) { HttpFailure?.Invalidate(); ExceptionDispatchInfo.Capture(error).Throw(); }
        DemandAuthority();
    }
    protected virtual void DemandAuthority() { }
    internal void BeginUnit()
    {
        DemandCurrent();
        DemandNoIncompleteAttempt();
        if (selectedSessionCleanup is not null) throw new InvalidOperationException("Selected-session cleanup is in progress.");
        if (unitOpen) throw new InvalidOperationException("Command work units cannot overlap.");
        unitOpen = true;
    }
    internal void TrackOperation(Task operation) => operations.Add(operation);
    internal void EndUnit() { unitOpen = false; activeUnit = null; }
    internal void DemandFinished()
    {
        DemandCurrent();
        DemandNoIncompleteAttempt();
        if (this is ProtocolEffectSelected selected) selected.State.DemandFinished();
        if (selectedSessionCleanup is not null || unitOpen || operations.Any(operation => !operation.IsCompleted))
            throw new InvalidOperationException("A command returned with an unfinished work unit.");
    }
    internal async ValueTask DrainOperationsAsync()
    {
        foreach (var operation in operations.ToArray())
        {
            try { await operation.ConfigureAwait(false); }
            catch (Exception error) { Failed(error); }
        }
    }
    internal void Committed(IReadOnlyList<SelectedSessionRecipient> participants) => committed.AddRange(participants);
    internal void Failed(Exception error)
    {
        if (scopeCleanup is { } phase) { FailedDuringScope(phase, error); return; }
        // Propagating the same fault through unit/body/drain is one occurrence.
        if (failures.Any(item => Includes(error, item.Error)))
        {
            if (failure is not null && Includes(error, failure)) failure = error;
            return;
        }
        FailedIndependent(error, new object());
    }
    internal HttpRouteOperation? HttpOperation { get; set; }
    internal HttpCommandFailureState? HttpFailure { get; set; }
    internal object[] FailureSources() => failures.Select(item => item.Source).ToArray();
    internal void DemandHttpFailureAuditLifetime()
    {
        if (closed || GameplayJob.Current?.Id != job || this is not TrustedInternal { Origin.Kind: GameplayCallSource.Http or GameplayCallSource.Gm }
            || HttpFailure is null || failure is null)
            throw new InvalidOperationException("The HTTP failure audit is outside its owning failed request.");
        GameplayJob.DemandHealthy();
    }
    internal void BeginHttpFailureAuditUnit()
    {
        DemandHttpFailureAuditLifetime();
        if (unitOpen || selectedSessionCleanup is not null || pendingSelfSettlement is not null)
            throw new InvalidOperationException("The HTTP failure audit cannot overlap another operation.");
        unitOpen = true;
    }
    internal ProducerCancellationObservation? ProducerCancellation { get; set; }
    internal bool OperationsCompleted => !unitOpen && operations.All(operation => operation.IsCompleted);
    /// <summary>Observes one propagated failure; an independent second occurrence is never the same event.</summary>
    public bool HasOnlyFailure(Exception cause) => failures.Count == 1 && ReferenceEquals(failures[0].Error, cause);
    internal bool HasOnlyFailure(object source, Exception error) => failures.Count == 1
        && ReferenceEquals(failures[0].Source, source) && ReferenceEquals(failures[0].Error, error);
    internal void FailedIndependent(Exception error, object source)
    {
        ProducerCancellation?.FailureRecorded(source);
        if (failures.Any(item => ReferenceEquals(item.Source, source))) return;
        failures.Add((source, error));
        failure = failure is null ? error : new AggregateException("Independent command failures.", failure, error);
    }
    internal void BeginScopeCleanup(object source)
    {
        if (scopeCleanup is not null) throw new InvalidOperationException("Scope cleanup is already active.");
        scopeCleanup = new(source, failure);
    }
    internal void EndScopeCleanup() => scopeCleanup = null;
    private void FailedDuringScope(ScopeCleanupPhase phase, Exception error)
    {
        ProducerCancellation?.Invalidate();
        // Only reports from this cleanup phase can establish propagation. A matching body cause
        // is still independent when disposal throws it again.
        if (phase.Reports.Any(known => Includes(known, error))) return;
        if (error is AggregateException aggregate && aggregate.InnerExceptions.Count > 0)
            foreach (var child in aggregate.InnerExceptions) FailedDuringScope(phase, child);
        else if (error.InnerException is { } inner && phase.Reports.Any(known => Includes(error, known)))
            FailedDuringScope(phase, inner);
        else if (!phase.Reports.Any(known => Includes(error, known)))
            FailedIndependent(error, new ScopeFailureOccurrence(phase.Source, error));

        // Preserve the original wrapper/aggregate while replacing its already reported children.
        // E + reported C + disposal Aggregate(C,D) becomes E + Aggregate(C,D), never E+C+C or E+C without D.
        phase.Reports.RemoveAll(known => Includes(error, known));
        phase.Reports.Add(error);
        var scoped = phase.Reports.Count == 1 ? phase.Reports[0]
            : new AggregateException("Independent scope cleanup failures.", phase.Reports);
        failure = phase.Before is null ? scoped
            : new AggregateException("The command and scope cleanup failed.", phase.Before, scoped);
    }
    private sealed class ScopeCleanupPhase(object source, Exception? before)
    {
        internal object Source { get; } = source;
        internal Exception? Before { get; } = before;
        internal List<Exception> Reports { get; } = [];
    }
    private sealed record ScopeFailureOccurrence(object Scope, Exception Error);
    private static bool Includes(Exception outer, Exception inner) => ReferenceEquals(outer, inner)
        || outer is AggregateException aggregate && aggregate.InnerExceptions.Any(error => Includes(error, inner))
        || outer.InnerException is { } cause && Includes(cause, inner);
    private void DemandNoIncompleteAttempt()
    {
        if (attempts.Any(attempt => !attempt.Completed))
        {
            var error = new InvalidOperationException("A command registration attempt has not completed.");
            Failed(error);
            throw error;
        }
    }
    public ICommandRegistrationAttempt BeginRegistrationAttempt(CommandRegistrationExit exit, NetworkConnectionContext connection)
    {
        try
        {
            DemandCurrent();
            DemandNoIncompleteAttempt();
            if (this is not ProtocolEffectSelected selected || !RegistrationExits.Contains(exit))
                throw new InvalidOperationException("The command has no permission for this registration exit.");
            if (unitOpen || operations.Any(operation => !operation.IsCompleted))
                throw new InvalidOperationException("Registration requires every work unit to finish.");
            selected.State.DemandRegistration(exit, connection);
            var attempt = new CommandRegistrationAttempt(this, selected.State, exit, connection);
            attempts.Add(attempt);
            return attempt;
        }
        catch (Exception error) { Failed(error); GameplayJob.RecordProgramError(error); throw; }
    }

    internal bool RetainsRawEncoderFailure(Exception error) => failures.Count == 1
        && ReferenceEquals(failures[0].Error, error)
        && !TransactionOutcomeUncertainException.Contains(error)
        && this is ProtocolEffectSelected selected && selected.State.Entry.State == EffectOperationState.Committed
        && attempts.Any(attempt => ReferenceEquals(attempt.EncoderFailure, error));
    internal Exception? Failure => failure;
    internal IReadOnlyList<SelectedSessionRecipient> CommittedParticipants => committed;
    internal void Uncertain(IReadOnlyList<SelectedSessionRecipient> participants) => committed.AddRange(participants);
    internal bool HasCommittedUnit { get; private set; }
    internal void ConfirmCommit(IReadOnlyList<SelectedSessionRecipient> participants)
    { HasCommittedUnit = true; Committed(participants); }
    internal void Close() => closed = true;

    public sealed class ProtocolNonEffect : CommandRequestContext
    {
        private readonly Func<bool> authority;
        internal ProtocolNonEffect(NetworkDispatchContext dispatch, IClientNetworkMessage message,
            ProtocolFrameMetadata metadata, Func<bool> authority)
        { Dispatch = dispatch; Message = message; Metadata = metadata; this.authority = authority; }
        public NetworkDispatchContext Dispatch { get; }
        public IClientNetworkMessage Message { get; }
        public ProtocolFrameMetadata Metadata { get; }
        protected override void DemandAuthority()
        {
            if (!authority()) throw new InvalidOperationException("The protocol request lost its captured authority.");
        }
    }
    public sealed class ProtocolEffectRejected : CommandRequestContext
    {
        private readonly Func<bool> authority;
        internal ProtocolEffectRejected(NetworkDispatchContext dispatch, IClientNetworkMessage message,
            ProtocolFrameMetadata metadata, Func<bool> authority)
        { Dispatch = dispatch; Message = message; Metadata = metadata; this.authority = authority; }
        public NetworkDispatchContext Dispatch { get; }
        public IClientNetworkMessage Message { get; }
        public ProtocolFrameMetadata Metadata { get; }
        protected override void DemandAuthority()
        { if (!authority()) throw new InvalidOperationException("The rejected effect request lost its authority."); }
    }
    public sealed class ProtocolEffectSelected : CommandRequestContext
    {
        private readonly Func<bool> authority;
        internal ProtocolEffectSelected(NetworkDispatchContext dispatch, IClientNetworkMessage message,
            ProtocolFrameMetadata metadata, Func<bool> authority, ExplicitEffectRequest state)
        { Dispatch = dispatch; Message = message; Metadata = metadata; this.authority = authority; State = state; state.Request = this; }
        public NetworkDispatchContext Dispatch { get; }
        public IClientNetworkMessage Message { get; }
        public ProtocolFrameMetadata Metadata { get; }
        public SelectedSessionRecipient Recipient => State.Recipient;
        internal ExplicitEffectRequest State { get; }
        protected override void DemandAuthority()
        {
            if (!authority()) throw new InvalidOperationException("The selected effect request lost its authority.");
            State.DemandRecipient();
        }
    }
    public sealed class TrustedInternal : CommandRequestContext
    {
        internal TrustedInternal(InternalCommandOrigin origin) => Origin = origin;
        public InternalCommandOrigin Origin { get; }
    }
}
