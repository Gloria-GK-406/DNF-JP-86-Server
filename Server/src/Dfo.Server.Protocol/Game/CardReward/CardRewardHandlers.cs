using Dfo.Server.Data.Networking;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
namespace Dfo.Server.Protocol.Game.CardReward;
public sealed class CardSelectRightStateHandler(ICommandBus commands) : ClientMessageHandler<CardSelectRightStateRequest>, Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameCardSelectRightStateCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, CardSelectRightStateRequest message, CancellationToken token) =>
        _ = await commands.SendAsync(new GameCardSelectRightStateCommand(context, message), token).ConfigureAwait(false);
}
public sealed class SelectCardHandler(ICommandBus commands) : ClientMessageHandler<SelectCardRequest>, Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameSelectCardCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, SelectCardRequest message, CancellationToken token) =>
        _ = await commands.SendAsync(new GameSelectCardCommand(context, message), token).ConfigureAwait(false);
}
