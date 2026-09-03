using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Quests;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Quests;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Application.Tests.Quests;

/// <summary>
/// What a character's quest log answers, and what asking it to do something changes.
/// </summary>
/// <remarks>
/// The store underneath is the real one, over a real database, because two of the things worth
/// proving here are only true of a log that reads through: that it sees a use case's own unsettled
/// writes, and that it stops seeing them when the use case is taken back. A fake store would
/// answer both by construction and prove neither.
/// </remarks>
public sealed class QuestLogTests
{
    private static readonly AccountId Account = new(6);

    private static readonly CharacterId Character = new(60);

    [Fact]
    public async Task TwoLogsAskedForTheSameCharacterAreTheSameLog()
    {
        await using var fixture = await Fixture.CreateAsync();

        Assert.True((await fixture.Logs.LogOf(Account, Character)
            .AcceptAsync(Quest(1016), CancellationToken.None)).Accepted);
        Assert.True((await fixture.Logs.LogOf(Account, Character)
            .AcceptAsync(Quest(101), CancellationToken.None)).Accepted);

        Assert.Equal(
            [1016, 101],
            (await fixture.Logs.LogOf(Account, Character).CarriedAsync(CancellationToken.None))
                .Select(quest => quest.QuestId));
    }

    [Fact]
    public async Task WhatIsCarriedComesBackWithItsSlotAndItsProgress()
    {
        await using var fixture = await Fixture.CreateAsync();
        var log = fixture.Log();

        await log.AcceptAsync(Quest(1016, trigger: 7), CancellationToken.None);
        await log.AcceptAsync(Quest(101, trigger: 3), CancellationToken.None);

        Assert.Equal(
            [new CarriedQuest(0, 1016, 7), new CarriedQuest(1, 101, 3)],
            await log.CarriedAsync(CancellationToken.None));
    }

    [Fact]
    public async Task WhatWasFinishedComesBack()
    {
        await using var fixture = await Fixture.CreateAsync();
        var log = fixture.Log();

        await log.AcceptAsync(Quest(1016), CancellationToken.None);
        Assert.True(await log.CompleteAsync(1016, 1, CancellationToken.None));

        Assert.Equal([new ClearedQuest(1016, 1)], await log.ClearedAsync(CancellationToken.None));
    }

    [Fact]
    public async Task TheFirstQuestTakenOnGoesInTheFirstSlotAtTheProgressTheArchiveDeclares()
    {
        await using var fixture = await Fixture.CreateAsync();

        var taken = await fixture.Log().AcceptAsync(Quest(1016, trigger: 5), CancellationToken.None);

        Assert.Equal(new QuestAcceptance(true, 0, 5, null), taken);
    }

    /// <remarks>
    /// The lowest free slot, not the next one after the last: a quest given up leaves a hole, and
    /// the client draws the list by slot, so the hole is where the next quest belongs.
    /// </remarks>
    [Fact]
    public async Task AQuestTakenOnGoesInTheLowestFreeSlot()
    {
        await using var fixture = await Fixture.CreateAsync();
        var log = fixture.Log();

        await log.AcceptAsync(Quest(1016), CancellationToken.None);
        await log.AcceptAsync(Quest(101), CancellationToken.None);
        await log.GiveUpAsync(1016, CancellationToken.None);
        var taken = await log.AcceptAsync(Quest(102), CancellationToken.None);

        Assert.Equal((short)0, taken.Slot);
    }

    [Fact]
    public async Task AQuestAlreadyBeingCarriedIsRefusedAndChangesNothing()
    {
        await using var fixture = await Fixture.CreateAsync();
        var log = fixture.Log();
        await log.AcceptAsync(Quest(1016), CancellationToken.None);

        var again = await log.AcceptAsync(Quest(1016), CancellationToken.None);

        Assert.Equal(new QuestAcceptance(false, 0, 0, QuestRefusal.AlreadyCarried), again);
        Assert.Single(await log.CarriedAsync(CancellationToken.None));
    }

