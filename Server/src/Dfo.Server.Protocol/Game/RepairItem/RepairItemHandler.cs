using Dfo.Server.Data.Networking;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;

namespace Dfo.Server.Protocol.Game.RepairItem;

public sealed class RepairItemHandler(ICommandBus commands) : ClientMessageHandler<RepairItemRequest>, IProtocolCommandMessageHandler<GameRepairItemCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        RepairItemRequest message, CancellationToken cancellationToken) =>
        _ = await commands.SendAsync(new GameRepairItemCommand(context, message), cancellationToken).ConfigureAwait(false);
}
