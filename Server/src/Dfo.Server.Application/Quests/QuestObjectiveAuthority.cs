using Dfo.Server.Abstractions.Quests;

namespace Dfo.Server.Application.Quests;

/// <summary>Who counts one quest's progress, and therefore what a client report may do to it.</summary>
internal enum QuestObjectiveAuthority
{
    /// <summary>The client counts it. Its report moves the stored value.</summary>
    Client,

    /// <summary>
    /// The server counts it. The client reports anyway and is answered with the value already
    /// stored.
    /// </summary>
    Server,

    /// <summary>
    /// Neither: the value is derived from state elsewhere -- what is in the bag -- rather than
    /// counted by anybody.
    /// </summary>
    Derived,

    /// <summary>This objective does not take a report of that shape.</summary>
    None,
}

/// <summary>
/// Reads a quest's objective and says who is allowed to move its progress.
/// </summary>
/// <remarks>
/// This is the reference server's <c>QuestClientTriggerAuthority</c> table, kept as a table
/// because that is what it is: a vocabulary of about forty objective words, each mapping to one of
/// four answers, with no structure underneath to derive it from. Guessing the unlisted ones would
/// be the expensive mistake in both directions -- treating a server-counted objective as
/// client-counted lets the client finish a quest by asking, and the reverse leaves the player
/// clicking an NPC that never responds.
///
/// The words themselves come through <see cref="Abstractions.Content.QuestDefinition.ObjectiveTypeCode"/>
/// exactly as the archive spells them, which is why they are matched as strings here rather than
/// as an enumeration. The archive's vocabulary is not closed and this server has not confirmed all
/// of it; an unrecognised word answers <see cref="QuestObjectiveAuthority.Server"/>, which is the
/// reference server's default and the safe direction -- the client is answered, and nothing it
/// says is allowed to move a value on its own say-so.
/// </remarks>
internal static class QuestObjectives
{
    /// <summary>
    /// Objectives the client counts. A report on one of these moves the stored progress.
    /// </summary>
    /// <remarks>
    /// Two are missing from this set and handled in <see cref="AuthorityOver"/> instead, because
    /// their answer depends on the report as well as on the quest: <c>question</c> takes only the
    /// whole-word selector, and <c>seek n meet npc</c> is client-counted for the middle channel
    /// and derived for anything else.
    ///
    /// <c>hunt enemy</c> is in neither: the reference server reads a per-quest flag to decide
    /// whether the client or the server counts it, and this server does not read that flag. It
    /// falls to the default, which counts it here -- the objective goes unprogressed rather than
    /// being progressed on the client's word.
    /// </remarks>
    private static readonly HashSet<string> ClientCounted =
    [
        "meet npc", "seeking repeat", "get item", "condition under clear", "condition under clear2",
        "normal clear", "use item", "get score", "custom quest", "send chatting", "check life",
        "amplify item", "disjoint item", "equipped item", "check time", "use fortune coin",
        "meet secret npc", "turn gold card", "ui click", "assault count", "mobile",
        "accumulate play", "powerwar win", "belong to winning power", "powerwar point",
        "clear quest by grade", "clear a dungeon", "use skill", "pvp quest", "pvp match",
        "pvp rank", "raid phase clear", "level up", "first grow", "awakening",
        "get item check index", "quest accept",
    ];

    public static QuestObjectiveAuthority AuthorityOver(string? objectiveTypeCode, byte selector)
    {
        if (!QuestTrigger.IsReportable(selector))
            return QuestObjectiveAuthority.None;

        var objective = Normalize(objectiveTypeCode);
        if (objective.Length == 0)
            return QuestObjectiveAuthority.Server;

        // A question quest is answered, not counted: the whole-word selector is the answer
        // arriving, and any channel selector is a report about a quest of a shape this is not.
        if (objective == "question")
        {
            return selector == 0
                ? QuestObjectiveAuthority.Client
                : QuestObjectiveAuthority.None;
        }

        // Two objectives in one: finding the items and then taking them to somebody. The middle
        // channel is the visit, which the client counts; everything else is the item count, which
        // is read off the bag.
        if (objective == "seek n meet npc")
        {
            return selector == 0x20
                ? QuestObjectiveAuthority.Client
                : QuestObjectiveAuthority.Derived;
        }

        if (objective == "seeking")
            return QuestObjectiveAuthority.Derived;

        // Everything else this server counts. That is the reference server's default and it is
        // also its explicit answer for `hunt monster`, `clear map`, `clear quest` and
        // `quest clear`, which are listed there and answered the same way -- so they are not
        // listed again here, where a set that changed no answer would only look like one that did.
        return ClientCounted.Contains(objective)
            ? QuestObjectiveAuthority.Client
            : QuestObjectiveAuthority.Server;
    }

    /// <remarks>
    /// The content source already strips the archive's brackets, and a test holds it to that, so
    /// what arrives here is the bare word. Lower-cased anyway: the archive is not consistent about
    /// case, and a rule that turned on it would be a rule about typography.
    /// </remarks>
    private static string Normalize(string? objectiveTypeCode) =>
        objectiveTypeCode?.Trim().ToLowerInvariant() ?? string.Empty;
}
