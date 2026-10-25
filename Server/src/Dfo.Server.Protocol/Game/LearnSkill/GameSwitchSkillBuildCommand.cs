using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
namespace Dfo.Server.Protocol.Game.LearnSkill;
public sealed record GameSwitchSkillBuildCommand(NetworkDispatchContext Dispatch, SwitchSkillBuildRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed class GameSwitchSkillBuildCommandHandler(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService users,
    ICharacterSkillLearningDomainService learning, ICommandWorkUnits units, IGameCommandOutbox outbox) : ICommandHandler<GameSwitchSkillBuildCommand,bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context,GameSwitchSkillBuildCommand command,CancellationToken ct)
    {
        var message=command.Input;
        var session=sessions.FindByConnection(command.Dispatch.Connection.ConnectionId);
        int? active=null;
        if(session is not null && users.TryGetByConnection(command.Dispatch.Connection.ConnectionId,out var user) && user is not null)
            active=(await units.WriteAsync(context,new("SwitchSkillBuild",[]),async (_,token)=>new SwitchSkillBuildResponse(await learning.SwitchAsync(session, message.Current, 1 - message.Current, token).ConfigureAwait(false)),ct).ConfigureAwait(false)).Active;
        if(session is not null && !sessions.IsInWorld(session)) return true;
        await outbox.RegisterCmdAsync(command.Dispatch.Connection.ConnectionId,new SwitchSkillBuildResponse(active),ct).ConfigureAwait(false);
        return true;
    }
}
