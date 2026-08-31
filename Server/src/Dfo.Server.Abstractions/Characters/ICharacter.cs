using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Characters;

/// <summary>
/// One character, as something to ask rather than an identifier to look up.
/// </summary>
/// <remarks>
/// It is bound to one character, so its methods do not take one. It holds nothing: every answer is
/// read when it is asked for, which is what keeps it from disagreeing with a use case that rolled
/// back.
///
/// It is an entry point, not a container. What belongs here is what belongs to the character's own
/// body; a bag, a quest log and a skill page are each their own thing, reached through their own
/// repository rather than bolted on here.
///
/// A character can only answer while it is the one being played -- what it reads exists only then.
/// That is not a lifetime of its own: nothing is prepared for it and nothing is released, and
/// asking about a character nobody is playing gets the same answer it always did.
/// </remarks>
public interface ICharacter
{
    CharacterId Id { get; }

    /// <summary>
    /// Whether this character may use the item, from the archive's own declaration.
    /// </summary>
    /// <remarks>
    /// A question, and only a question: nothing here starts a cooldown, so asking twice is the same
    /// as asking once. Starting one belongs to the effects, which run after a use succeeds.
    /// </remarks>
    ValueTask<ItemUsability> CanUseAsync(int itemId, CancellationToken cancellationToken);
}

/// <summary>
/// What a character is and where it stands, in the terms the archive's declaration is written in.
/// </summary>
/// <remarks>
/// The judgement takes this rather than a character, which is the whole point of separating them:
/// the four comparisons can then be made without anything that knows how to find a character, and
/// checked without constructing one.
/// </remarks>
public sealed record ItemUseCandidate(
    byte Level,
    QuestJobRequirement JobFamily,
    ItemUsePlace Place,
    bool IsCoolingDown);

/// <summary>
/// Judges the archive's declaration against a candidate. Knows how to find nothing.
/// </summary>
public interface IItemUseJudgementService
{
    /// <summary>
    /// What the archive declares about using this item, or null when it declares nothing usable
    /// under that identifier.
    /// </summary>
    /// <remarks>
    /// Read here rather than by each caller, so that the declaration a judgement is made against
    /// is the same one the effects are planned from.
    /// </remarks>
    ValueTask<ItemUseRule?> RuleForAsync(int itemId, CancellationToken cancellationToken);

    /// <summary>The cooldown this item shares, or its own when it shares none.</summary>
    ItemCooldownKey CooldownKeyFor(int itemId, ItemUseRule rule);

    /// <summary>
    /// Which of the declared restrictions this candidate falls foul of, or that it falls foul of
    /// none.
    /// </summary>
    /// <remarks>
    /// The order is the answer as much as the comparisons are. The permanent reasons are asked
    /// first: a cooldown expires and a level does not, so reporting the cooldown in front of a
    /// level the character has not reached sends the player away to wait for something that will
    /// not help them -- and cooldowns are shared, so the item that started the wait need not be the
    /// item being refused.
    /// </remarks>
    ItemUsability Judge(ItemUseRule rule, ItemUseCandidate candidate);
}
