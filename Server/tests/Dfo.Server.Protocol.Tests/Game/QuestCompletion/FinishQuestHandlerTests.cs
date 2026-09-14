using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Quests;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.QuestCompletion;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Protocol.Tests.Game.QuestCompletion;

/// <summary>
/// What answering a quest handed in does with the character's standing.
/// </summary>
/// <remarks>
/// Three frames, in the reference server's order: the acknowledgement, the experience frame
/// carrying the new total, and the acceptable-quest list without the quest just finished. What is
/// checked here is the middle one, which nothing checked before: it carries the character's
/// remaining skill points beside the total, and handing a quest in can cross a level -- so a
/// figure read off the session's entry-time projection tells a player who just levelled that their
/// new level bought them nothing.
/// </remarks>
public sealed class FinishQuestHandlerTests
{
    /// <summary>The quest handed in, and where the character stands either side of it.</summary>
    private const ushort QuestId = 101;

    private const byte StartingLevel = 1;
    private const uint StartingExperience = 900;
    private const byte FinishedLevel = 2;
    private const uint FinishedTotal = 1147;

    /// <summary>
    /// The points reported are the ones the completion left behind, not the ones the session holds.
    /// </summary>
    /// <remarks>
    /// <b>The criterion this file exists for.</b> Two things have to be true and neither is
    /// visible from the frame alone: the figure has to come from the character rather than from
    /// the session, and it has to be asked for after the completion has been written. The ledger
    /// counts how many times it was asked, so a slice that read the session asks nobody; the bus
    /// records what that count stood at when the completion ran, so a slice that asked first
    /// records one rather than none.
    /// <para>
    /// Once, and only once. Nothing else a completion does can move the ledger, so a second ask
    /// would be a second walk through the archive for an answer that cannot have changed.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ThePointsReportedAreTheOnesTheCompletionLeftBehind()
    {
        var ledger = new SkillPointLedger();
        var bus = new FixedCompletion(ledger);

        var sent = await Finish(ledger, bus);

        Assert.Equal(1, ledger.TimesAsked);
        Assert.Equal(0, bus.LedgerAsksWhenTheCompletionRan);

        var experience = sent.OfType<ExperienceNotification>().Single();
        Assert.Equal(SkillPointLedger.BeforeTheLevel, experience.SkillPoints);
        Assert.NotEqual(SkillPointLedger.StaleEntryMirror, experience.SkillPoints);
    }

    /// <summary>The three frames the reference server sends, carrying the new standing.</summary>
    /// <remarks>
    /// The order is the one confirmed against its 2026-08-27 capture. The list changes here where
    /// accepting a quest leaves it alone, and that is not a contradiction: the list is computed
    /// from cleared quests among other things, and finishing one is the only thing on this path
    /// that changes them.
    /// </remarks>
    [Fact]
    public async Task TheCompletionReportsTheStandingItLeftBehind()
    {
        var ledger = new SkillPointLedger();
        var content = new StubWorldContentEntryRuleService();

        var sent = await Finish(ledger, new FixedCompletion(ledger), content);

        Assert.Equal(
            [typeof(FinishQuestAcknowledgement), typeof(ExperienceNotification),
             typeof(AvailableQuestListMessage)],
            sent.Select(frame => frame.GetType()));

        var experience = sent.OfType<ExperienceNotification>().Single();
        Assert.Equal(FinishedLevel, experience.Level);
        Assert.Equal(FinishedTotal, experience.TotalExperience);
        Assert.Equal(FinishedLevel, content.LastSubject?.Level);
    }

    /// <summary>
    /// A completion the character has no ledger for stands, and sends no experience frame.
    /// </summary>
    /// <remarks>
    /// The frame carries the remaining points as well as the total, and inventing a figure would
    /// tell the client it had points it does not. The completion itself still stands -- the quest
    /// is handed in and the list refreshed -- because the write has already happened by the time
    /// the ledger is asked.
    /// <para>
    /// This is the other end of the same seam: a slice reading the session would find a figure
    /// there whatever the character can answer, and would send a frame that has no business
    /// existing.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ACompletionWithNoLedgerSendsNoExperienceFrame()
    {
        var ledger = new SkillPointLedger(present: false);

        var sent = await Finish(ledger, new FixedCompletion(ledger));

        Assert.Equal(
            [typeof(FinishQuestAcknowledgement), typeof(AvailableQuestListMessage)],
            sent.Select(frame => frame.GetType()));
    }

