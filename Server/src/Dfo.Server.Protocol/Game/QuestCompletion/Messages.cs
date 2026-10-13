using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Protocol.Game.QuestCompletion;

/// <summary>
/// A request to hand a quest in (cmd 0x01/type 0x0022): which quest, and which of its rewards.
/// </summary>
/// <remarks>
/// Sent when the player accepts the completion dialogue at the quest's NPC. It arrives after the
/// quest's progress has already reached zero through SET_QUEST_TRIGGER, and its own job is to turn
/// that finished objective into rewards.
///
/// The body is ten bytes, of which the first two repeat the packet's own type. The reference
/// server rejects a body of any other length outright, which is why this codec matches on the
/// exact length rather than a minimum: the trailing sentinel is what tells a well-formed request
/// from a truncated one, and a shorter body would put the sentinel's check on the wrong bytes.
/// </remarks>
public sealed record FinishQuestRequest(
    GameMessageMetadata Metadata,
    ushort QuestId,
    ushort RewardSelection,
    ushort CompletionCount) : IEffectSettlementRequest
{
    public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.Inventory | ReplayProjectionNeeds.Quests;
    /// <summary>The value the selection carries when the quest offered no choice.</summary>
    public const ushort NoRewardSelection = 0xFFFF;

    /// <summary>Whether the player picked one reward from a list.</summary>
    public bool HasRewardSelection => this.RewardSelection != NoRewardSelection;

    public ClientMessageType MessageType => ClientMessageType.GameFinishQuest;
}

/// <summary>One reward the completion handed over, and the slot it landed in.</summary>
public sealed record FinishedQuestItem(ushort SlotIndex, uint ItemId, uint Count);

/// <summary>The answer to 0x0022: what the quest paid.</summary>
/// <remarks>
/// A refusal is two bytes -- a zero and a reason -- and a completion carries the quest, what kind
/// of objective it was, the experience it paid, and every item it handed over with the slot each
/// one landed in. The client draws all of it: the experience popup, the item bubbles, and the
/// quest leaving the log.
/// </remarks>
public sealed record FinishQuestAcknowledgement(
    bool Finished,
    ushort QuestId,
    byte ObjectiveKind,
    uint Experience,
    IReadOnlyList<FinishedQuestItem> Items,
    uint AcquiredAtUnixTime,
    byte RefusalCode,
    byte? GrowType = null) : IGameCmdMessage
{
    public byte? Awakening { get; init; }
    public IReadOnlyList<CharacterSkillPageState> SkillPages { get; init; } = [];
    public ServerMessageType MessageType => ServerMessageType.GameFinishQuest;
}
