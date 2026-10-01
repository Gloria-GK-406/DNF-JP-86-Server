using Dfo.Server.Data.Networking;

namespace Dfo.Server.Application.Commands;

/// <summary>Rechecks a request's captured login binding after command admission locks are acquired.</summary>
internal sealed class NetworkCommandAuthorization
{
    private readonly AsyncLocal<Frame?> current = new();

    public GameMessageMetadata? Metadata => current.Value is { Active: true } frame ? frame.Metadata : null;

    public IDisposable Enter(Func<bool> isCurrent, GameMessageMetadata? metadata = null)
    {
        var previous = current.Value;
        var frame = new Frame(isCurrent, metadata);
        current.Value = frame;
        return new Scope(() => { frame.Active = false; current.Value = previous; });
    }

    public void DemandCurrent()
    {
        var frame = current.Value;
        if (frame is { Active: true } && !frame.IsCurrent())
            throw new UnauthorizedAccessException("The originating game connection no longer owns this login session.");
    }

    private sealed class Frame(Func<bool> isCurrent, GameMessageMetadata? metadata)
    {
        public volatile bool Active = true;
        public Func<bool> IsCurrent { get; } = isCurrent;
        public GameMessageMetadata? Metadata { get; } = metadata;
    }

    private sealed class Scope(Action dispose) : IDisposable
    {
        private Action? action = dispose;
        public void Dispose() => Interlocked.Exchange(ref action, null)?.Invoke();
    }
}
