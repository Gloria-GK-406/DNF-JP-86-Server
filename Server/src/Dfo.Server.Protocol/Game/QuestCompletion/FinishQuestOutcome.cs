using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Game;

namespace Dfo.Server.Protocol.Game.QuestCompletion;

/// <summary>Why a completion was refused, in the code the client's answer carries.</summary>
/// <remarks>
/// One number, which is all the reference server answers with here: every refusal path in its
/// completion service ends at <c>QuestFinishResult.Fail(22)</c>. An unknown quest, an unmet
/// objective, a full bag and a quest nobody is carrying are indistinguishable on the wire.
/// </remarks>
public enum QuestFinishRefusal : byte
{
    /// <summary>The completion cannot be granted. The reference server's only code for this frame.</summary>
    Unusable = 22,
}

/// <summary>What became of one attempt to hand a quest in.</summary>
/// <param name="Experience">What the quest paid, which is not the character's new total.</param>
/// <param name="NewExperience">The total afterwards, which is what the experience frame carries.</param>
public sealed record QuestFinishOutcome(
    bool Finished,
    ushort QuestId,
    byte ObjectiveKind,
    uint Experience,
    IReadOnlyList<FinishedQuestItem> Items,
    byte NewLevel,
    uint NewExperience,
    QuestFinishRefusal? Refusal = null,
    QuestProgressSnapshot? QuestProgress = null,
    IReadOnlyList<InventoryRuntimeValueState>? AccountMaterials = null,
    byte? GrowType = null)
{
    public bool MirrorCompletion { get; init; }
    public byte? Awakening { get; init; }
    public uint NotificationExperience { get; init; } = NewExperience;
    public IReadOnlyList<CommittedInventoryUpdateState> ConsumedItemUpdates { get; init; } = [];

    public static QuestFinishOutcome Refuse(
        ushort questId, QuestProgressSnapshot? questProgress = null) =>
        new(false, questId, 0, 0, [], 0, 0, QuestFinishRefusal.Unusable, questProgress);
}

/// <summary>One character handing one quest in.</summary>
/// <param name="AcquiredAtUnixTime">Stamped into every item the completion hands over.</param>
/// <remarks>
/// Where the character stands is deliberately NOT carried here. It is read from the store inside
/// the transaction, because the caller's copy of it is the entry-time snapshot and that snapshot
/// never moves: pricing a reward from it means every completion in a session is priced at the
/// level the session opened at, and adding to it means every completion writes the same total.
/// Both happened -- on 2026-08-27 a quest finished twice paid <c>total=1101</c> both times.
/// </remarks>
internal sealed record QuestCompletionInput(
    AccountId AccountId,
    CharacterId CharacterId,
    ushort QuestId,
    ushort RewardSelection,
    ushort CompletionCount,
    uint AcquiredAtUnixTime, SelectedSessionIdentity? Session = null) ;

public sealed class QuestFinishRejectedException : Exception
{
    public QuestFinishRejectedException(ushort questId, string message)
        : base(message) => this.QuestId = questId;

    internal QuestFinishRejectedException(ushort questId, string message, Exception innerException)
        : base(message, innerException) => this.QuestId = questId;

    public ushort QuestId { get; }
}
