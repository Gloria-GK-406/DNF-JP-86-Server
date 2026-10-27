using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Protocol.Game.QuestAcceptance;

/// <summary>Why an acceptance was refused, in the codes the client's answer carries.</summary>
/// <remarks>
/// The numbers are the reference server's own. It answers a refusal with two bytes -- a zero and
/// one of these -- and says nothing else about it, so the code is the whole of what the player is
/// told. Two of them share a value there as well: an unusable request and a full list both answer
/// 0x17, and nothing distinguishes them on the wire.
///
/// These are the wire's words for what the quest log answered in its own. The mapping is here
/// because a code the client draws is the protocol's business; which refusals exist at all is the
/// log's.
/// </remarks>
public enum QuestAcceptRefusal : byte
{
    /// <summary>The archive declares no such quest.</summary>
    Unknown = 0x17,

    /// <summary>The character is already carrying it, or has already finished it.</summary>
    /// <remarks>
    /// One code for both, which is the reference server's own doing: its acceptance service
    /// answers 18 for a quest in the active list and 18 again for one already cleared and not
    /// repeatable. Nothing on the wire tells the two apart.
    /// </remarks>
    AlreadyActive = 18,

    /// <summary>Its items would not fit.</summary>
    NoRoomForItems = 0x11,

    /// <summary>Every active slot is taken.</summary>
    ActiveListFull = 0x17,
}

/// <summary>One item the acceptance handed over, and the slot it landed in.</summary>
public sealed record AcceptedQuestItem(ushort SlotIndex, uint ItemId, uint Count);

/// <summary>What became of one attempt to accept a quest.</summary>
/// <param name="InitialTrigger">
/// Acceptance uses the archive's initial value, followed by a progress notification when needed.
/// </param>
public sealed record QuestAcceptOutcome(
    bool Accepted,
    ushort QuestId,
    uint InitialTrigger,
    IReadOnlyList<AcceptedQuestItem> Items,
    QuestAcceptRefusal? Refusal = null,
    QuestProgressSnapshot? QuestProgress = null,
    IReadOnlyList<InventoryRuntimeValueState>? AccountMaterials = null)
{
    public static QuestAcceptOutcome Refuse(ushort questId, QuestAcceptRefusal refusal) =>
        new(false, questId, 0, [], refusal);
}

/// <summary>One character taking on one quest.</summary>
internal sealed record QuestAcceptanceInput(
    AccountId AccountId,
    CharacterId CharacterId,
    ushort QuestId) ;
