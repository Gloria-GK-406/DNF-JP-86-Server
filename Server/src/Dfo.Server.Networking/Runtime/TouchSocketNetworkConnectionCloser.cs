using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Networking.Runtime;

internal sealed class TouchSocketNetworkConnectionCloser(TouchSocketConnectionRegistry connections)
    : INetworkConnectionCloser
{
    public async ValueTask CloseAsync(
        NetworkConnectionId connectionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var connection = connections.Get(connectionId);
        await connection.Session.CloseAsync("select-character entry failed");
    }
}
