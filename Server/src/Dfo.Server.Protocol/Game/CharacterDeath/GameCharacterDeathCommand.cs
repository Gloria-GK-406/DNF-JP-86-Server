using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
namespace Dfo.Server.Protocol.Game.CharacterDeath;
public sealed record GameCharacterDeathCommand(NetworkDispatchContext Dispatch, CharacterDeathRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed record GameUseRevivalCoinCommand(NetworkDispatchContext Dispatch, UseRevivalCoinRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