    [Fact]
    public async Task TheWireSelectionAndCompletionCountEnterTheCommand()
    {
        var ledger = new SkillPointLedger();
        var bus = new FixedCompletion(ledger);

        _ = await Finish(ledger, bus, rewardSelection: 7, completionCount: 3);

        Assert.Equal((ushort)7, bus.LastCommand?.RewardSelection);
        Assert.Equal((ushort)3, bus.LastCommand?.CompletionCount);
    }

    [Fact]
    public async Task ASettlementExceptionBecomesOnlyTheReferenceRefusal()
    {
        var sent = await Finish(new SkillPointLedger(), new RejectedCompletion());

        var acknowledgement = Assert.IsType<FinishQuestAcknowledgement>(Assert.Single(sent));
        Assert.False(acknowledgement.Finished);
        Assert.Equal((byte)QuestFinishRefusal.Unusable, acknowledgement.RefusalCode);
    }

    private static async Task<IReadOnlyList<IGameServerMessage>> Finish(
        SkillPointLedger ledger,
        ICommandBus bus,
        StubWorldContentEntryRuleService? content = null,
        ushort rewardSelection = FinishQuestRequest.NoRewardSelection,
        ushort completionCount = 1)
    {
        var session = SelectedSessionIdentity.Create(
            new AccountId(1),
            new CharacterId(60),
            new NetworkConnectionId("one"),
            connectionGeneration: 1,
            listenerPort: 10010);
        var sender = new RecordingSender();

        var handler = new FinishQuestHandler(
            new OneSession(session),
            new OneRuntimeSession(ExperienceFrameEntry.At(StartingLevel, StartingExperience)),
            new OnePlayingCharacter(ledger),
            bus,
            sender,
            new QuestOfferRefreshDomainService(
                content ?? new StubWorldContentEntryRuleService(), new RecordingQuestLogs(), Dfo.Server.Application.Tests.Support.CurrentCharacterStub.Domain()),
            new FixedClock(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000)),
            NullLogger<FinishQuestHandler>.Instance);

        await handler.HandleAsync(
            new NetworkDispatchContext(
                new NetworkConnectionContext(
                    new NetworkConnectionId("one"), NetworkEndpointKind.Game, 10010)),
            new FinishQuestRequest(
                GameMessageMetadata.Empty,
                QuestId,
                rewardSelection,
                completionCount),
            CancellationToken.None);

        return sender.Sent;
    }

    /// <summary>
    /// A bus that hands the quest in, and remembers what the ledger had been asked by then.
    /// </summary>
    /// <remarks>
    /// The second half is what makes the ordering checkable. A slice that asked the character
    /// before the completion was written would be asking about the level the character had
    /// walked in with, which is the same wrong answer the session holds and just as invisible on
    /// the frame.
    /// </remarks>
    private sealed class FixedCompletion(SkillPointLedger ledger) : ICommandBus
    {
        /// <summary>What the ledger's count stood at when the completion was written.</summary>
        public int LedgerAsksWhenTheCompletionRan { get; private set; } = -1;

        public FinishQuestCommand? LastCommand { get; private set; }

        public ValueTask<TResult> SendAsync<TResult>(
            ICommand<TResult> command, CancellationToken cancellationToken = default)
            where TResult : notnull
        {
            this.LastCommand = Assert.IsType<FinishQuestCommand>(command);
            this.LedgerAsksWhenTheCompletionRan = ledger.TimesAsked;
            var outcome = new QuestFinishOutcome(
                Finished: true,
                QuestId,
                ObjectiveKind: 2,
                Experience: FinishedTotal - StartingExperience,
                Items: [],
                NewLevel: FinishedLevel,
                NewExperience: FinishedTotal);
            return ValueTask.FromResult((TResult)(object)outcome);
        }
    }

    private sealed class RejectedCompletion : ICommandBus
    {
        public ValueTask<TResult> SendAsync<TResult>(
            ICommand<TResult> command, CancellationToken cancellationToken = default)
            where TResult : notnull =>
            throw new QuestFinishRejectedException(QuestId, "settlement failed");
    }

    /// <summary>A clock that does not move, because nothing here is about when.</summary>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
