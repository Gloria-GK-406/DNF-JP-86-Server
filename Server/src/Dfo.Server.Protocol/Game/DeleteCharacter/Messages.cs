using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.DeleteCharacter;

/// <summary>
/// CMD 0x0006, <c>DELETE_CHARACTER</c>: a roster index, then the length-prefixed name the player
/// typed as confirmation.
/// </summary>
/// <remarks>
/// The index is a <c>u16</c>, which a real request settled. Both this server and the reference read
/// it as a single byte and took the name length from offset 1, which lands on its high byte and
/// yields a nonsense length -- so deletion could never have worked on either. The reference
/// presumably never exercised it.
///
/// It is called a roster index rather than a slot because that is all the client's code supports:
/// the request builder pushes it straight into the accessor that returns the entry at that index of
/// the client's character vector. Whether the vector is packed (so the index is a position) or
/// sized to the slot limit (so it is a slot) is not settled; the two agree on every gap-free
/// roster, which is every sample observed so far.
/// </remarks>
public sealed record DeleteCharacterRequest(
    GameMessageMetadata Metadata,
    ushort RosterIndex,
    byte[]? RawName) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameDeleteCharacter;
}

/// <param name="RosterIndex">
/// The index the request carried, echoed back unchanged. The client uses one index space for both
/// directions, so this is never derived from anything the server knows -- deriving it from the
/// character id removed the wrong entry from the roster view.
/// </param>
public sealed record DeleteCharacterResponse(
    CharacterDeletionStatus Status,
    ushort RosterIndex) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameDeleteCharacter;
}

