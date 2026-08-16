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

/// <summary>The two zero bytes the reference server answers 0x02A8 with.</summary>
/// <remarks>
/// This protocol was recorded as expecting no response, and that was wrong in an instructive way:
/// the evidence was that no inbound frame followed it in five captured sessions -- sessions served
/// by this server, which never replied. Observing our own silence and concluding the client wanted
/// silence is circular. The reference server replies, and the difference showed up only when both
/// servers were driven through the same sequence and diffed.
/// </remarks>
public sealed record RosterFollowUpResponse : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameRosterFollowUp;
}
