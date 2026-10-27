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
internal sealed record QuestTriggerInput(
    AccountId AccountId,
    CharacterId CharacterId,
    ushort QuestId,
    byte Selector,
    bool Increment) ;
