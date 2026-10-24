using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.EnchantByBead;

public sealed class EnchantByBeadHandler(ICommandBus commands) : ClientMessageHandler<EnchantByBeadRequest>, IProtocolCommandMessageHandler<GameEnchantByBeadCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, EnchantByBeadRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameEnchantByBeadCommand(context, message), ct).ConfigureAwait(false);
}
