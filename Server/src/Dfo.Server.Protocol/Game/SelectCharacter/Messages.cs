using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.SelectCharacter;

/// <summary>
/// CMD 0x0004 <c>SELECT_CHARACTER</c>. The single <c>u16</c> is a position in the roster the
/// server sent, not a slot index -- see protocol/game/client-to-server/0x0004.md.
/// </summary>
public sealed record SelectCharacterRequest(
    GameMessageMetadata Metadata,
    ushort RosterIndex) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameSelectCharacter;
}
