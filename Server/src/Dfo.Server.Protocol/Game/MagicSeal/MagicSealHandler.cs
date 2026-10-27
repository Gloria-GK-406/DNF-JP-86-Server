using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.MagicSeal;

public sealed class MagicSealHandler(ICommandBus commands) : ClientMessageHandler<MagicSealRequest>, IProtocolCommandMessageHandler<GameMagicSealCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, MagicSealRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameMagicSealCommand(context, message), ct).ConfigureAwait(false);
}
