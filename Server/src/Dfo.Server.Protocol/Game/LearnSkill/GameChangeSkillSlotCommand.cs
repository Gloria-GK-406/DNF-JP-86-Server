using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
namespace Dfo.Server.Protocol.Game.LearnSkill;
public sealed record GameChangeSkillSlotCommand(NetworkDispatchContext Dispatch, ChangeSkillSlotRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed class GameChangeSkillSlotCommandHandler(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService users,
    ICharacterSkillLearningDomainService learning, ICommandWorkUnits units, IGameCommandOutbox outbox) : ICommandHandler<GameChangeSkillSlotCommand,bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context,GameChangeSkillSlotCommand command,CancellationToken ct)
    {
        var message=command.Input;
        var session=sessions.FindByConnection(command.Dispatch.Connection.ConnectionId);
        bool success=false;
        if(session is not null && users.TryGetByConnection(command.Dispatch.Connection.ConnectionId,out var user) && user is not null)
            success=await units.WriteAsync(context,new("ChangeSkillSlot",[]),async (_,token)=>await learning.SwapSlotsAsync(session, message.Page, message.First, message.Second, token).ConfigureAwait(false),ct).ConfigureAwait(false);
        if(session is not null && !sessions.IsInWorld(session)) return true;
        await outbox.RegisterCmdAsync(command.Dispatch.Connection.ConnectionId,new ChangeSkillSlotResponse(success, message.Page, message.First, message.Second),ct).ConfigureAwait(false);
        return true;
    }
}
