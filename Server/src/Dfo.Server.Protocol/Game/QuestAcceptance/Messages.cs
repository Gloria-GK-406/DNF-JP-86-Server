using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.QuestAcceptance;

/// <summary>
/// A request to take on a quest (cmd 0x01/type 0x001F): the quest's identifier, and nothing else.
/// </summary>
/// <remarks>
/// The body is four bytes and only the last two are a field. The first two repeat the packet's own
/// type -- the reference server drops them before parsing, and the two bodies observed live are
/// <c>1F 00 F8 03</c> and <c>1F 00 65 00</c>, whose leading pair is 0x001F both times.
///
/// <b>The same number is a notification in the other direction</b>, where it enables the
/// dungeon-clear announcement, so the two must not share a constant. See
/// protocol/game/client-to-server/0x001F.md.
/// </remarks>
public sealed record AcceptQuestRequest(
    GameMessageMetadata Metadata,
    ushort QuestId) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameAcceptQuest;
}

/// <summary>The answer to 0x001F, which is where the quest's opening state travels.</summary>
/// <remarks>
/// A refusal is two bytes -- a zero and a reason -- and an acceptance carries the quest back, the
/// progress to start it at, and every item the acceptance handed over with the slot it landed in.
/// The client draws all three, so an acceptance that answered only "yes" would leave the quest
/// with no progress bar and its items undrawn until something else refreshed them.
/// </remarks>
public sealed record AcceptQuestAcknowledgement(
    bool Accepted,
    ushort QuestId,
    uint InitialTrigger,
    IReadOnlyList<AcceptedQuestItem> Items,
    byte RefusalCode) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameAcceptQuest;
}
