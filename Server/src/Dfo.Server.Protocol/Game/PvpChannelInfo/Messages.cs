using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.PvpChannelInfo;

/// <summary>CMD <c>0x00C3</c> <c>PVP_CHANNEL_INFO</c>: an empty request for whether duelling is open here.</summary>
/// <remarks>
/// The client's builders (<c>0x00B8BAC0</c>, <c>0x00FA71A7</c>, <c>0x02187698</c>) write no field.
/// <paramref name="WellFormed"/> is false for a body that is not empty; the request is still answered,
/// because the client registered a reply handler for it and waits on one.
/// </remarks>
public sealed record PvpChannelInfoRequest(GameMessageMetadata Metadata, bool WellFormed) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GamePvpChannelInfo;
}

/// <summary>CMD <c>0x00C3</c> reply.</summary>
/// <remarks>
/// Upstream <c>PvpChannelInfoHandler</c>: success is <c>01</c> then <c>i32 0, u8 0</c> (a context value and
/// a connected-server count, both empty for one server); failure is <c>00 00</c>, which A21 <c>1119FB0</c>
/// (upstream address) sends to its ordinary channel-selector fallback. <c>0x15</c> would open the
/// mercenary notice instead and is never sent.
/// </remarks>
public sealed record PvpChannelInfoResponse(bool Available) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GamePvpChannelInfo;
}
