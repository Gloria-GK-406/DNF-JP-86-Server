using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
namespace Dfo.Server.Protocol.Game.GroundPickup;
public sealed record GameGetItemCommand(NetworkDispatchContext Dispatch, GetItemRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
