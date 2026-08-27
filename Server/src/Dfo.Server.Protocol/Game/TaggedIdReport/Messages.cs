using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.TaggedIdReport;

/// <summary>
/// A tutorial flag change (cmd 0x01/type 0x008F): a mode byte, the flag's index, and whether the
/// step's reward is claimed with it.
/// </summary>
/// <remarks>
/// The fields were <c>Id</c> and <c>Flag</c> when this was read as the older client's five-byte
/// <c>{u32 id, u8 flag}</c>. A21 prepends a mode byte, and with the real layout the names are no
/// longer guesses: the index selects the tutorial step, and the reward flag decides whether that
/// step's items are granted. Mode's meaning is not established -- every observed body has it zero.
///
/// The client also sends this while handling the inbound EXIT response, so it can arrive after the
/// server has answered EXIT, possibly while the connection is closing. See
/// protocol/game/client-to-server/0x008F.md.
/// </remarks>
public sealed record TaggedIdReportRequest(
    GameMessageMetadata Metadata,
    byte Mode,
    uint FlagIndex,
    byte RewardFlag) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameTaggedIdReport;
}

/// <summary>
/// The acknowledgement for 0x008F, carrying whatever the tutorial step handed over.
/// </summary>
/// <remarks>
/// The reference server answers every flag change with this, and the count is zero for the flags
/// that give nothing -- which, in the archive shipped here, is every flag but one.
/// </remarks>
public sealed record TutorialFlagAcknowledgement(
    IReadOnlyList<GrantedTutorialItem> Granted) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameTaggedIdReport;
}

/// <summary>One item actually placed in the character's inventory, and where it went.</summary>
public sealed record GrantedTutorialItem(ushort Slot, uint ItemId, uint Count);
