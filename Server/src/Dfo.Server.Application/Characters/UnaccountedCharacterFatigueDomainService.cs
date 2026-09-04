using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Characters;

/// <summary>
/// The fatigue seam with no fatigue behind it: it answers a fixed pair and keeps no account.
/// </summary>
/// <remarks>
/// <b>Nothing reported to this is recorded anywhere, by design.</b> <see cref="SpendAsync"/> and
/// <see cref="RefundAsync"/> return having done nothing at all; <see cref="BalanceOfAsync"/>
/// answers the same two numbers before and after either of them, for every character, for the
/// server's whole lifetime. There is no store behind it and no table under it, because a store
/// that holds nothing is worse than none: it reads as a persistence path that already exists.
/// <para>
/// It is a placeholder and it is named one. What it stands in for is the fatigue system, which is
/// its own piece of work: a day's allowance, a charge per newly entered room, a floor, a refusal at
/// zero, a potion that gives points back and a daily reset. Putting the seam in first lets the
/// place that spends fatigue call it now -- so the call site is written, wired and tested against
/// the shape it will keep -- while the numbers on the wire stay exactly where they are. That
/// matters for more than tidiness: a real accounting would count a test session's fatigue down to
/// zero and lock the character out of the dungeon that the rest of this work is checked against.
/// </para>
/// <para>
/// The pair it answers is the pair the server already sends. <c>CharacterRosterService</c> builds
/// every entering character's <see cref="CharacterFatigueState"/> as a hardcoded
/// <c>(188, 0)</c> and the entry acknowledgement writes those two numbers straight out, so
/// answering anything else here would describe a character the client is not being told about.
/// The two literals are restated rather than shared because the roster's belongs to the frame it
/// fills and this one to the seam; the fatigue system replaces both at once.
/// </para>
/// </remarks>
internal sealed class UnaccountedCharacterFatigueDomainService : ICharacterFatigueDomainService
{
    /// <summary>The day's allowance every character is told they have.</summary>
    /// <remarks>
    /// The number a live A21 session was measured sending, and the number this server hardcodes:
    /// 156 as standard plus 32 for a black diamond, as the owner gives the rule.
    /// </remarks>
    public const ushort UnvaryingTotal = 188;

    /// <summary>How much every character is told they have spent, whatever they have done.</summary>
    public const ushort UnvaryingUsed = 0;

    /// <summary>Answers the fixed pair, ignoring which character is asked about.</summary>
    public ValueTask<CharacterFatigueBalance> BalanceOfAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new CharacterFatigueBalance(UnvaryingTotal, UnvaryingUsed));

    /// <summary>Accepts the report and discards it: nothing is spent and nothing is written.</summary>
    public ValueTask SpendAsync(
        CharacterId characterId, ushort points, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    /// <summary>Accepts the report and discards it: nothing is returned and nothing is written.</summary>
    public ValueTask RefundAsync(
        CharacterId characterId, ushort points, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}
