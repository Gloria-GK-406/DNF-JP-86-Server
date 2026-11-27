using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildOverviewDomainService(IGuildMembershipQueryDomainService query, IGuildAttendanceDomainService attendance,
    IGuildMonthlyContributionDomainService monthly, IGuildBenefitDomainService benefits, IGuildAllianceDomainService alliances) : IGuildOverviewDomainService
{
    public async ValueTask<GuildOverview?> ReadAsync(CharacterId viewer, CancellationToken ct)
    {
        if (await query.FindMembershipAsync(viewer, ct).ConfigureAwait(false) is not { } membership)
            return null;

        var members = await query.ReadRosterAsync(viewer, ct).ConfigureAwait(false);
        var attended = await attendance.ReadAttendanceTodayAsync(viewer, ct).ConfigureAwait(false);
        var ranks = await query.ReadRankSettingsAsync(membership.Guild.Id, ct).ConfigureAwait(false);
        var contents = await benefits.ReadContentsAsync(membership.Guild.Id, ct).ConfigureAwait(false);
        var ally = await alliances.FindAllyAsync(membership.Guild.Id, ct).ConfigureAwait(false);
        var contributions = await monthly.ReadTotalsAsync(membership.Guild.Id, ct).ConfigureAwait(false);
        return new(membership.Guild, members, attended.Count, ranks, contents, ally, contributions);
    }
}
