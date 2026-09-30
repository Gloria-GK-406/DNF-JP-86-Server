namespace Dfo.Server.Networking.Runtime;

/// <summary>Observes every inbound frame before decoder lookup, including unknown opcodes.</summary>
internal sealed class GameInboundFrameCounter
{
    private readonly Lock gate = new();
    private long ordinal;
    private ushort? latest;

    internal (long Ordinal, bool Fresh) Observe(ushort sequence)
    {
        lock (gate)
        {
            var current = checked(++ordinal);
            var fresh = latest is null || sequence == unchecked((ushort)(latest.Value + 1));
            if (fresh) latest = sequence;
            return (current, fresh);
        }
    }
}
