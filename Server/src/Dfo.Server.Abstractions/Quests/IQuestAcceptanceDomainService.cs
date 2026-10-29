using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Quests;

/// <summary>
/// One character taking on one quest: which kind of quest it is decides who may accept it, and an
/// accepted quest hands over its declared items and reports the holdings it changed.
/// </summary>
/// <remarks>Runs inside the caller's write unit.</remarks>
public interface IQuestAcceptanceDomainService
{
    ValueTask<QuestAcceptanceResult> AcceptAsync(
        AccountId accountId, CharacterId characterId, ushort questId, CancellationToken cancellationToken);
}

/// <summary>Why an acceptance was refused, in the quest domain's words.</summary>
public enum QuestAcceptanceRefusal
{
    /// <summary>No such quest, or this character may not take this kind of quest now.</summary>
    NotAcceptable,
    AlreadyCarried,
    /// <summary>Finished already and not repeatable, or an exchange already spent.</summary>
    AlreadyCleared,
    NoFreeSlot,
    ManagedByAchievement,
    UnsupportedObjective,
    /// <summary>The quest declares items and the character has no main inventory to take them.</summary>
    NoRoomForItems,
}

/// <param name="Items">The declared items that landed in the inventory; mailed overflow is not listed.</param>
public sealed record QuestAcceptanceResult(
    ushort QuestId,
    uint InitialTrigger,
    IReadOnlyList<ItemDeliveryAddition> Items,
    QuestProgressSnapshot? QuestProgress,
    QuestAcceptanceRefusal? Refusal)
{
    public bool Accepted => Refusal is null;

    public static QuestAcceptanceResult Refuse(ushort questId, QuestAcceptanceRefusal refusal) =>
        new(questId, 0, [], null, refusal);
}
