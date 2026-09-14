using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Quests;

/// <summary>
/// One character's quests, answered by reading the store every time.
/// </summary>
/// <remarks>
/// Nothing is held between calls. The alternative -- reading once and keeping the answer -- fails
/// in both directions inside a use case: a log that cached before a write would go on describing
/// the state before it, and one that cached after a rollback would describe a state that was taken
/// back. Reading through costs a statement and removes the question.
///
/// How the store is reached is <see cref="AmbientStoreAccess"/>'s to decide. A read outside a use
/// case is allowed and opens a boundary it throws away; a write outside one is refused, because a
/// change to rows that outlive the process with nothing to unwind it is exactly what the boundary
/// exists to prevent.
/// </remarks>
internal sealed class QuestLog(
    OwnedCharacterId place, IQuestDomainStore store, AmbientStoreAccess stores) : IQuestLog
{
    /// <summary>How many quests a character can carry at once.</summary>
    /// <remarks>
    /// The reference server's number. It is a property of the log rather than of any one caller,
    /// which is the whole reason this constant is here and not in a protocol handler.
    /// </remarks>
    public const int SlotCount = 30;

    public CharacterId CharacterId => place.Character;

    public ValueTask<IReadOnlyList<CarriedQuest>> CarriedAsync(CancellationToken cancellationToken) =>
        stores.ReadAsync(store, async session =>
        {
            var rows = await session.Carried.ReadAllAsync(place, cancellationToken)
                .ConfigureAwait(false);
            return (IReadOnlyList<CarriedQuest>)[.. rows
                .OrderBy(row => row.Slot)
                .Select(row => new CarriedQuest(row.Slot, row.QuestId, row.TriggerValue))];
        });

    public async ValueTask<CarriedQuest?> FindCarriedAsync(
        int questId, CancellationToken cancellationToken)
    {
        var carried = await this.CarriedAsync(cancellationToken).ConfigureAwait(false);
        return carried.FirstOrDefault(quest => quest.QuestId == questId);
    }

    public ValueTask<IReadOnlyList<ClearedQuest>> ClearedAsync(CancellationToken cancellationToken) =>
        stores.ReadAsync(store, async session =>
        {
            var rows = await session.Cleared.ReadAllAsync(place, cancellationToken)
                .ConfigureAwait(false);
            // A row whose mark is zero is a quest that was never finished. The entry projection
            // reads them the same way, and a log that answered otherwise would disagree with the
            // list the client is drawn.
            return (IReadOnlyList<ClearedQuest>)[.. rows
                .Where(row => row.Mark != 0)
                .OrderBy(row => row.QuestId)
                .Select(row => new ClearedQuest(row.QuestId, row.Mark))];
        });

    public ValueTask<IReadOnlyList<int>> TrackedAsync(CancellationToken cancellationToken) =>
        stores.ReadAsync(store, async session =>
        {
            var rows = await session.Tracked.ReadAllAsync(place, cancellationToken)
                .ConfigureAwait(false);
            return (IReadOnlyList<int>)[.. rows
                .OrderBy(row => row.SlotIndex)
                .Select(row => row.QuestId)];
        });

    public async ValueTask<QuestAcceptance> AcceptAsync(
        QuestDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var carried = await this.CarriedAsync(cancellationToken).ConfigureAwait(false);
        if (carried.Any(quest => quest.QuestId == definition.QuestId))
        {
            return QuestAcceptance.Refused(QuestRefusal.AlreadyCarried);
        }

        // Asked before the slot is chosen, because a quest that may not be taken on at all should
        // not consume the answer "no room". A repeatable quest is meant to come back -- that is
        // what its grade says -- so only the others are held against their own clear.
        if (!definition.IsRepeatable)
        {
            var cleared = await this.ClearedAsync(cancellationToken).ConfigureAwait(false);
            if (cleared.Any(quest => quest.QuestId == definition.QuestId))
                return QuestAcceptance.Refused(QuestRefusal.AlreadyCleared);
        }

        if (FirstFreeSlot(carried) is not { } slot)
        {
            return QuestAcceptance.Refused(QuestRefusal.NoFreeSlot);
        }

        var triggerValue = checked((int)definition.InitialTrigger);
        await stores.ChangeAsync(store, session =>
        {
            session.Carried.Write(
                place, new StoredActiveQuest(slot, definition.QuestId, triggerValue));
            return ValueTask.CompletedTask;
        }, cancellationToken).ConfigureAwait(false);
        return QuestAcceptance.Taken(slot, triggerValue);
    }

    /// <remarks>
    /// The slot the caller names is the slot written. Finding the row by quest number instead
    /// would be a guess whenever two slots hold the same quest -- which the table's key permits,
    /// being the slot alone -- and correcting the wrong one leaves the caller's own snapshot
    /// describing a row it never touched.
    /// </remarks>
    public async ValueTask<bool> RecordProgressAsync(
        CarriedQuest asLastSeen, int triggerValue, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asLastSeen);
        var carried = await this.CarriedAsync(cancellationToken).ConfigureAwait(false);
        if (!carried.Any(quest =>
                quest.Slot == asLastSeen.Slot && quest.QuestId == asLastSeen.QuestId))
        {
            return false;
        }

        await stores.ChangeAsync(store, session =>
        {
            session.Carried.Write(
                place,
                new StoredActiveQuest(
                    asLastSeen.Slot,
                    asLastSeen.QuestId,
                    triggerValue,
                    asLastSeen.TriggerValue));
            return ValueTask.CompletedTask;
        }, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <remarks>
    /// The order matters: whether the report is one this quest takes at all is asked before the
    /// character is looked at, so a malformed report is refused rather than answered "you are not
    /// carrying that". Then the row, then the arithmetic, and the row is written only when the
    /// value actually moved -- a report landing on the value already stored is an answer, not a
    /// write, and writing it anyway would cost a statement per repeat while the client waits.
    ///
    /// It reads the row itself rather than taking one from the caller. A report is the player
    /// doing something, not a caller correcting a snapshot: there is no earlier value being
    /// asserted, so there is nothing to hold the row against, and the value now stored is the only
    /// one the arithmetic can be applied to.
    /// </remarks>
    public async ValueTask<QuestAdvance> AdvanceAsync(
        QuestDefinition definition,
        byte selector,
        bool increment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var authority = QuestObjectives.AuthorityOver(definition, selector);
        if (authority == QuestObjectiveAuthority.None)
            return QuestAdvance.Refused;

        if (await this.FindCarriedAsync(definition.QuestId, cancellationToken).ConfigureAwait(false)
            is not { } carried)
        {
            return QuestAdvance.NotCarried;
        }

        var current = new QuestTrigger(unchecked((uint)carried.TriggerValue));
        if (authority is not QuestObjectiveAuthority.Client)
        {
            return new QuestAdvance(
                authority == QuestObjectiveAuthority.Derived
                    ? QuestAdvanceVerdict.NotEvaluable
                    : QuestAdvanceVerdict.Unchanged,
                current);
        }

        var next = current.Advance(selector, increment);
        if (next == current)
            return new QuestAdvance(QuestAdvanceVerdict.Unchanged, current);

        await this.RecordProgressAsync(carried, unchecked((int)next.PackedValue), cancellationToken)
            .ConfigureAwait(false);
        return new QuestAdvance(QuestAdvanceVerdict.Advanced, next);
    }

    /// <remarks>
    /// The two writes are one change: a quest that stopped being carried without being recorded as
    /// finished would be a quest the character did and can do again. They settle together because
    /// they are staged into the same session, and the use case settles that.
    /// </remarks>
    public async ValueTask<bool> CompleteAsync(
        int questId, byte mark, CancellationToken cancellationToken)
    {
        // A mark of zero is how every reader of this table spells "never finished", so writing one
        // would take the quest out of the carried list and never put it in the cleared one. That is
        // a quest silently lost, and it is refused here rather than discovered later as a quest the
        // player did and can do again.
        if (mark == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mark), mark, "A finished quest is never marked zero; zero means unfinished.");
        }

        if (await this.FindCarriedAsync(questId, cancellationToken).ConfigureAwait(false)
            is not { } quest)
        {
            return false;
        }

        await stores.ChangeAsync(store, session =>
        {
            session.Carried.Clear(place, quest.Slot);
            session.Cleared.Write(place, new StoredClearedQuest(questId, mark));
            return ValueTask.CompletedTask;
        }, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <remarks>
    /// Written in terms of <see cref="CompleteAsync"/> rather than beside it, so the two verbs
    /// cannot drift into writing the cleared list two different ways. What this adds is the gate.
    /// </remarks>
    public async ValueTask<QuestFinishVerdict> FinishAsync(
        int questId, CancellationToken cancellationToken)
    {
        if (await this.FindCarriedAsync(questId, cancellationToken).ConfigureAwait(false)
            is not { } carried)
        {
            return QuestFinishVerdict.NotCarried;
        }

        if (!new QuestTrigger(unchecked((uint)carried.TriggerValue)).IsComplete)
            return QuestFinishVerdict.ObjectiveUnmet;

        await this.CompleteAsync(questId, IQuestLog.ClearedMark, cancellationToken)
            .ConfigureAwait(false);
        return QuestFinishVerdict.Finished;
    }

    public async ValueTask<bool> GiveUpAsync(int questId, CancellationToken cancellationToken)
    {
        if (await this.FindCarriedAsync(questId, cancellationToken).ConfigureAwait(false)
            is not { } quest)
        {
            return false;
        }

        await stores.ChangeAsync(store, session =>
        {
            session.Carried.Clear(place, quest.Slot);
            return ValueTask.CompletedTask;
        }, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <remarks>
    /// Every slot the old selection occupied and the new one does not reach is emptied, and the
    /// old slots are read rather than counted: a selection that shrank would otherwise keep its
    /// tail, and counting would only find that tail while the slots happened to run 0, 1, 2 with
    /// no gaps. Nothing guarantees that of rows in a table, so nothing here assumes it.
    /// </remarks>
    public async ValueTask TrackAsync(
        IReadOnlyList<int> questIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(questIds);
        await stores.ChangeAsync(store, async session =>
        {
            var before = await session.Tracked.ReadAllAsync(place, cancellationToken)
                .ConfigureAwait(false);
            foreach (var stale in before.Where(row => row.SlotIndex >= questIds.Count))
            {
                session.Tracked.Clear(place, stale.SlotIndex);
            }

            for (var slot = 0; slot < questIds.Count; slot++)
            {
                session.Tracked.Write(
                    place, new StoredTrackedQuest(checked((short)slot), questIds[slot]));
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private static short? FirstFreeSlot(IReadOnlyList<CarriedQuest> carried)
    {
        var used = carried.Select(quest => quest.Slot).ToHashSet();
        for (short slot = 0; slot < SlotCount; slot++)
        {
            if (!used.Contains(slot))
            {
                return slot;
            }
        }

        return null;
    }
}

/// <summary>Hands out a log per character, holding nothing between calls.</summary>
/// <remarks>
/// There is no registry here and nothing to clean up when a character leaves, because a log owns
/// no state of its own -- it is a way of addressing rows. Two logs asked for the same character
/// are the same log in every way that matters: they read and write the same place through the same
/// store.
/// </remarks>
internal sealed class CharacterQuestLogDomainService(IQuestDomainStore store, AmbientStoreAccess stores)
    : ICharacterQuestLogDomainService
{
    public IQuestLog LogOf(AccountId accountId, CharacterId characterId) =>
        new QuestLog(new OwnedCharacterId(accountId, characterId), store, stores);
}
