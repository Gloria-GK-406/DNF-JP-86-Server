using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
namespace Dfo.Server.Protocol.Game.MonsterDeath;
public sealed record GameMonsterDeathCommand(NetworkDispatchContext Dispatch, MonsterDeathRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
