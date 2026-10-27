using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
namespace Dfo.Server.Protocol.Game.ReturnToSelection;
public sealed class ReturnToSelectionHandler(ICommandBus commands) : ClientMessageHandler<ReturnToSelectionRequest>, IProtocolCommandMessageHandler<GameReturnToSelectionCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, ReturnToSelectionRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameReturnToSelectionCommand(context, message), ct).ConfigureAwait(false);
}
