using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.World;

/// <summary>What an award is being worked out for.</summary>
/// <remarks>
/// Not every entitlement applies to every occasion, and the archive says so plainly: an elixir's
/// own text promises double experience for <i>clearing</i> a dungeon, and the reference server
/// pays its avatar and creature bonuses only at settlement. An entitlement that reported the same
/// rate everywhere would pay it three times over a run.
/// </remarks>
public enum ExperienceEvent
{
    MonsterKill,
    DungeonClear,
    QuestCompletion,
}

/// <summary>Who an award is for and what brought it about.</summary>
/// <remarks>
/// Identity, not state. Each source reaches its own domain with what it already holds; a context
/// carrying the facts instead would have to know every domain, and adding a source would mean
/// editing it rather than writing a new one.
/// </remarks>
public readonly record struct ExperienceOccasion(
    ExperienceEvent Event,
    AccountId Account,
    CharacterId Character,
    int DungeonId);

/// <summary>
/// Which of the client's experience lines a bonus belongs to.
/// </summary>
/// <remarks>
/// The client's experience popup is not a free-form list: <c>NOTI 0x0025</c> has one fixed slot
/// per named line, and the client works out the "base" line by subtracting every slot it was sent
/// from the total it gained (<c>0x01179476</c> onwards in the A21 client). So a bonus that is paid
/// but declared in no slot is not merely undrawn -- it is drawn as part of the base.
/// <para>
/// <see cref="Unlisted"/> is that state, named rather than left implicit. It is what a bonus whose
/// line this server has not identified must use, and it is honest: the total is right, and the
/// breakdown attributes the amount to the occasion instead of to the entitlement.
/// </para>
/// </remarks>
public enum ExperienceBonusKind
{
    /// <summary>Paid, but belonging to no slot this server has identified.</summary>
    Unlisted,

    /// <summary>The channel's own bonus, drawn from the slot at <c>+0x4B</c>.</summary>
    Channel,
}

/// <summary>One reason a character earns more than the occasion is worth.</summary>
/// <param name="Source">
/// What to call it where a person will read it. Required, not decorative: experience is a number
/// nobody can check by looking, and an award that cannot say where it came from can only be
/// debugged by rerunning the game.
/// </param>
/// <param name="Rate">How much of the occasion's worth this adds.</param>
public readonly record struct ExperienceModifier(
    string Source, double Rate, ExperienceBonusKind Kind = ExperienceBonusKind.Unlisted);

/// <summary>
/// Everything that increases what a character earns beyond what the occasion itself is worth.
/// </summary>
/// <remarks>
/// One service rather than a registry of self-reporting bonuses, because "which of a character's
/// things affect experience" is knowledge about experience, not about the character. A character
/// holds an elixir effect and does not know what it is for; an account holds a premium term and
/// does not know either. Splitting that knowledge across the domains it reads would scatter one
/// concern without reducing it.
/// <para>
/// <b>Synchronous on purpose.</b> Every fact this needs is already resident -- an account's
/// entitlements arrive with the character's entry, a channel is session state -- and a source that
/// wanted a database round trip on every kill would be a fact stored in the wrong place. Returning
/// no task makes that impossible to write by accident rather than merely discouraged.
/// </para>
/// </remarks>
public interface IExperienceBonusService
{
    /// <summary>What this character earns on top of this occasion, each reason named.</summary>
    IReadOnlyList<ExperienceModifier> Collect(in ExperienceOccasion occasion);
}

/// <summary>
/// A one-shot measurement: pay a distinct amount into every named line of the
/// experience frame, so the client labels them itself.
/// </summary>
/// <remarks>
/// The frame has fourteen bonus slots and this server knows the name of one of
/// them, because the client drew it: what the reference server calls the
/// channel bonus is drawn as "Seria's welcome". Its labels for the other
/// thirteen are therefore evidence of nothing, and copying them would repeat
/// one known error thirteen times.
/// <para>
/// So the names are asked of the client instead. With this on, a kill pays
/// 1 into the first slot, 2 into the second and so on, and the popup comes back
/// with every line named and identifiable by its amount. One kill, all fourteen.
/// It is off by default and belongs to a measurement session, not to play: what
/// it pays is invented, and the frame it produces describes bonuses nobody
/// earned.
/// </para>
/// </remarks>
public sealed record ExperienceDiagnostics(bool LabelProbe)
{
    /// <summary>Ordinary play: no probe.</summary>
    public static ExperienceDiagnostics None { get; } = new(false);

    /// <summary>What each slot is declared as, in the order the frame writes them.</summary>
    /// <remarks>
    /// Powers of two from 2, so the base line the client draws identifies the
    /// subtracted slots exactly. The client works the base out by subtracting
    /// the lines it accounts for, and a first run showed it accounts for one
    /// point more than it draws; with distinct bits the shortfall is a mask and
    /// names that set outright instead of leaving it to arithmetic.
    /// <para>
    /// Bit 0 is deliberately unused. If the shortfall carries it, the odd point
    /// comes from a field none of these slots is -- the account's honour level,
    /// which is 1 and is the standing suspicion.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<uint> ProbeAmounts { get; } =
        [2, 4, 8, 16, 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384];

    /// <summary>The whole declared amount: every slot, and nothing else.</summary>
    public static uint ProbeTotal { get; } = 32766;
}
