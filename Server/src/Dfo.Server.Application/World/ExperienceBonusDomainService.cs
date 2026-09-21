using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Persistence;
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
/// Two sources are read today -- the account's premium terms and the channel's own reward -- and
/// the others still to come, a timed effect from an elixir or a world-wide event, arrive the same
/// way. If they outgrow one file, each is already a whole that lifts out.
/// </para>
/// </remarks>
internal sealed class ExperienceBonusDomainService(
    ISelectedSessionDomainService sessions,
    ISelectedSessionRuntimeDirectory runtimeSessions,
    IPremiumServiceDomainService premiums,
    IChannelEntryPolicy channels,
    IExperienceRuleService experienceRules,
    TimeProvider clock) : IExperienceBonusDomainService
{
    public async ValueTask<IReadOnlyList<ExperienceModifier>> CollectAsync(
        ExperienceOccasion occasion, CancellationToken cancellationToken)
    {
        var session = sessions.FindByCharacter(occasion.Character);
        if (session is null || !runtimeSessions.IsPublishedExact(session))
        {
            // Nothing resident to read. Every bonus this service knows about is a fact about the
            // published session -- the account's terms, the channel the connection came in on --
            // so without one there is nothing to add, rather than something to guess at.
            return [];
        }

        var currentPremiums = await premiums.For(occasion.Account).ReadAsync(cancellationToken)
            .ConfigureAwait(false);
        var channel = await channels.ResolveAsync(session.ListenerPort, cancellationToken)
            .ConfigureAwait(false);
        if (sessions.FindByCharacter(occasion.Character) != session
            || !runtimeSessions.IsPublishedExact(session))
            return [];
        return experienceRules.ModifiersFor(
            occasion.Event,
            currentPremiums.Select(row => new Dfo.Server.Data.Accounts.AccountPremiumEntryState(
                row.PremiumType, row.ExpiresAtUnixSeconds)).ToArray(),
            clock.GetUtcNow(),
            channel.ChannelId,
            occasion.DungeonId);
    }
}
