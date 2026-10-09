using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.Exit;

public sealed class GameExitHandler(IGameMessageSender sender) : ClientMessageHandler<GameExitRequest>
{
    protected override ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        GameExitRequest message,
        CancellationToken cancellationToken) =>
        // EXIT also precedes channel switching; it must not revoke the reusable login credential.
        sender.SendCmdAsync(context.Connection.ConnectionId, new GameExitResponse(true), cancellationToken);
}
