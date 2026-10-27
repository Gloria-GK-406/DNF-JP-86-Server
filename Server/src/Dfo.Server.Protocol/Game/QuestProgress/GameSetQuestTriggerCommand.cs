using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Protocol.Game.QuestProgress;

/// <summary>
/// Moves the quest on and answers with where it now stands.
/// </summary>
/// <remarks>
/// One frame leaves here, and the important thing about it is that it leaves at all. Before this
/// existed the client clicked 赛丽亚, sent the report, waited, and sent it again -- five copies of
/// the same frame in one live session, because an unanswered report is one the client assumes was
/// lost.
///
/// The reference server sends nothing else on this path: no refreshed offer list, no carried-quest
/// list. A quest reaching zero here is a quest whose objective is met, not a quest finished --
/// finishing it is FINISH_QUEST's, and the lists are re-sent there.
/// </remarks>
public sealed record GameSetQuestTriggerCommand(NetworkDispatchContext Dispatch, SetQuestTriggerRequest Input) : IProtocolCommand<QuestTriggerOutcome>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed class GameSetQuestTriggerCommandHandler(
    ISelectedSessionDomainService selectedSessions,
    ICommandWorkUnits units,
    IGameCommandOutbox outbox,
    ILogger<GameSetQuestTriggerCommandHandler> logger,
    IQuestProgressDomainService progress)
    : ICommandHandler<GameSetQuestTriggerCommand, QuestTriggerOutcome>
{
    public async ValueTask<QuestTriggerOutcome> HandleAsync(CommandExecutionContext execution, GameSetQuestTriggerCommand command, CancellationToken cancellationToken)
    {
        var context = command.Dispatch;
        var message = command.Input;
        var session = selectedSessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null)
        {
            // No character is selected, so there is no log to move. Refused rather than ignored:
            // silence is what made the client repeat itself.
            logger.LogWarning(
                "Quest trigger {ConnectionId}: quest {Quest} with no character selected",
                context.Connection.ConnectionId, message.QuestId);
            await this.AnswerAsync(
                context,
                new SetQuestTriggerAcknowledgement(
                    false, message.QuestId, 0, (byte)QuestTriggerRefusal.Unusable),
                cancellationToken).ConfigureAwait(false);
            return QuestTriggerOutcome.Refuse(message.QuestId);
        }

        var outcome = await units.WriteAsync(execution, new("SetQuestTrigger", []),
            (_, token) => ReportAsync(new(session.AccountId, session.CharacterId, message.QuestId,
                message.Selector, message.Increment), token), cancellationToken).ConfigureAwait(false);

        // At warning, because this one is a gap rather than a decision. The quest is an
        // item-seeking one, whose progress is a count of what is in the bag, and this server does
        // not recompute it here -- so the value answered is the stored one and may be stale on
        // screen. Nothing in the tutorial reaches it; a live line naming the quest is how anyone
        // finds out something else did.
        if (outcome.Verdict == QuestAdvanceVerdict.NotEvaluable)
        {
            logger.LogWarning(
                "Quest trigger {ConnectionId}: quest {Quest} is counted from the bag and this "
                    + "server does not recompute it, so it was answered with the stored {Trigger}",
                context.Connection.ConnectionId, message.QuestId, outcome.TriggerValue);
        }
        else
        {
            logger.LogInformation(
                "Quest trigger {ConnectionId} account={Account}: quest={Quest} "
                    + "selector=0x{Selector:X2} {Direction} -> {Verdict} trigger={Trigger}",
                context.Connection.ConnectionId,
                session.AccountId,
                message.QuestId,
                message.Selector,
                message.Increment ? "one more" : "one fewer",
                outcome.Verdict,
                outcome.TriggerValue);
        }

        await this.AnswerAsync(
            context,
            new SetQuestTriggerAcknowledgement(
                outcome.Answered,
                outcome.QuestId,
                outcome.TriggerValue,
                (byte)QuestTriggerRefusal.Unusable),
            cancellationToken).ConfigureAwait(false);
        return outcome;
    }

    private ValueTask AnswerAsync(
        NetworkDispatchContext context,
        SetQuestTriggerAcknowledgement acknowledgement,
        CancellationToken cancellationToken) =>
        outbox.RegisterExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery([acknowledgement], []),
            cancellationToken);

    internal async ValueTask<QuestTriggerOutcome> ReportAsync(
        QuestTriggerInput command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var reported = await progress.ReportAsync(
            command.AccountId,
            command.CharacterId,
            [new ClientQuestTriggerReport(
                command.QuestId, command.Selector, command.Increment)],
            cancellationToken).ConfigureAwait(false);
        var advance = reported.ClientResponse
            ?? throw new InvalidDataException("A client quest report produced no CMD response.");

        return advance.Verdict switch
        {
            QuestAdvanceVerdict.Refused => QuestTriggerOutcome.Refuse(command.QuestId),
            QuestAdvanceVerdict.NotCarried => QuestTriggerOutcome.NotCarried(command.QuestId),
            _ => new QuestTriggerOutcome(
                true, command.QuestId, advance.Trigger.PackedValue, advance.Verdict),
        };
    }
}
