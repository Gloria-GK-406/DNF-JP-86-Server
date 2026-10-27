using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
namespace Dfo.Server.Protocol.Game.Altar;

public sealed class AltarPrepareHandler(ICommandBus commands) : ClientMessageHandler<AltarPrepareRequest>,
    Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameAltarPrepareCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, AltarPrepareRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameAltarPrepareCommand(context, message), ct).ConfigureAwait(false);
}
public sealed class AltarDeathBatchHandler(ICommandBus commands) : ClientMessageHandler<AltarDeathBatchRequest>,
    Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameAltarDeathBatchCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, AltarDeathBatchRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameAltarDeathBatchCommand(context, message), ct).ConfigureAwait(false);
}
public sealed class AltarDifficultyHandler(ICommandBus commands) : ClientMessageHandler<AltarDifficultyRequest>,
    Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameAltarDifficultyCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, AltarDifficultyRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameAltarDifficultyCommand(context, message), ct).ConfigureAwait(false);
}
