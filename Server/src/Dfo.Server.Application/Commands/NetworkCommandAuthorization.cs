using Dfo.Server.Data.Networking;

namespace Dfo.Server.Application.Commands;

/// <summary>Rechecks a request's captured login binding after command admission locks are acquired.</summary>
internal sealed class NetworkCommandAuthorization
{
    private readonly AsyncLocal<Frame?> current = new();

    public GameMessageMetadata? Metadata => current.Value is { Active: true } frame ? frame.Metadata : null;

    internal enum MessageCategory { Unknown, NonEffect, Effect }
    internal enum SnapshotKind { NoFrame, ActiveUnknown, ActiveNonEffect, ActiveEffect, Closed }

    internal sealed class Snapshot(Frame? frame)
    {
        internal bool IsAbsent => frame is null;
        internal SnapshotKind Kind => frame is null ? SnapshotKind.NoFrame
            : !frame.Active ? SnapshotKind.Closed
            : frame.Category == MessageCategory.Effect ? SnapshotKind.ActiveEffect
            : frame.Category == MessageCategory.NonEffect ? SnapshotKind.ActiveNonEffect
            : SnapshotKind.ActiveUnknown;
        internal void DemandOpen()
        {
            if (Kind == SnapshotKind.Closed)
                throw new InvalidOperationException("The inherited network authorization is closed.");
        }
        internal void DemandAuthority()
        {
            if (frame is { Active: true } && !frame.IsCurrent())
                throw new UnauthorizedAccessException("The originating game connection no longer owns this login session.");
        }
    }

    internal Snapshot Capture() => new(current.Value);

    public IDisposable Enter(Func<bool> isCurrent, GameMessageMetadata? metadata = null,
        MessageCategory category = MessageCategory.Unknown)
    {
        var previous = current.Value;
        var frame = new Frame(isCurrent, metadata, category);
        current.Value = frame;
        return new Scope(() => { frame.Active = false; current.Value = previous; });
    }

    public void DemandCurrent()
    {
        var frame = current.Value;
        if (frame is { Active: true } && !frame.IsCurrent())
            throw new UnauthorizedAccessException("The originating game connection no longer owns this login session.");
    }

    internal sealed class Frame(Func<bool> isCurrent, GameMessageMetadata? metadata, MessageCategory category)
    {
        public volatile bool Active = true;
        internal MessageCategory Category { get; } = category;
        public Func<bool> IsCurrent { get; } = isCurrent;
        public GameMessageMetadata? Metadata { get; } = metadata;
    }

    private sealed class Scope(Action dispose) : IDisposable
    {
        private Action? action = dispose;
        public void Dispose() => Interlocked.Exchange(ref action, null)?.Invoke();
    }
}
