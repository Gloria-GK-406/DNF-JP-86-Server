using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

/// <summary>
/// Reads what each domain holds and reports what it adds to an award.
/// </summary>
/// <remarks>
/// Fetching and deciding are separate here on purpose. Each private method reaches one domain and
/// hands what it found to <see cref="ExperienceBonusRules"/>, which decides without touching
/// anything -- so the rules can be tested on plain data, and this class is only ever wrong about
/// where a fact lives.
/// <para>
/// It is one class rather than a registry of self-reporting bonuses because "which of a
/// character's things affect experience" is knowledge about experience, not about the character.
/// One domain is read today and the others -- a channel's rate, a timed effect from an elixir, a
/// world-wide event -- arrive as further methods of the same shape. If they outgrow one file, each
/// is already a whole that lifts out.
/// </para>
/// </remarks>
internal sealed class ExperienceBonusService(
    ISelectedSessionDirectory sessions,
    ISelectedSessionRuntimeDirectory runtimeSessions,
    ExperienceEntitlementPolicy policy,
    ExperienceChannelPolicy channel,
    TimeProvider clock) : IExperienceBonusService
{
    public IReadOnlyList<ExperienceModifier> Collect(in ExperienceOccasion occasion)
    {
        var found = new List<ExperienceModifier>();
        this.CollectAccountEntitlements(occasion, found);
        ExperienceBonusRules.FromChannel(occasion.Event, channel, found);
        return found;
    }

    /// <remarks>
    /// The account's premium terms, which arrive with the character's entry and sit in the
    /// published session -- so this reads memory, not the database, which is what lets the whole
    /// interface be synchronous.
    /// </remarks>
    private void CollectAccountEntitlements(
        in ExperienceOccasion occasion, List<ExperienceModifier> into)
    {
        var session = sessions.FindByCharacter(occasion.Character);
        if (session is null || !runtimeSessions.TryGet(session, out var result, out _)
            || result is null)
        {
            return;
        }

        ExperienceBonusRules.FromAccountEntitlements(
            result.Account.Entitlements.Premiums,
            clock.GetUtcNow(),
            occasion.Event,
            policy,
            into);
    }
}
