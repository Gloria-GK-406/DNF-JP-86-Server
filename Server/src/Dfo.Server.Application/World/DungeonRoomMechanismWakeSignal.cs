using System.Threading.Channels;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

internal sealed class DungeonRoomMechanismWakeSignal(TimeProvider clock) : IDungeonRoomMechanismWakeSignal
{
    private readonly Channel<bool> wake = System.Threading.Channels.Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    public void Wake() => wake.Writer.TryWrite(true);

    public async ValueTask WaitAsync(DateTimeOffset? deadline, CancellationToken cancellationToken)
    {
        if (deadline is null)
        {
            _ = await wake.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            return;
        }
        var delay = deadline.Value - clock.GetUtcNow();
        if (delay <= TimeSpan.Zero) return;
        using var timeout = new CancellationTokenSource(delay, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try { _ = await wake.Reader.ReadAsync(linked.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested) { }
    }
}
