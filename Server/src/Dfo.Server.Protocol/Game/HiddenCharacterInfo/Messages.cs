using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.HiddenCharacterInfo;

/// <summary>
/// CMD 0x0286, sent by the roster screen with an empty body.
/// </summary>
/// <remarks>
/// Named after the legacy <c>CmdPacketType.CHARAC_VIEW_HIDDEN_CHARAC_INFO</c>. The same number is
/// <c>DAILY_CHALLENGE</c> in the NOTI enum and means something else entirely there, which is why
/// this slice is keyed on the CMD selector and not on the type alone.
/// </remarks>
public sealed record HiddenCharacterInfoRequest(
    GameMessageMetadata Metadata) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameHiddenCharacterInfo;
}

public sealed record HiddenCharacterInfoResponse : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameHiddenCharacterInfo;
}
