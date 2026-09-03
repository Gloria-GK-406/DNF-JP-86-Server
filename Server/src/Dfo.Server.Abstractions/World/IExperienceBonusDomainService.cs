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
/// The nine named members are the nine slots the client both draws and subtracts. Their names are
/// the client's own, read off the screen: a probe paid a distinct amount into all fourteen slots
/// and the popup labelled each line itself. That measurement, and the two rounds that
/// cross-validated it, are written up in <c>Docs/ExperienceNoticeSlots.md</c>.
/// </para>
/// <para>
/// <b>Five slots are deliberately absent.</b> <c>+0x2F</c> is drawn but not subtracted and
/// <c>+0x33</c> is subtracted but not drawn, so either one makes the breakdown the player reads
/// stop adding up -- and the whole value of this notice is that it adds up. <c>+0x22</c>,
/// <c>+0x37</c> and <c>+0x47</c> are neither drawn nor subtracted, so writing to them has no
/// observable effect at all. The reference server has names for several of these; those names are
/// what makes them look fillable, and two of its names for slots that ARE usable were measured
/// wrong. Do not add a member for any of the five.
/// </para>
/// </remarks>
public enum ExperienceBonusKind
{
    /// <summary>Paid, but belonging to no slot this server has identified.</summary>
    /// <remarks>
    /// The client folds it into the base line. The total stays right and the breakdown attributes
    /// the amount to the occasion instead of to the entitlement -- one line fewer, rather than a
    /// line that does not balance.
    /// </remarks>
    Unlisted,

    /// <summary>队伍奖励, drawn from <c>+0x05</c>.</summary>
    PartyReward,

    /// <summary>疲劳值Buff, drawn from <c>+0x15</c>.</summary>
    FatigueBuff,

    /// <summary>
    /// 赛丽亚的祝福奖励, drawn from <c>+0x1A</c>. This is the channel's own reward.
    /// </summary>
    /// <remarks>
    /// The slot moved here on 2026-08-28 and the reason is worth keeping. The reference server
    /// wrote its channel bonus at <c>+0x4B</c> and this server copied that; the client draws
    /// <c>+0x4B</c> as 赛丽亚的欢迎, which is a different reward. Both the slot and the name came
    /// from the same source and both were wrong, which is why every name here was re-measured.
    /// </remarks>
    SeriaBlessing,

    /// <summary>成长之契约奖励, drawn from <c>+0x1E</c>.</summary>
    GrowthContract,

    /// <summary>疲劳值燃烧奖励, drawn from <c>+0x26</c>.</summary>
    FatigueBurn,

    /// <summary>网吧奖励, drawn from <c>+0x2A</c>.</summary>
    InternetCafe,

    /// <summary>极速成长Buff奖励, drawn from <c>+0x43</c>.</summary>
    RapidGrowthBuff,

    /// <summary>赛丽亚的欢迎, drawn from <c>+0x4B</c>.</summary>
    /// <remarks>
    /// Distinct from <see cref="SeriaBlessing"/> despite the similar name, and the pair is exactly
    /// how the reference server's labelling was caught: a bonus written here appears under this
    /// name, not that one.
    /// </remarks>
    SeriaWelcome,

    /// <summary>公会奖励, drawn from <c>+0x4F</c>.</summary>
    GuildReward,
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
public interface IExperienceBonusDomainService
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