    /// <summary>
    /// Handed in, then offered again: the second acceptance is refused rather than paid.
    /// </summary>
    /// <remarks>
    /// The regression this exists for happened on a live client on 2026-08-27. FINISH_QUEST had
    /// just landed, the acceptance rules still had no cleared-quest check because until that
    /// moment nothing could write one, and 与赛丽亚的初次见面 was taken on again and paid its
    /// reward a second time.
    /// </remarks>
    [Fact]
    public async Task AQuestAlreadyFinishedCannotBeTakenOnAgain()
    {
        await using var fixture = await Fixture.CreateAsync();
        var log = fixture.Log();
        await log.AcceptAsync(Quest(1016, trigger: 0, objective: "meet npc"), CancellationToken.None);
        Assert.Equal(
            QuestFinishVerdict.Finished, await log.FinishAsync(1016, CancellationToken.None));

        var again = await log.AcceptAsync(Quest(1016), CancellationToken.None);

        Assert.False(again.Accepted);
        Assert.Equal(QuestRefusal.AlreadyCleared, again.Refusal);
        Assert.Empty(await log.CarriedAsync(CancellationToken.None));
    }

    /// <summary>A repeatable quest is meant to come back, and does.</summary>
    /// <remarks>
    /// The other half of the same rule, and the reason it is asked of the grade rather than of the
    /// cleared list alone: refusing every cleared quest would retire the dailies permanently after
    /// their first completion.
    /// </remarks>
    [Fact]
    public async Task AQuestThatRepeatsCanBeTakenOnAfterBeingFinished()
    {
        await using var fixture = await Fixture.CreateAsync();
        var log = fixture.Log();
        var daily = Quest(5000, trigger: 0, objective: "meet npc") with { Grade = QuestGrade.Daily };
        Assert.True(daily.IsRepeatable);
        await log.AcceptAsync(daily, CancellationToken.None);
        Assert.Equal(
            QuestFinishVerdict.Finished, await log.FinishAsync(5000, CancellationToken.None));

        var again = await log.AcceptAsync(daily, CancellationToken.None);

        Assert.True(again.Accepted);
        Assert.Equal([new CarriedQuest(0, 5000, 0)], await log.CarriedAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AQuestArrivingAtAFullLogIsRefusedForADifferentReasonAndChangesNothing()
    {
        await using var fixture = await Fixture.CreateAsync();
        var log = fixture.Log();
        for (var questId = 1; questId <= QuestLog.SlotCount; questId++)
        {
            Assert.True((await log.AcceptAsync(Quest(questId), CancellationToken.None)).Accepted);
        }

        var refused = await log.AcceptAsync(Quest(9999), CancellationToken.None);

        Assert.Equal(QuestRefusal.NoFreeSlot, refused.Refusal);
        Assert.Equal(QuestLog.SlotCount, (await log.CarriedAsync(CancellationToken.None)).Count);
    }

    [Fact]
    public async Task ProgressMovesToWhatWasRecorded()
    {
        await using var fixture = await Fixture.CreateAsync();
        var log = fixture.Log();
        await log.AcceptAsync(Quest(1016, trigger: 1), CancellationToken.None);
        await fixture.SettleAsync();
        var seen = (await log.FindCarriedAsync(1016, CancellationToken.None))!;

        Assert.True(await log.RecordProgressAsync(seen, 42, CancellationToken.None));

        Assert.Equal(42, (await log.FindCarriedAsync(1016, CancellationToken.None))!.TriggerValue);
    }

    /// <remarks>
    /// The slot the caller names is the slot written, even while another slot holds the same
    /// quest. Nothing produces that state today -- taking a quest on refuses a duplicate -- but
    /// the table is keyed by slot alone, so it is a state the store can hold, and a write that
    /// found its row by quest number would correct whichever of the two it happened to meet first.
    /// </remarks>
    [Fact]
    public async Task ProgressGoesToTheNamedSlotEvenWhenTwoSlotsHoldTheSameQuest()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedCarriedAsync((0, 1016, 1), (1, 1016, 1));
        var log = fixture.Log();

        Assert.True(await log.RecordProgressAsync(
            new CarriedQuest(1, 1016, 1), 42, CancellationToken.None));

        Assert.Equal(
            [new CarriedQuest(0, 1016, 1), new CarriedQuest(1, 1016, 42)],
            await log.CarriedAsync(CancellationToken.None));
    }

    /// <remarks>
    /// The guard is the whole reason the quest travels as it was last seen. A caller acting on a
    /// snapshot that has since moved is told so rather than overwriting a decision made about a
    /// value nobody holds any more.
    /// </remarks>
    [Fact]
    public async Task ProgressRecordedAgainstAValueTheQuestNoLongerHoldsFails()
    {
        await using var fixture = await Fixture.CreateAsync();
        var log = fixture.Log();
        await log.AcceptAsync(Quest(1016, trigger: 1), CancellationToken.None);
        await fixture.SettleAsync();

        await log.RecordProgressAsync(new CarriedQuest(0, 1016, 1234), 42, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidDataException>(async () => await fixture.SettleAsync());
    }

    [Fact]
    public async Task ProgressRecordedAgainstASlotHoldingAnotherQuestSaysSo()
    {
        await using var fixture = await Fixture.CreateAsync();
        var log = fixture.Log();
        await log.AcceptAsync(Quest(1016), CancellationToken.None);

        Assert.False(await log.RecordProgressAsync(
            new CarriedQuest(0, 101, 1), 42, CancellationToken.None));
    }

    [Fact]
    public async Task AFinishedQuestStopsBeingCarriedAndStartsBeingCleared()
    {
        await using var fixture = await Fixture.CreateAsync();
        var log = fixture.Log();
        await log.AcceptAsync(Quest(1016), CancellationToken.None);

        Assert.True(await log.CompleteAsync(1016, 2, CancellationToken.None));

        Assert.Empty(await log.CarriedAsync(CancellationToken.None));
        Assert.Equal([new ClearedQuest(1016, 2)], await log.ClearedAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AQuestGivenUpIsInNeitherList()
    {
        await using var fixture = await Fixture.CreateAsync();
        var log = fixture.Log();
        await log.AcceptAsync(Quest(1016), CancellationToken.None);

        Assert.True(await log.GiveUpAsync(1016, CancellationToken.None));

        Assert.Empty(await log.CarriedAsync(CancellationToken.None));
        Assert.Empty(await log.ClearedAsync(CancellationToken.None));
    }

    [Fact]
    public async Task FinishingOrGivingUpAQuestNobodyIsCarryingSaysSo()
    {
        await using var fixture = await Fixture.CreateAsync();
        var log = fixture.Log();

        Assert.False(await log.CompleteAsync(1016, 1, CancellationToken.None));
        Assert.False(await log.GiveUpAsync(1016, CancellationToken.None));
        Assert.False(await log.RecordProgressAsync(
            new CarriedQuest(0, 1016, 1), 3, CancellationToken.None));
    }

    /// <remarks>
    /// Zero is how this table spells "never finished", and every reader of it agrees. Accepting a
    /// zero here would take the quest out of the carried list and put it in neither -- the one
    /// outcome finishing a quest must never have.
    /// </remarks>
    [Fact]
    public async Task AQuestCannotBeFinishedWithNoMarkAtAll()
    {
        await using var fixture = await Fixture.CreateAsync();
        var log = fixture.Log();
        await log.AcceptAsync(Quest(1016), CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await log.CompleteAsync(1016, 0, CancellationToken.None));

        Assert.Single(await log.CarriedAsync(CancellationToken.None));
    }

    /// <remarks>
    /// The entry projection reads these rows into a flag array of thirty thousand and refuses a
    /// row outside it. Such a row would not be a wrong answer, it would be a character nobody can
    /// enter with, so the store refuses it where it is asked rather than while flushing.
    /// </remarks>
    [Fact]
    public async Task AQuestTooHighToBeDrawnCannotBeRecordedAsFinished()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedCarriedAsync((0, 40_000, 1));
        var log = fixture.Log();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await log.CompleteAsync(40_000, 1, CancellationToken.None));
    }

    [Fact]
    public async Task WhatTheCharacterAskedToBeToldAboutComesBackInOrder()
    {
        await using var fixture = await Fixture.CreateAsync();
        var log = fixture.Log();

        await log.TrackAsync([1016, 101, 102], CancellationToken.None);

        Assert.Equal([1016, 101, 102], await log.TrackedAsync(CancellationToken.None));
    }

    /// <remarks>
    /// A shorter selection has to lose its tail. Writing only the new entries would leave the old
    /// ones past the end in place, and the client would go on being told about a quest the player
    /// stopped asking about.
    /// </remarks>
    [Fact]
    public async Task AShorterSelectionLosesWhatItNoLongerReaches()
    {
        await using var fixture = await Fixture.CreateAsync();
        var log = fixture.Log();
        await log.TrackAsync([1016, 101, 102], CancellationToken.None);
        // Settled first, so the shorter selection meets rows in the database instead of rows still
        // staged beside it. Only that way does anything run the statement that removes them.
        await fixture.SettleAsync();

        await log.TrackAsync([102], CancellationToken.None);
        await fixture.SettleAsync();

        Assert.Equal([102], await log.TrackedAsync(CancellationToken.None));
        Assert.Equal(1, await fixture.CountRowsAsync("character_quest_notify_selections"));
    }

    [Fact]
    public async Task TheLogSeesWhatTheUseCaseHasDecidedBeforeAnythingIsSettled()
    {
        await using var fixture = await Fixture.CreateAsync();
        var log = fixture.Log();

        await log.AcceptAsync(Quest(1016), CancellationToken.None);

        Assert.Equal(0, await fixture.CountRowsAsync());
        Assert.Single(await log.CarriedAsync(CancellationToken.None));
    }

    /// <remarks>
    /// One settled quest and one that is not, asked of the same log on both sides of the rollback.
    /// Both halves are load-bearing: without the settled quest the count would already be zero
    /// before anything was taken back, and with a freshly built log the answer would be right
    /// however much the old one had cached.
    /// </remarks>
    [Fact]
    public async Task AUseCaseTakenBackTakesItsQuestWithItAndLeavesTheRest()
    {
        await using var fixture = await Fixture.CreateAsync();
        var log = fixture.Log();
        await log.AcceptAsync(Quest(1016), CancellationToken.None);
        await fixture.SettleAsync();

        await log.AcceptAsync(Quest(101), CancellationToken.None);
        Assert.Equal(
            [1016, 101],
            (await log.CarriedAsync(CancellationToken.None)).Select(quest => quest.QuestId));

        await fixture.RollBackAsync();

        Assert.Equal(
            [1016],
            (await log.CarriedAsync(CancellationToken.None)).Select(quest => quest.QuestId));
        Assert.Equal(1, await fixture.CountRowsAsync());
    }

    /// <remarks>
    /// The archive declares what a quest starts at; every other field is what the parser found in
    /// the file and none of it is a question the log asks.
    /// </remarks>
    /// <summary>The whole tutorial exchange: report the visit, then hand the quest in.</summary>
    [Fact]
    public async Task AQuestWhoseObjectiveIsMetIsHandedInAndBecomesCleared()
    {
        await using var fixture = await Fixture.CreateAsync();
        var log = fixture.Log();
        var quest = Quest(1016, trigger: 1, objective: "meet npc");
        await log.AcceptAsync(quest, CancellationToken.None);
        await log.AdvanceAsync(quest, 0, false, CancellationToken.None);

        Assert.Equal(
            QuestFinishVerdict.Finished,
            await log.FinishAsync(1016, CancellationToken.None));

        Assert.Empty(await log.CarriedAsync(CancellationToken.None));
        Assert.Equal(
            [new ClearedQuest(1016, IQuestLog.ClearedMark)],
            await log.ClearedAsync(CancellationToken.None));
    }

    /// <remarks>
    /// The gate that stops a crafted frame finishing any carried quest by asking. The client will
    /// not send this before the objective is met, so it is refused where a real client never goes.
    /// </remarks>
    [Fact]
    public async Task AQuestWhoseObjectiveIsNotMetIsRefusedAndStaysCarried()
    {
        await using var fixture = await Fixture.CreateAsync();
        var log = fixture.Log();
        await log.AcceptAsync(Quest(1016, trigger: 1, objective: "meet npc"), CancellationToken.None);

        Assert.Equal(
            QuestFinishVerdict.ObjectiveUnmet,
            await log.FinishAsync(1016, CancellationToken.None));

        Assert.Equal(
            [new CarriedQuest(0, 1016, 1)], await log.CarriedAsync(CancellationToken.None));
        Assert.Empty(await log.ClearedAsync(CancellationToken.None));
    }

    [Fact]
    public async Task HandingInAQuestNobodyIsCarryingSaysSo()
    {
        await using var fixture = await Fixture.CreateAsync();

        Assert.Equal(
            QuestFinishVerdict.NotCarried,
            await fixture.Log().FinishAsync(1016, CancellationToken.None));
    }

    /// <summary>Handed in once. The second attempt finds nothing to hand in.</summary>
    /// <remarks>
    /// Worth its own test because the client repeats an unanswered request, and because the two
    /// writes this verb makes are what would otherwise let a quest be paid for twice.
    /// </remarks>
    [Fact]
    public async Task AQuestAlreadyHandedInCannotBeHandedInAgain()
    {
        await using var fixture = await Fixture.CreateAsync();
        var log = fixture.Log();
        await log.AcceptAsync(Quest(1016, trigger: 0, objective: "meet npc"), CancellationToken.None);
        Assert.Equal(
            QuestFinishVerdict.Finished, await log.FinishAsync(1016, CancellationToken.None));

        Assert.Equal(
            QuestFinishVerdict.NotCarried, await log.FinishAsync(1016, CancellationToken.None));

        Assert.Equal(
            [new ClearedQuest(1016, IQuestLog.ClearedMark)],
            await log.ClearedAsync(CancellationToken.None));
    }

    /// <summary>Clicking 赛丽亚, end to end: the quest goes to zero and the row holds it.</summary>
    [Fact]
    public async Task AReportOnAClientCountedQuestMovesItAndIsStored()
    {
        await using var fixture = await Fixture.CreateAsync();
        var log = fixture.Log();
        var quest = Quest(1016, trigger: 1, objective: "meet npc");
        await log.AcceptAsync(quest, CancellationToken.None);

        var advance = await log.AdvanceAsync(quest, 0, false, CancellationToken.None);

        Assert.Equal(QuestAdvanceVerdict.Advanced, advance.Verdict);
        Assert.Equal(new QuestTrigger(0), advance.Trigger);
        Assert.Equal(
            [new CarriedQuest(0, 1016, 0)], await log.CarriedAsync(CancellationToken.None));
    }

    /// <remarks>
    /// The client repeats a report it was not answered for. Once the quest is at zero the repeats
    /// have to be absorbed: the answer stays zero, and the row is left alone rather than written
    /// with the value it already holds.
    /// </remarks>
    [Fact]
    public async Task AReportThatLandsOnTheValueAlreadyHeldChangesNothing()
    {
        await using var fixture = await Fixture.CreateAsync();
        var log = fixture.Log();
        var quest = Quest(1016, trigger: 1, objective: "meet npc");
        await log.AcceptAsync(quest, CancellationToken.None);
        await log.AdvanceAsync(quest, 0, false, CancellationToken.None);

        var again = await log.AdvanceAsync(quest, 0, false, CancellationToken.None);

        Assert.Equal(QuestAdvanceVerdict.Unchanged, again.Verdict);
        Assert.Equal(new QuestTrigger(0), again.Trigger);
        Assert.Equal(
            [new CarriedQuest(0, 1016, 0)], await log.CarriedAsync(CancellationToken.None));
    }

    /// <summary>
    /// An objective this server counts is answered with the stored value and not moved by what
    /// the client says about it.
    /// </summary>
    [Fact]
    public async Task AReportOnAServerCountedQuestIsAnsweredWithoutMovingIt()
    {
        await using var fixture = await Fixture.CreateAsync();
        var log = fixture.Log();
        var quest = Quest(2000, trigger: 5, objective: "hunt monster");
        await log.AcceptAsync(quest, CancellationToken.None);

        var advance = await log.AdvanceAsync(quest, 0, false, CancellationToken.None);

        Assert.Equal(QuestAdvanceVerdict.Unchanged, advance.Verdict);
        Assert.Equal(new QuestTrigger(5), advance.Trigger);
        Assert.Equal(
            [new CarriedQuest(0, 2000, 5)], await log.CarriedAsync(CancellationToken.None));
    }

    /// <remarks>
    /// An item-seeking quest's progress is a count of what is in the bag, which this log does not
    /// read. It says so with its own verdict rather than passing for an ordinary no-change: the
    /// caller logs that distinction, and losing it would turn a known gap into a silent one.
    /// </remarks>
    [Fact]
    public async Task AReportOnABagCountedQuestSaysItCannotBeEvaluatedHere()
    {
        await using var fixture = await Fixture.CreateAsync();
        var log = fixture.Log();
        var quest = Quest(2001, trigger: 3, objective: "seeking");
        await log.AcceptAsync(quest, CancellationToken.None);

        var advance = await log.AdvanceAsync(quest, 0, false, CancellationToken.None);

        Assert.Equal(QuestAdvanceVerdict.NotEvaluable, advance.Verdict);
        Assert.Equal(new QuestTrigger(3), advance.Trigger);
    }

    [Fact]
    public async Task AReportAboutAQuestNobodyIsCarryingSaysSo()
    {
        await using var fixture = await Fixture.CreateAsync();

        var advance = await fixture.Log().AdvanceAsync(
            Quest(1016, objective: "meet npc"), 0, false, CancellationToken.None);

        Assert.Equal(QuestAdvanceVerdict.NotCarried, advance.Verdict);
    }

    /// <remarks>
    /// Asked before the character is looked at, so a report this server cannot read is refused
    /// as such rather than answered "you are not carrying that" -- which would be a different
    /// byte on the wire and a wrong account of what happened.
    /// </remarks>
    [Fact]
    public async Task AReportWithASelectorThisServerCannotReadIsRefusedBeforeTheLogIsRead()
    {
        await using var fixture = await Fixture.CreateAsync();

        var advance = await fixture.Log().AdvanceAsync(
            Quest(1016, objective: "meet npc"), 0x11, false, CancellationToken.None);

        Assert.Equal(QuestAdvanceVerdict.Refused, advance.Verdict);
    }

    /// <summary>A quest at zero told one more goes back to one, and the row follows.</summary>
    [Fact]
    public async Task AReportOfOneMoreMovesTheQuestBackUp()
    {
        await using var fixture = await Fixture.CreateAsync();
        var log = fixture.Log();
        var quest = Quest(1016, trigger: 0, objective: "meet npc");
        await log.AcceptAsync(quest, CancellationToken.None);

        var advance = await log.AdvanceAsync(quest, 0, true, CancellationToken.None);

        Assert.Equal(QuestAdvanceVerdict.Advanced, advance.Verdict);
        Assert.Equal(
            [new CarriedQuest(0, 1016, 1)], await log.CarriedAsync(CancellationToken.None));
    }

    private static QuestDefinition Quest(
        int questId, uint trigger = 1, string objective = "") =>
        new(questId, "", null, null, null, null, null, null, [], objective, "",
            null, null, null, null, null, null, null, [], [], [], [], InitialTrigger: trigger);

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(string path)
        {
            this.Path = path;
            this.Store = new SqliteQuestDomainStore(path);
            this.Transactions = new SwappableTransactionAccessor();
            this.Logs = new CharacterQuestLogDomainService(
                this.Store, new AmbientStoreAccess(this.Transactions));
        }

        public string Path { get; }

        public SqliteQuestDomainStore Store { get; }

        public SwappableTransactionAccessor Transactions { get; }

        public ICharacterQuestLogDomainService Logs { get; }

        public IQuestLog Log() => this.Logs.LogOf(Account, Character);

        public static async Task<Fixture> CreateAsync()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"dfo-questlog-{Guid.NewGuid():N}.db");
            // The database owns the schema; the store only writes into it.
            _ = new SqliteDatabase(path, new DomainTransactionScope());
            var fixture = new Fixture(path);
            await using var connection = Open(path);
            await using var character = connection.CreateCommand();
            character.CommandText =
                "INSERT INTO characters(character_id,account_id,name) VALUES(60,6,'quests')";
            await character.ExecuteNonQueryAsync();
            return fixture;
        }

