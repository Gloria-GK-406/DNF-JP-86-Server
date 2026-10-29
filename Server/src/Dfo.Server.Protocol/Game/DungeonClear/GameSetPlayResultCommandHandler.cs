using Dfo.Server.Abstractions.DomainEvents;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Protocol.Game.CardReward;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Protocol.Game.Party;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.DungeonClear;

internal sealed partial class GameSetPlayResultCommandHandler(
    ISelectedSessionDomainService selectedSessions,
    ISelectedCharacterCurrentViewDomainService currentViews,
    ICharacterDomainService characters,
    IDungeonRunDomainService runs,
    ICardRewardDomainService cardRewards,
    ICommandWorkUnits units,
    ICharacterDungeonDifficultyDomainService difficulties, TimeProvider clock,
    Dfo.Server.Abstractions.Events.IWeeklyAttendanceDomainService attendance, IQuestProgressDomainService quests,
    IDespairTowerDomainService despair, Dfo.Server.Abstractions.Rental.IAccountLuckyStarDomainService luckyStars,
    IQuestOfferRefreshDomainService offers,
    IGameCommandOutbox outbox,
    ILogger<GameSetPlayResultCommandHandler> logger,
    IDungeonCardPresentationControl cardPresentation,
    Dfo.Server.Abstractions.DomainEvents.ICommandProjections eventNotices,
    Dfo.Server.Abstractions.Inventory.IInventorySlotProjectionDomainService inventoryProjection)
    : ICommandHandler<GameSetPlayResultCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameSetPlayResultCommand command, CancellationToken cancellationToken)
    {
        await RunAsync(execution, command.Dispatch, command.Input, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private ValueTask<DungeonSettlementRules> RulesAsync(SelectedSessionIdentity session, DungeonRunState run, CancellationToken ct) =>
        runs.SettlementRulesForAsync(session.AccountId, session.CharacterId, run, ct);

    private async ValueTask RunAsync(CommandExecutionContext execution, NetworkDispatchContext context, SetPlayResultRequest message, CancellationToken cancellationToken)
    {
        var session = selectedSessions.FindByConnection(context.Connection.ConnectionId);
        var capturedRecipient = session is null ? null : new SelectedSessionRecipient(session, context.Connection, message.Metadata);
        var result = session is null ? null
            : await currentViews.TryLoadAsync(session, cancellationToken).ConfigureAwait(false);
        if (session is null || result is null)
        {
            logger.LogWarning(
                "Set play result {ConnectionId}: no published session, ignoring",
                context.Connection.ConnectionId);
            return;
        }

        var currentRun = runs.Find(session.CharacterId);
        if (currentRun is null)
        {
            logger.LogWarning(
                "Set play result {ConnectionId} character={CharacterId}: no run in progress",
                context.Connection.ConnectionId,
                session.CharacterId.Value);
            return;
        }

        // Both story and ordinary runs settle from the run's verified clear fact. A party
        // member's personal quest progress is not a second completion gate.
        var settlement = await RulesAsync(
            session,
            currentRun,
            cancellationToken).ConfigureAwait(false);
        if (!settlement.CanComplete)
        {
            logger.LogWarning(
                "Set play result {ConnectionId} character={CharacterId}: run purpose {Purpose} "
                    + "has not met its completion rule; ignoring",
                context.Connection.ConnectionId,
                session.CharacterId.Value,
                currentRun.Layout.Purpose);
            return;
        }

        // Claimed, not merely looked up. The client says when a run is over, and since 2026-08-29
        // saying so pays experience -- so a repeated report would be a repeated payment. The same
        // guard a kill has, for the same reason, and the run is left in progress either way: what
        // ends it is the character walking out.
        if (!selectedSessions.IsInWorld(session))
            return;

        var settlementClaim = runs.TryClaimSettlement(session.CharacterId, currentRun.RunId);
        if (settlementClaim is null)
        {
            logger.LogWarning(
                "Set play result {ConnectionId} character={CharacterId}: no run in progress, or "
                    + "one already settled; ignoring",
                context.Connection.ConnectionId,
                session.CharacterId.Value);
            return;
        }
        var run = settlementClaim.Run;
        var settlementCommitted = false;
        var durableCommitted = false;
        try
        {
            // The cards are fixed here, and here is the only place they can be fixed. Above is the
            // claim: a repeated result report is turned back before it reaches this line, so one run
            // draws one pair of faces however many times its result is reported. Below is every frame
            // this settlement sends, and the reward breakdown is one of them -- so a face fixed after
            // them would be reported as the zeroes it was before it existed.
            //
            // The level handed in is the one the character cleared at, taken before the award below
            // can move it: what band a clear's cards are drawn from is a fact about the run that was
            // just fought, not about the level the last of its experience happened to buy.
            var clearProgress = await characters.Get(session.CharacterId).ProgressAsync(cancellationToken).ConfigureAwait(false);
            if (clearProgress is null)
            {
                runs.ReleaseSettlement(settlementClaim);
                return;
            }
            var settled = !run.Layout.Mode.Capabilities.DrawsClearCards
                ? new ClearRewardSettlementResult(new(new(ClearRewardCardKind.Free, [], 0),
                    new(ClearRewardCardKind.Paid, [], 0), System.Collections.Immutable.ImmutableHashSet<ClearRewardCardId>.Empty, null), null)
                : await cardRewards.SettleAsync(
                new ClearRewardSettlement(
                    session.CharacterId,
                    clearProgress.Level,
                    run.PartyMemberCount,
                    run.RunId),
                cancellationToken).ConfigureAwait(false);
            if (settled is null)
            {
                runs.ReleaseSettlement(settlementClaim);
                return;
            }
            // Every client needs the same reward row indexed by party seat. Fixing the
            // other faces is idempotent and does not grant their experience or inventory.
            List<PartyCardReward>? partyCards = null;
            if (run.Participants.Count > 1)
            {
                partyCards = [];
                foreach (var participant in run.Participants.OrderBy(value => value.Slot))
                {
                    if (runs.Find(participant.CharacterId)?.RunId != run.RunId) continue;
                    var participantProgress = await characters.Get(participant.CharacterId)
                        .ProgressAsync(cancellationToken).ConfigureAwait(false);
                    if (participantProgress is null) continue;
                    var memberCards = participant.CharacterId == session.CharacterId ? settled
                        : await cardRewards.SettleAsync(new ClearRewardSettlement(
                            participant.CharacterId, participantProgress.Level, run.PartyMemberCount,
                            run.RunId), cancellationToken).ConfigureAwait(false);
                    if (memberCards is not null)
                        partyCards.Add(new(participant.Slot, memberCards.Cards.Free.Slots,
                            memberCards.Cards.Free.Gold) { BlackDiamond = memberCards.Cards.BlackDiamond });
                }
            }
            var freeCards = settled.Cards.Free.Slots;
            var cardGold = settled.Cards.Free.Gold;

            // What the result screen is told a paid flip costs. The settlement answers the archive's
            // price for the level this clear settled at, and answers nothing for a level the archive
            // prices none at -- which is not the same as pricing it at nothing, so the two are not
            // written the same way. Neither value is invented here: both are the frame's own, named
            // and argued where the frame is built.
            var paidFlipCost =
                settled.PaidFlipCost ?? DungeonClearPayloadCodec.UnpricedPaidFlipCost;

            var elapsed = DateTimeOffset.UtcNow - run.StartedAt;
            var clearTime = (int)Math.Clamp(elapsed.TotalMilliseconds, 0, int.MaxValue);

            // Asked before anything is paid, and the same refusal a kill makes. The experience frame
            // carries the character's remaining skill points as well as the total, so without them
            // there is no frame to report the new standing on -- and paying anyway would leave the
            // character credited, the run spent, and the client still drawing the total it entered
            // with.
            //
            // Asked of the character rather than read off the session, for the reason a kill asks:
            // the session's copy is entry's projection and nothing recomputes it, so a character who
            // levelled during the run would be told the figure they walked in with.
            //
            // The settlement claim remains retryable until the award commits. Missing this mirror
            // therefore leaves the run unpaid rather than spending its one settlement attempt.
            var character = characters.Get(session.CharacterId);
            var mirror = await character.RemainingSkillPointsAsync(cancellationToken)
                .ConfigureAwait(false);
            if (mirror is null)
            {
                runs.ReleaseSettlement(settlementClaim);
                logger.LogWarning(
                    "Set play result {ConnectionId} character={CharacterId}: the character has no "
                        + "skill point ledger to report, so the clear was neither paid nor reported",
                    context.Connection.ConnectionId,
                    session.CharacterId.Value);
                await outbox.RegisterExplicitAsync(
                    context.Connection.ConnectionId,
                    new GameExplicitMessageDelivery(
                        [
                            new PlayResultNotification(
                                session.WireUserId,
                                clearTime,
                                message.ClientRankPoint,
                                settlement.QuestMaze),
                            new ClearDungeonRewardNotification(
                                0, 0, freeCards, cardGold, paidFlipCost, partyCards, BlackDiamond: settled.Cards.BlackDiamond),
                        ],
                        []),
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            var paid = await GrantAsync(execution, capturedRecipient!, run,
                new ClearCircumstances(run.Layout.BasisLevel, run.Difficulty,
                    run.Layout.ExperienceWeight, message.ClientRankPoint, run.KillSummary.Total,
                    run.PartyMemberCount), clearProgress.Level, cancellationToken).ConfigureAwait(false);

            if (run.Layout.Mode is DungeonRunMode.Despair && paid.Despair is null)
            {
                runs.ReleaseSettlement(settlementClaim);
                return;
            }
            durableCommitted = true;

            // The whole award, in its parts. Experience is a number nobody can check by looking, so
            // this line is what makes a live session usable as evidence for what a clear paid -- and
            // what the result screen's own figures have to agree with.
            logger.LogInformation(
                "Set play result {ConnectionId} character={CharacterId}: dungeon {DungeonId} cleared "
                    + "in {ClearTime}ms at rank {Rank}; clearBase={ClearBase} rankBonus={RankBonus} "
                    + "award={Award} kills={Kills} party={PartyCount} killExperience={KillExperience}",
                context.Connection.ConnectionId,
                session.CharacterId.Value,
                run.Layout.DungeonId,
                clearTime,
                message.ClientRankPoint,
                paid.ClearBase,
                paid.RankBonus,
                paid.Award, run.KillSummary.Total, run.PartyMemberCount, run.KillExperience);

            // The award is the result of this write. When none was made, the character's current
            // progress supplies the frame; the session remains only a compatibility projection.
            var award = paid.Award;
            var currentProgress = award is null
                ? await characters.Get(session.CharacterId).ProgressAsync(cancellationToken).ConfigureAwait(false)
                : new CharacterProgressionState(award.Level, award.Total);
            if (currentProgress is null)
                throw new InvalidOperationException("The current progress disappeared after its durable award.");

            if (runs.CommitSettlement(settlementClaim) is null)
            {
                throw new InvalidOperationException(
                    "The dungeon settlement claim was no longer current after its durable award.");
            }
            settlementCommitted = true;
            if (paid.AttendanceChanged)
                await eventNotices.ProjectAsync(new Dfo.Server.Abstractions.Notifications.NotificationAudience.TheseSessions([session]),
                    new Dfo.Server.Abstractions.Events.EventsChanged(),cancellationToken);
            var level = currentProgress.Level;
            var total = currentProgress.Experience;
            if (award is not null)
            {
                // A level decides which quests are offered, and a clear is as capable of crossing one
                // as a kill is -- more so, since it pays in one lump. The same refresh, for the same
                // reason: a quest whose band was just entered would otherwise stay hidden until the
                // character next entered the world.
                if (award.LeveledUp)
                {
                    // And the same second ask a kill makes: crossing a level grants more points, so
                    // the figure taken before the award is a level out of date. Only a level moves
                    // that ledger here -- a clear learns no skills and stores no bonus.
                    mirror = await character.RemainingSkillPointsAsync(cancellationToken)
                        .ConfigureAwait(false) ?? mirror;

                    // Read again now the award has committed, rather than edited into what it should be.
                    result = await currentViews.TryLoadAsync(session, cancellationToken).ConfigureAwait(false)
                        ?? throw new InvalidOperationException("Character left before the clear could be projected.");
                    result = await offers
                        .RefreshAsync(result, session.AccountId, session.CharacterId, level,
                            cancellationToken).ConfigureAwait(false);
                }

            }

            List<IGameServerMessage> frames =
            [
                new PlayResultNotification(
                    session.WireUserId,
                    clearTime,
                    message.ClientRankPoint,
                    settlement.QuestMaze),
                new ExperienceNotification(
                    level,
                    award?.NotificationTotal ?? total,
                    mirror,
                    AccountHonorLevel.Initial,
                    ExperienceBonusLines.Of(award)),
            ];

            if (paid.DifficultyUnlocked is { } unlocked)
                frames.Add(new DungeonPermissionStateMessage(
                    [new CharacterDungeonProgressState(unlocked.DungeonId, (byte)unlocked.Maximum)]));

            if (award?.LeveledUp == true)
            {
                frames.Add(new AvailableQuestListMessage(
                    level, result.World.Content.AvailableQuests));
            }

            // Render the earned reward, independent of any progression ceiling.
            frames.Add(award is null
                ? new ClearDungeonRewardNotification(0, 0, freeCards, cardGold, paidFlipCost, partyCards, BlackDiamond: settled.Cards.BlackDiamond)
                : new ClearDungeonRewardNotification(
                    award.Breakdown.Base, award.Breakdown.ClearRank, freeCards, cardGold, paidFlipCost, partyCards, run.KillExperience, award.Breakdown, settled.Cards.BlackDiamond));

            if (paid.Despair is { } tower)
                frames.AddRange(DespairTowerProjection.Rewards(tower, result.World.PreparedAt,
                    inventoryProjection ?? throw new InvalidOperationException("Inventory projection is required.")));
            await outbox.RegisterExplicitAsync(
                context.Connection.ConnectionId,
                new GameExplicitMessageDelivery(frames, []),
                cancellationToken).ConfigureAwait(false);
            if (run.Layout.Mode.Capabilities.DrawsClearCards) cardPresentation.ScheduleAfterSettlement(session, currentRun.RunId);
        }
        catch (Exception failure)
        {
            if (durableCommitted && !settlementCommitted)
                throw new Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException(
                        Dfo.Server.Abstractions.Transactions.TransactionUncertaintyReason.PostCommitPublicationFailure,
                        "Durable clear effects could not be published.", failure);
            if (!settlementCommitted && !Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException.Contains(failure))
                runs.ReleaseSettlement(settlementClaim);
            throw;
        }
    }
}
