using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.EquipmentDurability;

internal sealed class DecreaseDurabilityMessageDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length != 1)
            throw new InvalidDataException($"DECREASE_DURABILITY requires 1 body byte; received {body.Length}.");
        return new DecreaseDurabilityRequest(metadata, body[0]);
    }
}
