using System.Runtime.ExceptionServices;
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
    private readonly Guid job = GameplayJob.Current?.Id ?? throw new InvalidOperationException("A request begins on a gameplay job.");
    private readonly List<SelectedSessionRecipient> committed = [];
    private Exception? failure;
    private bool closed;
    private bool unitOpen;
    private readonly List<Task> operations = [];
    private CommandRequestContext() { }

    public bool IsClosed => closed;
    internal void DemandCurrent()
    {
        if (closed || GameplayJob.Current?.Id != job)
            throw new InvalidOperationException("The command request is closed or belongs to another job.");
        GameplayJob.DemandHealthy();
        if (failure is { } error) ExceptionDispatchInfo.Capture(error).Throw();
        DemandAuthority();
    }
    protected virtual void DemandAuthority() { }
    internal void BeginUnit()
    {
        DemandCurrent();
        if (unitOpen) throw new InvalidOperationException("Command work units cannot overlap.");
        unitOpen = true;
    }
    internal void TrackOperation(Task operation) => operations.Add(operation);
    internal void EndUnit() => unitOpen = false;
    internal void DemandFinished()
    {
        DemandCurrent();
        if (unitOpen || operations.Any(operation => !operation.IsCompleted))
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
        if (failure is null || Includes(error, failure)) failure = error;
        else if (!Includes(failure, error)) failure = new AggregateException("The command and its work unit both failed.", failure, error);
    }
    private static bool Includes(Exception outer, Exception inner) => ReferenceEquals(outer, inner)
        || outer is AggregateException aggregate && aggregate.InnerExceptions.Any(error => Includes(error, inner))
        || outer.InnerException is { } cause && Includes(cause, inner);
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
    // These states deliberately have no factory until the explicit effect ledger is implemented.
    public sealed class ProtocolEffectRejected : CommandRequestContext { private ProtocolEffectRejected() { } }
    public sealed class ProtocolEffectSelected : CommandRequestContext { private ProtocolEffectSelected() { } }
    public sealed class TrustedInternal : CommandRequestContext
    {
        internal TrustedInternal(InternalCommandOrigin origin) => Origin = origin;
        public InternalCommandOrigin Origin { get; }
    }
}
