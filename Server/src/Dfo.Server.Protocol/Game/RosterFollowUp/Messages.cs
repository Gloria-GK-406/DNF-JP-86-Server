using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.RosterFollowUp;

/// <summary>
/// A two-byte report (cmd 0x01/type 0x02A8) carrying a single u16, sent right after the character
/// roster arrives. All three client builder sites write one runtime u16 and nothing else. The
/// observed value is 0xFFFF, which is the same no-target sentinel GET_USERINFO uses, so the field
/// is modelled as a raw u16 rather than as an index. See
/// protocol/game/client-to-server/0x02A8.md.
/// </summary>
public sealed record RosterFollowUpRequest(
    GameMessageMetadata Metadata,
    ushort Value) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameRosterFollowUp;
}
