using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.QuestProgress;

/// <summary>
/// A report that a quest moved (cmd 0x01/type 0x0021): which quest, which counters, which way.
/// </summary>
/// <remarks>
/// The client sends this when the player does the thing a quest asked for -- clicking the NPC a
/// <c>meet npc</c> quest names, using the item a <c>use item</c> quest names. It is a report, not
/// a request to complete: what it moves and whether it is allowed to move anything is the quest
/// log's to decide.
///
/// The body is six bytes and only the last four are fields. The first two repeat the packet's own
/// type, the way ACCEPT_QUEST's do; the body seen live is <c>21 00 F8 03 00 00</c>, which is quest
/// 1016 -- 与赛丽亚的初次见面 -- reporting one fewer on the whole word.
///
/// <b>The same number is a notification in the other direction</b>, where it announces a failed
/// dungeon clear, so the two must not share a constant. See
/// protocol/game/client-to-server/0x0021.md.
/// </remarks>
public sealed record SetQuestTriggerRequest(
    GameMessageMetadata Metadata,
    ushort QuestId,
    byte Selector,
    bool Increment) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameSetQuestTrigger;
}

/// <summary>The answer to 0x0021: the quest's progress after the report.</summary>
/// <remarks>
/// A refusal is two bytes -- a zero and a reason -- and an answer carries the quest back with its
/// progress as a whole word. The client draws that word, so a report that changes nothing is still
/// answered with the unchanged value rather than with silence: silence is what makes the client
/// send the report again, and again.
/// </remarks>
public sealed record SetQuestTriggerAcknowledgement(
    bool Answered,
    ushort QuestId,
    uint TriggerValue,
    byte RefusalCode) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameSetQuestTrigger;
}
