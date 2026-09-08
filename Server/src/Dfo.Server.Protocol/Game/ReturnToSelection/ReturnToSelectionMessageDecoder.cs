using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.ReturnToSelection;

internal sealed class ReturnToSelectionMessageDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (!body.IsEmpty)
            throw new InvalidDataException("RETURN_SELECT_CHARACTER requires an empty body.");
        return new ReturnToSelectionRequest(metadata);
    }
}
