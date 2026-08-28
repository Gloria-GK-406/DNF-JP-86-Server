using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Application.World;

/// <summary>
/// What each account entitlement adds to an award, and how long it is worth adding.
/// </summary>
/// <remarks>
/// <b>Policy for now, and not because the archive is silent.</b> An earlier version of this note
/// claimed every premium type in <c>etc/premiumlist.etc</c> declares nothing. It does not: three
/// of them carry a rate -- two <c>[exp]</c> sections of 5 and one <c>[bonus exp]</c> of 20 -- which
/// an independent read of the A21 archive found on 2026-08-29. The claim was wrong in exactly the
/// way this server's channel rate was wrong for a day: a number was called policy because nobody
/// had looked.
/// <para>
/// Reading them is the next piece of work rather than this one, because it needs the mapping from
/// a declaration in that file to the type an account row carries, and inventing that mapping to
/// close a comment would be the same mistake a third time. Until then the rates are an operator's,
/// and empty by default rather than filled with a plausible guess.
/// </para>
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

    /// <summary>What the channel the character is on adds to this occasion.</summary>
    /// <remarks>
    /// The rate arrives already looked up, because the lookup needs two facts this rule has no
    /// business holding: which channel the connection came in on, and which dungeon the run is in.
    /// Both are read next door and both come from the archive's own declaration -- a channel pays
    /// for its own dungeons and for no others.
    /// <para>
    /// A kill and nothing else. The captures cover kills; the settlement at the end of a run pays
    /// nothing at all yet, so there is no base for a clear-side bonus to be a proportion of.
    /// </para>
    /// </remarks>
    public static void FromChannel(
        ExperienceEvent occasion,
        double rate,
        ICollection<ExperienceModifier> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        if (occasion is ExperienceEvent.MonsterKill && rate > 0)
        {
            into.Add(new ExperienceModifier(
                "channel", rate, ExperienceBonusKind.SeriaBlessing));
        }
    }
}
