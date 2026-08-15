using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.SelectCharacter;

/// <summary>
/// The one outbound message that is not a single frame: the composer expands the select-character
/// result into its ordered static stream, and that order is the order the frames go out.
/// </summary>
internal sealed class SelectCharacterResponseEncoder : ServerMessageEncoder<SelectCharacterResponse>
{
    protected override IReadOnlyList<byte[]> EncodeMessage(SelectCharacterResponse message) =>
        SelectCharacterResponseComposer.Encode(message.Result);
}
