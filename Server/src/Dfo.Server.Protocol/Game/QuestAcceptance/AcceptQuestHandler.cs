using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.QuestAcceptance;

/// <summary>
/// Takes the quest on, answers with what it opened at, and refreshes the offer list.
/// </summary>
/// <remarks>
/// Three frames leave here in the reference server's order: the acknowledgement, then -- only when
/// the quest was taken -- the acceptable-quest list again. It re-sends that list whether or not it
/// changed, and on this server it never does: the list is computed from level, job, grow type and
/// cleared quests, and accepting changes none of them. It is sent anyway because matching the
/// reference is the rule, and because a client that redraws on receipt would otherwise be left
/// showing the offer it just took.
///
/// The list is the one entry computed, read back from the session rather than recomputed. That is
/// not a shortcut around a stale answer -- nothing it depends on has moved -- and recomputing it
/// would mean rebuilding the whole world-content subject to arrive at the same bytes.
///
/// <b>Answering at all is what matters most here.</b> Before this existed the client sent the
/// request, waited, and sent it again: the two acceptances observed live arrived twice each.
/// </remarks>
public sealed class AcceptQuestHandler(
    ISelectedSessionDomainService selectedSessions,
    ISelectedSessionRuntimeDirectory runtimeSessions,
    ICommandBus commandBus,
    IGameMessageSender sender,
    IQuestOfferRefreshDomainService offers,
    ILogger<AcceptQuestHandler> logger) : ClientMessageHandler<AcceptQuestRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        AcceptQuestRequest message,
        CancellationToken cancellationToken)
    {
        var session = selectedSessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null)
        {
            // No character is selected, so there is nothing to carry the quest. Refused rather
            // than ignored: silence is what made the client repeat itself.
            logger.LogWarning(
                "Accept quest {ConnectionId}: quest {Quest} with no character selected",
                context.Connection.ConnectionId, message.QuestId);
            await AnswerAsync(
                context,
                new AcceptQuestAcknowledgement(
                    false, message.QuestId, 0, [], (byte)QuestAcceptRefusal.Unknown),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var outcome = await commandBus.SendAsync(
            new AcceptQuestCommand(session.AccountId, session.CharacterId, message.QuestId),
            cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Accept quest {ConnectionId} account={Account}: quest={Quest} {Verdict} trigger={Trigger} items={Items}",
            context.Connection.ConnectionId,
            session.AccountId,
            message.QuestId,
            outcome.Accepted ? "accepted" : $"refused({outcome.Refusal})",
            outcome.InitialTrigger,
            outcome.Items.Count);

        await AnswerAsync(
            context,
            new AcceptQuestAcknowledgement(
                outcome.Accepted,
                outcome.QuestId,
                outcome.InitialTrigger,
                outcome.Items,
                (byte)(outcome.Refusal ?? QuestAcceptRefusal.Unknown)),
            cancellationToken).ConfigureAwait(false);

        if (!outcome.Accepted)
            return;

        if (!runtimeSessions.TryGet(session, out var result, out _) || result is null)
        {
            logger.LogWarning(
                "Accept quest {ConnectionId}: quest {Quest} was taken but the session holds no "
                    + "entry result, so the offer list was not refreshed",
                context.Connection.ConnectionId, message.QuestId);
            return;
        }

        // The offer list is worked out again, because taking a quest on changes one of its inputs.
        // The quest just accepted leaves it, which is a deliberate difference from the reference
        // server: its list builder is never given the carried set and therefore names a quest the
        // character is already holding. The client believes that list -- it raised its own accept
        // prompt again for a quest just accepted, and the second acceptance was refused. Seen live
        // on 2026-08-27 and reproduced by the reference server on the same client. Recorded in
        // Docs/ProtocolScaffoldDissonances.md.
        result = await offers.RefreshAsync(
            result,
            session.AccountId,
            session.CharacterId,
            result.Character.Progression.Level,
            cancellationToken).ConfigureAwait(false);
        runtimeSessions.ReplaceExact(session, result);

        await sender.SendExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery(
                [
                    new AvailableQuestListMessage(
                        result.Character.Progression.Level, result.World.Content.AvailableQuests),
                ],
                []),
            cancellationToken).ConfigureAwait(false);
    }

    private ValueTask AnswerAsync(
        NetworkDispatchContext context,
        AcceptQuestAcknowledgement acknowledgement,
        CancellationToken cancellationToken) =>
        sender.SendExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery([acknowledgement], []),
            cancellationToken);
}