        /// <summary>Settles the open use case and opens the next one.</summary>
        public async Task SettleAsync()
        {
            await this.Transactions.Open.CommitAsync(CancellationToken.None);
            this.Transactions.Reopen();
        }

        /// <summary>Takes the open use case back and opens the next one.</summary>
        public async Task RollBackAsync()
        {
            await this.Transactions.Open.RollbackAsync();
            this.Transactions.Reopen();
        }

        public async Task<int> CountRowsAsync(string table = "character_active_quests")
        {
            await using var connection = Open(this.Path);
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE character_id=60";
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        /// <summary>Puts rows in place directly, for states no verb of the log will produce.</summary>
        public async Task SeedCarriedAsync(params (int Slot, int QuestId, int TriggerValue)[] rows)
        {
            await using var connection = Open(this.Path);
            foreach (var (slot, questId, triggerValue) in rows)
            {
                await using var command = connection.CreateCommand();
                command.CommandText =
                    "INSERT INTO character_active_quests(character_id,slot,quest_id,trigger_value) "
                    + "VALUES(60,@slot,@quest,@trigger)";
                command.Parameters.AddWithValue("@slot", slot);
                command.Parameters.AddWithValue("@quest", questId);
                command.Parameters.AddWithValue("@trigger", triggerValue);
                await command.ExecuteNonQueryAsync();
            }
        }

        private static SqliteConnection Open(string path)
        {
            var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            connection.Open();
            return connection;
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            try
            {
                File.Delete(this.Path);
            }
            catch (IOException)
            {
                // A file the runtime still holds is left for the temp directory to reap.
            }

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Stands in for the pipeline's ambient transaction, one use case at a time.</summary>
    internal sealed class SwappableTransactionAccessor : IDomainTransactionAccessor
    {
        public DomainTransaction Open { get; private set; } = new();

        public IDomainTransaction? Current => this.Open;

        public void Reopen() => this.Open = new DomainTransaction();
    }
}
