using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.Quests;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.QuestCompletion;

/// <summary>
/// Hands the quest in and tells the client what changed because of it.
/// </summary>
/// <remarks>
/// Three frames, in the order the reference server sends them and confirmed against its 2026-08-27
/// capture: the acknowledgement, then the experience frame carrying the character's new total, then
/// the acceptable-quest list without the quest that was just finished.
/// <code>
/// S-&gt;C  CMD  0x0022  01 F8 03 04 65 00 00 00 ...   the completion
/// S-&gt;C  NOTI 0x0025  02 7B 04 00 00 ...            level 2, 1147 total
/// S-&gt;C  NOTI 0x0015  02 0A 00 65 00 ...            ten offers, 1016 gone
/// </code>
/// The acceptable list changes here where accepting a quest leaves it alone, and the difference is
/// not a contradiction: the list is computed from cleared quests among other things, and finishing
/// one is the only thing on this path that changes them.
/// </remarks>
public sealed class FinishQuestHandler(
    ISelectedSessionDirectory selectedSessions,
    ISelectedSessionRuntimeDirectory runtimeSessions,
    ICommandBus commandBus,
    IGameMessageSender sender,
    QuestOfferRefresher offers,
    TimeProvider clock,
    ILogger<FinishQuestHandler> logger) : ClientMessageHandler<FinishQuestRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        FinishQuestRequest message,
        CancellationToken cancellationToken)
    {
        var session = selectedSessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null || !runtimeSessions.TryGet(session, out var result, out _)
            || result is null)
        {
            logger.LogWarning(
                "Finish quest {ConnectionId}: quest {Quest} with no published session",
                context.Connection.ConnectionId, message.QuestId);
            await this.AnswerAsync(context, Refusal(message.QuestId), cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        // Recorded rather than acted on. The reference server picks one reward out of a list when
        // the quest offers a choice and repeats a completion when asked for several; no quest this
        // server can finish declares either, so the request's own words for them are logged where
        // the first quest that uses one will show up.
        if (message.HasRewardSelection || message.CompletionCount != 1)
        {
            logger.LogWarning(
                "Finish quest {ConnectionId}: quest {Quest} asks for selection={Selection} "
                    + "count={Count}; this server grants the whole reward once",
                context.Connection.ConnectionId,
                message.QuestId,
                message.RewardSelection,
                message.CompletionCount);
        }

        var outcome = await commandBus.SendAsync(
            new FinishQuestCommand(
                session.AccountId,
                session.CharacterId,
                message.QuestId,
                checked((uint)clock.GetUtcNow().ToUnixTimeSeconds())),
            cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Finish quest {ConnectionId} account={Account}: quest={Quest} {Verdict} "
                + "exp={Experience} items={Items} level={Level} total={Total}",
            context.Connection.ConnectionId,
            session.AccountId,
            message.QuestId,
            outcome.Finished ? "finished" : $"refused({outcome.Refusal})",
            outcome.Experience,
            outcome.Items.Count,
            outcome.NewLevel,
            outcome.NewExperience);

        await this.AnswerAsync(
            context,
            new FinishQuestAcknowledgement(
                outcome.Finished,
                outcome.QuestId,
                outcome.ObjectiveKind,
                outcome.Experience,
                outcome.Items,
                checked((uint)clock.GetUtcNow().ToUnixTimeSeconds()),
                (byte)(outcome.Refusal ?? QuestFinishRefusal.Unusable)),
            cancellationToken).ConfigureAwait(false);

        if (!outcome.Finished)
            return;

        // The session is corrected before the frames go out, and the correction is the fix for a
        // real defect rather than tidiness. Every later frame reads the character and the offer
        // list from here: a session still holding the entry-time list re-sends the finished quest
        // as offerable the next time anything refreshes it, and a session still holding the
        // entry-time experience prices the next completion at the wrong level. Both were seen on
        // 2026-08-27 -- 与赛丽亚的初次见面 was offered again after being handed in, and the
        // second completion wrote the same total as the first.
        result = result with
        {
            Character = result.Character with
            {
                Progression = new CharacterProgressionState(
                    outcome.NewLevel, outcome.NewExperience),
            },
            CharacterEntry = result.CharacterEntry with
            {
                Public = result.CharacterEntry.Public with
                {
                    Extras = result.CharacterEntry.Public.Extras with
                    {
                        CharacterExperience = outcome.NewExperience,
                    },
                },
            },
        };
        // Worked out again rather than edited. Handing a quest in can satisfy another quest's
        // prerequisite, and the quest that becomes offerable because of it has to APPEAR -- an
        // edited list can only shrink. Quest 1776 declares [pre required quest] 101, so finishing
        // 青之守护者塔娜 is what makes it offerable; with a shrinking list the player was left with
        // no next quest and no way into a dungeon until they logged out.
        result = await offers.RefreshAsync(
            result,
            session.AccountId,
            session.CharacterId,
            outcome.NewLevel,
            cancellationToken).ConfigureAwait(false);
        runtimeSessions.ReplaceExact(session, result);

        var mirror = result.CharacterEntry.Skills.ProtocolMirror;
        var frames = new List<IGameServerMessage>();
        if (mirror is not null)
        {
            frames.Add(new ExperienceNotification(
                outcome.NewLevel, outcome.NewExperience, mirror, AccountHonorLevel.Initial));
        }
        else
        {
            // The frame carries the skill-point mirror as well as the total, and inventing one
            // would tell the client it had points it does not. The completion still stands.
            logger.LogWarning(
                "Finish quest {ConnectionId}: quest {Quest} was finished but the session carries "
                    + "no skill point mirror, so the experience frame was not sent",
                context.Connection.ConnectionId, message.QuestId);
        }

        frames.Add(new AvailableQuestListMessage(
            outcome.NewLevel, result.World.Content.AvailableQuests));

        await sender.SendExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery(frames, []),
            cancellationToken).ConfigureAwait(false);
    }

    private static FinishQuestAcknowledgement Refusal(ushort questId) =>
        new(false, questId, 0, 0, [], 0, (byte)QuestFinishRefusal.Unusable);

    private ValueTask AnswerAsync(
        NetworkDispatchContext context,
        FinishQuestAcknowledgement acknowledgement,
        CancellationToken cancellationToken) =>
        sender.SendExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery([acknowledgement], []),
            cancellationToken);
}
