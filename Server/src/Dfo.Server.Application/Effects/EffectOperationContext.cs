using System.Collections.Immutable;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Effects;

internal enum EffectOperationState { Pending, Committed, ConfirmedRollback, Rejected, Blocked }

internal sealed class EffectOperationEntry(SelectedSessionIdentity session, long ordinal,
    byte selector, ushort type, ushort sequence, string hash, int length)
{
    public SelectedSessionIdentity Session { get; } = session;
    public long Ordinal { get; } = ordinal;
    public byte Selector { get; } = selector;
    public ushort Type { get; } = type;
    public ushort Sequence { get; } = sequence;
    public string Hash { get; } = hash;
    public int Length { get; } = length;
    public EffectOperationState State { get; set; }
    public ReplayProjectionRequest? Projection { get; set; }
}

/// <summary>The synchronous flow frame connects the one primary command to its preallocated ledger entry.</summary>
internal sealed class EffectOperationContext
{
    private readonly AsyncLocal<Frame?> current = new();
    private readonly AsyncLocal<Execution?> execution = new();
    private readonly AsyncLocal<Rejection?> rejection = new();
    private Frame? Current
    {
        get
        {
            if (rejection.Value is { Closed: true }) throw new InvalidOperationException("Inherited unselected operation has already closed.");
            if (current.Value is { Closed: true }) throw new InvalidOperationException("Inherited network operation has already closed.");
            if (execution.Value is { Closed: true }) throw new InvalidOperationException("Inherited command execution has already closed.");
            return current.Value;
        }
    }
    public IDisposable Enter(EffectOperationEntry entry, SelectedSessionRecipient recipient, ReplayProjectionNeeds minimum)
    {
        var previous = current.Value;
        var frame = new Frame(entry, recipient, minimum);
        current.Value = frame;
        return new Scope(() => { frame.Closed = true; current.Value = previous; });
    }
    public IDisposable EnterWithoutSelectedSession()
    {
        var previous = rejection.Value; var frame = new Rejection(); rejection.Value = frame;
        return new Scope(() => { frame.Closed = true; rejection.Value = previous; });
    }
    public IDisposable BeginMain(CommandExecutionContext commandExecution, ICommand command)
    {
        var frame = Current;
        if (rejection.Value is not null && command is IRequestSettlementCommand)
            throw new InvalidOperationException("An operation received without a selected session cannot settle a primary command.");
        if (frame is null) return new Scope(() => { });
        var isMain = command is IRequestSettlementCommand;
        if (command is IRequestSettlementCommand main)
        {
            if (main.Session != frame.Recipient.Session)
                throw new InvalidOperationException("Primary command differs from the operation's captured selected session.");
            lock (frame)
            {
                if (frame.MainExecutionId is not null)
                    throw new InvalidOperationException("A network effect request may execute only one primary settlement.");
                frame.MainExecutionId = commandExecution.ExecutionId;
            }
        }
        var previous = execution.Value;
        var active = new Execution(commandExecution.ExecutionId, isMain);
        execution.Value = active;
        return new Scope(() => { active.Closed = true; execution.Value = previous; });
    }
    private Frame? For(CommandExecutionContext commandExecution, ICommand command)
    {
        var frame = Current;
        return command is IRequestSettlementCommand && frame is not null
            && execution.Value is { IsMain: true, Closed: false } active && active.Id == commandExecution.ExecutionId ? frame : null;
    }
    public void DemandRecipient(CommandExecutionContext execution, ICommand command, SelectedSessionSafety safety)
    { if (For(execution, command) is { } frame) safety.DemandCurrent(frame.Recipient); }
    public void TransactionStarted(CommandExecutionContext execution, ICommand command)
    { if (For(execution, command) is { } frame) frame.TransactionStarted = true; }
    public void Freeze(CommandExecutionContext execution, ICommand command)
    {
        if (For(execution, command) is not { } frame) return;
        var projection = new ReplayProjectionRequest(frame.Needs, frame.Slots.ToImmutableArray()) { TouchedTownAreas = frame.Areas.ToImmutableArray() };
        projection.Validate();
        frame.Entry.Projection = projection;
        frame.Frozen = true;
    }
    // Settlement bookkeeping can still fence an expired inherited flow; it never grants execution rights.
    private Frame? ForSettlement(CommandExecutionContext commandExecution, ICommand command) =>
        command is IRequestSettlementCommand && current.Value is { } frame
            && execution.Value is { IsMain: true } active && active.Id == commandExecution.ExecutionId ? frame : null;
    public void RollbackConfirmed(CommandExecutionContext execution, ICommand command)
    { if (ForSettlement(execution, command) is { } frame) frame.RollbackConfirmed = true; }
    public void Settled(CommandExecutionContext execution, ICommand command)
    {
        if (For(execution, command) is not { } frame) return;
        if (!frame.TransactionStarted)
            throw new InvalidOperationException("A primary settlement returned without a transaction.");
        if (frame.RollbackConfirmed)
        {
            frame.Entry.State = EffectOperationState.ConfirmedRollback;
            return;
        }
        if (!frame.Frozen || frame.Entry.Projection is null)
            throw new InvalidOperationException("A primary settlement returned without a prepared transaction projection.");
        frame.Entry.State = EffectOperationState.Committed;
    }
    public void Failed(CommandExecutionContext execution, ICommand command, bool uncertain)
    {
        if (ForSettlement(execution, command) is not { } frame) return;
        frame.Entry.State = uncertain ? EffectOperationState.Blocked
            : frame.RollbackConfirmed ? EffectOperationState.ConfirmedRollback : EffectOperationState.Rejected;
    }
    internal LegacyFootprintInheritance CaptureInheritance()
    {
        var frame = current.Value; var active = execution.Value; var refused = rejection.Value;
        void DemandOpen()
        {
            if (frame?.Closed == true || active?.Closed == true || refused?.Closed == true)
                throw new InvalidOperationException("The inherited effect operation has closed.");
        }
        return new(frame is null && active is null && refused is null, DemandOpen);
    }

