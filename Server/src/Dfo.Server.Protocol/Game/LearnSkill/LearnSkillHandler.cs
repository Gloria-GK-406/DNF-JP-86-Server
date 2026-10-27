using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
namespace Dfo.Server.Protocol.Game.LearnSkill;
public sealed class LearnSkillHandler(ICommandBus commands) : ClientMessageHandler<LearnSkillRequest>, Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameLearnSkillCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, LearnSkillRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameLearnSkillCommand(context, message), ct).ConfigureAwait(false);
}
