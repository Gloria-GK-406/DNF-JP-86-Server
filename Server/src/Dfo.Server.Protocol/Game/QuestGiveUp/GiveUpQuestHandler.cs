using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.QuestGiveUp;

public sealed class GiveUpQuestHandler(ICommandBus commands) : ClientMessageHandler<GiveUpQuestRequest>,
    Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameGiveUpQuestCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, GiveUpQuestRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameGiveUpQuestCommand(context, message), ct).ConfigureAwait(false);
}
