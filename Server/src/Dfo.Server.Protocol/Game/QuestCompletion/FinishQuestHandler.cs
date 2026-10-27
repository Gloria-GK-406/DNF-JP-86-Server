using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.QuestCompletion;

public sealed class FinishQuestHandler(ICommandBus commands) : ClientMessageHandler<FinishQuestRequest>,
    Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameFinishQuestCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, FinishQuestRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameFinishQuestCommand(context, message), ct).ConfigureAwait(false);
}
