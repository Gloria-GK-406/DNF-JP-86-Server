using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Application.World;

/// <summary>
/// What each account entitlement adds to an award, and how long it is worth adding.
/// </summary>
/// <remarks>
/// <b>Policy, not content.</b> The archive declares an experience rate for an item that grants one
/// -- 484 stackables carry an <c>[exp bonus rate]</c> of their own -- but it declares none for a
/// premium term: every one of the fifty-two types in <c>etc/(r)premiumlist.etc</c> has an empty
/// <c>[bonus]</c> section. So this is a number an operator sets, and it is empty by default rather
/// than filled with a plausible one. A rate invented here would be indistinguishable from a rate
/// read from somewhere, which is the confusion this whole subject has already cost a day to.
/// </remarks>
/// <param name="RatesByPremiumType">
/// What each premium type adds, keyed by the type the account row carries. A type absent from this
/// map adds nothing.
/// </param>
public sealed record ExperienceEntitlementPolicy(IReadOnlyDictionary<int, double> RatesByPremiumType)
{
    /// <summary>No entitlement adds anything, which is the state of an unconfigured server.</summary>
    public static ExperienceEntitlementPolicy None { get; } =
        new(new Dictionary<int, double>());

    /// <summary>What this premium type adds, or zero when it is not configured to add anything.</summary>
    public double RateFor(int premiumType) =>
        this.RatesByPremiumType.TryGetValue(premiumType, out var rate) && rate > 0 ? rate : 0;
}

/// <summary>
/// What the channel a character is playing on adds to a kill.
/// </summary>
/// <remarks>
/// <b>Measured, not transcribed.</b> The four captured kills each paid more than
/// <see cref="MonsterExperienceRules"/> accounts for, and the excess is the same rule every time:
/// 62 paid 65, 72 paid 75, 126 paid 132 and 146 paid 153, which is <c>floor(worth x 0.05)</c> at
/// 3, 3, 6 and 7. Four samples, four agreements, and the only rate that fits all four.
/// <para>
/// It is policy rather than content: nothing in the archive declares it. <c>channel_info.etc</c>
/// lists which dungeons a channel carries and no rates at all, and no other file found declares an
/// experience rate per channel. So the default here is what was measured on the one channel that
/// was measured, and an operator running channels that differ has to say so.
/// </para>
/// <para>
/// Only a kill. The captures cover kills, and the settlement at the end of a run is not
/// implemented here at all; paying it at an occasion nothing measured would be inventing a second
/// rule out of the evidence for the first.
/// </para>
/// </remarks>
/// <param name="Rate">What the channel adds to what a kill is worth.</param>
public sealed record ExperienceChannelPolicy(double Rate)
{
    /// <summary>The rate the captured session was paying.</summary>
    public static ExperienceChannelPolicy Measured { get; } = new(0.05);

    /// <summary>A channel that adds nothing.</summary>
    public static ExperienceChannelPolicy None { get; } = new(0);
}

/// <summary>
/// Decides what an already-fetched set of facts adds to an award.
/// </summary>
/// <remarks>
/// Pure, and separate from the service that fetches for it, so that every rule here can be checked
/// against plain data rather than against a session, a clock and a directory arranged to produce
/// it.
/// </remarks>
internal static class ExperienceBonusRules
{
    /// <summary>
    /// What the account's premium terms add, skipping the ones that have run out.
    /// </summary>
    /// <param name="now">
    /// Compared against each term's end. Taken from the caller rather than read here, because a
    /// rule that asked the clock could not be checked at a chosen moment.
    /// </param>
    /// <remarks>
    /// Every occasion is treated alike. Nothing observed says a premium term pays differently for a
    /// kill than for a clear, and inventing a distinction would be inventing a rule; the parameter
    /// is taken because the entitlements that do differ are the ones this will grow to hold.
    /// </remarks>
    public static void FromAccountEntitlements(
        IReadOnlyList<AccountPremiumEntryState> premiums,
        DateTimeOffset now,
        ExperienceEvent occasion,
        ExperienceEntitlementPolicy policy,
        ICollection<ExperienceModifier> into)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(into);
        _ = occasion;
        if (premiums is null)
            return;

        var nowUnix = now.ToUnixTimeSeconds();
        foreach (var premium in premiums)
        {
            // A term whose end has passed is not an entitlement. Zero and below are read as no
            // declared end rather than as an end in 1970: the account tables use them for a row
            // that carries no expiry, and treating them as expired would silently retire whatever
            // an operator configured.
            if (premium.ExpiresAtUnixSeconds > 0 && premium.ExpiresAtUnixSeconds <= nowUnix)
                continue;

            var rate = policy.RateFor(premium.PremiumType);
            if (rate > 0)
            {
                // Unlisted: the frame has no slot this server can name for a premium term, so the
                // client will draw the amount as part of the base rather than on a line of its
                // own. The total is right either way, and inventing a slot to draw it on would be
                // guessing at which of the frame's eleven named lines the client means by it.
                into.Add(new ExperienceModifier(
                    $"premium type {premium.PremiumType}", rate, ExperienceBonusKind.Unlisted));
            }
        }
    }

    /// <summary>What the channel adds to this occasion.</summary>
    /// <remarks>
    /// A kill and nothing else, for the reason given on <see cref="ExperienceChannelPolicy"/>: the
    /// rate was measured against kills, and the other two occasions would be carrying it on the
    /// strength of evidence that says nothing about them.
    /// </remarks>
    public static void FromChannel(
        ExperienceEvent occasion,
        ExperienceChannelPolicy policy,
        ICollection<ExperienceModifier> into)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(into);

        if (occasion is ExperienceEvent.MonsterKill && policy.Rate > 0)
            into.Add(new ExperienceModifier("channel", policy.Rate, ExperienceBonusKind.Channel));
    }
}
