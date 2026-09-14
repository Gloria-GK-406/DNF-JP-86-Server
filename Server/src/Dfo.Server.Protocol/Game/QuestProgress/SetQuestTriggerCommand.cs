using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Protocol.Game.QuestProgress;

/// <summary>Why a report was refused, in the code the client's answer carries.</summary>
/// <remarks>
/// One number, which is all the reference server ever answers with here: it builds every refusal
/// of this frame from <c>QuestSetTriggerResult.Fail(22)</c>. A malformed report, a report about a
/// quest the archive does not declare, and a report with no character selected are indistinguishable
/// on the wire -- there, and therefore here.
/// </remarks>
public enum QuestTriggerRefusal : byte
{
    /// <summary>The report cannot be acted on. The reference server's only code for this frame.</summary>
    Unusable = 22,
}

/// <summary>What became of one report that a quest moved.</summary>
/// <param name="TriggerValue">
/// The progress to tell the client, which is the stored value whether or not the report moved it.
/// </param>
public sealed record QuestTriggerOutcome(
    bool Answered,
    ushort QuestId,
    uint TriggerValue,
    QuestAdvanceVerdict Verdict)
{
    public static QuestTriggerOutcome Refuse(ushort questId) =>
        new(false, questId, 0, QuestAdvanceVerdict.Refused);

    /// <summary>
    /// The answer for a quest the character is not carrying: told back with no progress at all.
    /// </summary>
    /// <remarks>
    /// The reference server's behaviour, and it is deliberately not a refusal. The client can
    /// report on a quest it thinks it has and the server does not -- after a give-up that crossed
    /// the report in flight, most plainly -- and answering zero is what stops it asking again.
    /// </remarks>
    public static QuestTriggerOutcome NotCarried(ushort questId) =>
        new(true, questId, 0, QuestAdvanceVerdict.NotCarried);
}

/// <summary>One character reporting that one quest moved.</summary>
public sealed record SetQuestTriggerCommand(
    AccountId AccountId,
    CharacterId CharacterId,
    ushort QuestId,
    byte Selector,
    bool Increment) : ICommand<QuestTriggerOutcome>;

/// <summary>
/// Hands the report to the quest log and turns its verdict into what the client is told.
/// </summary>
/// <remarks>
/// Nothing here decides whether the report advances anything. Which quest it is comes from the
/// archive, what the report does to it comes from the log, and what is left is the order the two
/// are asked in and which byte carries which answer.
/// </remarks>
public sealed class SetQuestTriggerCommandHandler(
    IQuestDefinitionDomainService quests,
    IQuestTriggerDomainService progress)
    : ICommandHandler<SetQuestTriggerCommand, QuestTriggerOutcome>
{
    public async ValueTask<QuestTriggerOutcome> HandleAsync(
        CommandExecutionContext context,
        SetQuestTriggerCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        // Asked of every request rather than only of the ones that reach a write, for the same
        // reason acceptance asks it: a refusal returns before anything is staged, so a
        // registration mistake would otherwise surface only on the reports that moved something.
        if (context.ExecutionKind is not CommandExecutionKind.Transactional)
        {
            throw new InvalidOperationException(
                "A quest trigger report writes a row that outlives the process, so it has to be "
                + $"registered transactional; this execution is {context.ExecutionKind}.");
        }

        var definition = await quests.FindAsync(command.QuestId, cancellationToken)
            .ConfigureAwait(false);
        if (definition is null)
            return QuestTriggerOutcome.Refuse(command.QuestId);

        var advance = await progress
            .ApplyClientReportAsync(
                command.AccountId,
                command.CharacterId,
                definition,
                command.Selector,
                command.Increment,
                cancellationToken)
            .ConfigureAwait(false);

        return advance.Verdict switch
        {
            QuestAdvanceVerdict.Refused => QuestTriggerOutcome.Refuse(command.QuestId),
            QuestAdvanceVerdict.NotCarried => QuestTriggerOutcome.NotCarried(command.QuestId),
            _ => new QuestTriggerOutcome(
                true, command.QuestId, advance.Trigger.PackedValue, advance.Verdict),
        };
    }
}
