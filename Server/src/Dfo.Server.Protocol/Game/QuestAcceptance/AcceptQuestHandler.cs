using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.QuestAcceptance;

public sealed class AcceptQuestHandler(ICommandBus commands) : ClientMessageHandler<AcceptQuestRequest>,
    Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameAcceptQuestCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, AcceptQuestRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameAcceptQuestCommand(context, message), ct).ConfigureAwait(false);
}