    internal LegacyEffectFootprint CaptureLegacy(CommandExecutionContext context,
        NetworkCommandAuthorization.Snapshot authorization, bool standalone, Action inheritedGuard)
    {
        if (context.Request is not null)
        {
            var error = new InvalidOperationException("An explicit command cannot capture a legacy footprint.");
            context.Request.Failed(error);
            throw error;
        }
        context.DemandLegacyInvocation();
        var frame = current.Value; var active = execution.Value; var refused = rejection.Value;
        string? Unavailable()
        {
            if (frame?.Closed == true || active?.Closed == true || refused?.Closed == true)
                return "The captured effect operation has closed.";
            var kind = authorization.Kind;
            if (frame is not null && refused is not null) return "Contradictory effect operation state.";
            if (kind == NetworkCommandAuthorization.SnapshotKind.ActiveUnknown)
                return "The legacy invocation has no declared network category.";
            if (kind == NetworkCommandAuthorization.SnapshotKind.ActiveNonEffect && (frame is not null || refused is not null))
                return "A non-effect message cannot acquire effect operation authority.";
            if (frame is not null)
            {
                if (active is not { IsMain: true } || active.Id != context.ExecutionId
                    || frame.MainExecutionId != context.ExecutionId || !frame.TransactionStarted || frame.Frozen)
                    return "Inventory footprint requires the captured primary transaction.";
                return null;
            }
            if (refused is not null) return null;
            if (kind == NetworkCommandAuthorization.SnapshotKind.ActiveEffect)
                return "The effect message is missing its operation state.";
            if (kind == NetworkCommandAuthorization.SnapshotKind.ActiveNonEffect || standalone) return null;
            return "The legacy invocation has no explicit footprint source.";
        }
        void DemandOwner(SelectedSessionIdentity owner)
        {
            if (refused is not null) throw new InvalidOperationException("An unselected effect request cannot record a footprint.");
            if (frame is not null && frame.Recipient.Session != owner)
                throw new InvalidOperationException("Inventory footprint differs from its captured owner.");
        }
        void Inventory(IReadOnlyList<BusinessSlot> slots)
        {
            if (frame is null) return;
            frame.Needs |= ReplayProjectionNeeds.Inventory;
            foreach (var slot in slots) frame.Slots.Add(slot);
        }
        void Rental() { if (frame is not null) frame.Needs |= ReplayProjectionNeeds.Rental; }
        void DemandEffectsOwner(EffectFootprintOwner owner)
        {
            if (refused is not null) throw new InvalidOperationException("An unselected effect request cannot apply effects.");
            if (frame is not null && (owner.Session != frame.Recipient.Session
                || owner.AccountId != frame.Recipient.Session.AccountId || owner.CharacterId != frame.Recipient.Session.CharacterId))
                throw new InvalidOperationException("Effect footprint differs from its captured owner.");
        }
        return new(authorization, () => { context.DemandLegacyInvocation(); inheritedGuard(); },
            Unavailable, DemandOwner, Inventory, Rental, DemandEffectsOwner, applied => RecordEffects(frame, applied));
    }
    private static void RecordEffects(Frame? frame, IReadOnlyList<AppliedEffect> applied) => frame?.RecordEffects(applied);
    private sealed class Frame(EffectOperationEntry entry, SelectedSessionRecipient recipient, ReplayProjectionNeeds minimum) : EffectFootprintProjection(minimum)
    {
        public EffectOperationEntry Entry { get; } = entry;
        public SelectedSessionRecipient Recipient { get; } = recipient;
        public Guid? MainExecutionId { get; set; }
        public bool TransactionStarted { get; set; }
        public bool Frozen { get; set; }
        public bool RollbackConfirmed { get; set; }
        public bool Closed { get; set; }
    }
    private sealed class Rejection { public bool Closed { get; set; } }
    private sealed class Execution(Guid id, bool isMain)
    {
        public Guid Id { get; } = id;
        public bool IsMain { get; } = isMain;
        public bool Closed { get; set; }
    }
    private sealed class Scope(Action dispose) : IDisposable
    { private Action? action = dispose; public void Dispose() => Interlocked.Exchange(ref action, null)?.Invoke(); }
}
