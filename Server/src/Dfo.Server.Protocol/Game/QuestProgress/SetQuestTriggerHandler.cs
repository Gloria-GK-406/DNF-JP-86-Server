using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.QuestProgress;

public sealed class SetQuestTriggerHandler(ICommandBus commands) : ClientMessageHandler<SetQuestTriggerRequest>,
    Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameSetQuestTriggerCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, SetQuestTriggerRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameSetQuestTriggerCommand(context, message), ct).ConfigureAwait(false);
}
