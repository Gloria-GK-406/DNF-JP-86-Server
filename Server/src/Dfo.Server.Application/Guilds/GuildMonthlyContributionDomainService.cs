using System.Globalization;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Scheduling;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildMonthlyContributionDomainService(
    IGuildDomainStore guilds, IGuildMonthlyContributionStore contributions, GuildMonthlyContributionRuleService rules,
    IItemDeliveryDomainService items, TimeProvider clock)
    : IGuildMonthlyContributionDomainService
{
    public async ValueTask<bool> RecordAttendanceAsync(CharacterId character, DateTimeOffset now, CancellationToken ct)
    {
        if (rules.ReadMonthlyContribution() is null
            || await guilds.FindMembershipAsync(character, ct).ConfigureAwait(false) is not { } membership)
            return false;

        var award = GuildMonthlyContributionRuleService.AttendanceAward(membership.Member.IsLeader);
        var day = DateOnly.FromDateTime(now.ToOffset(IDailyResetRuleService.BusinessOffset).DateTime.AddHours(-6));
        return await contributions.RecordAsync(membership.Guild.Id, character, award.Source,
            day.DayNumber.ToString(CultureInfo.InvariantCulture), award.Points, now, ct).ConfigureAwait(false);
    }

    public async ValueTask RecordClearAsync(CharacterId character, DungeonRunState run, int level, CancellationToken ct)
    {
        if (!run.ClearOpen || run.RunId.Value == Guid.Empty || !run.Layout.ConsumesFatigue
            || run.Layout.Mode.Kind is not DungeonRunKind.Ordinary
            || run.ClearParticipants.FirstOrDefault(participant => participant.CharacterId == character) is not { } participant
            || rules.ReadMonthlyContribution() is not { } rule
            || await guilds.FindMembershipAsync(character, ct).ConfigureAwait(false) is not { } member
            || run.ClearedAt is null || !participant.HasMembership(member.Member)
            || !await rules.IsRecommendedAsync(run.Layout.DungeonId, level, ct).ConfigureAwait(false))
            return;

        await contributions.RecordAsync(member.Guild.Id, character, GuildContributionSource.RecommendedClear,
            run.RunId.Value.ToString("N"), rule.RecommendedClearPoints, clock.GetUtcNow(), ct).ConfigureAwait(false);
    }

    public ValueTask<GuildContributionTotals> ReadTotalsAsync(GuildId guild, CancellationToken ct)
    {
        var start = GuildContributionMonth.StartAt(clock.GetUtcNow());
        return contributions.ReadTotalsAsync(guild, start, start.AddMonths(1), ct);
    }

    public async ValueTask<IReadOnlyList<GuildContributionEntry>?> ReadHistoryAsync(
        CharacterId viewer, int limit, CancellationToken ct) =>
        await guilds.FindMembershipAsync(viewer, ct).ConfigureAwait(false) is { } membership
            ? await contributions.ReadHistoryAsync(membership.Guild.Id, limit, ct).ConfigureAwait(false)
            : null;

    internal async ValueTask<GuildMonthlySettlementBatch> SettleBatchAsync(
        DateTimeOffset boundary, CharacterId? after, int limit, CancellationToken ct)
    {
        if (rules.ReadMonthlyContribution() is not { } rule)
            return default;

        var end = GuildContributionMonth.StartAt(boundary);
        var start = end.AddMonths(-1);
        var candidates = await contributions.ReadUnpaidAsync(start, end, after, limit, ct).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        foreach (var member in candidates)
        {
            if (GuildMonthlyContributionRuleService.EvaluateReward(rule, member.Points, now) is not { } reward)
                continue;
            var deliveryKey = FormattableString.Invariant($"guild-monthly-contribution:{start:yyyyMM}:{member.Character.Value}");
            await items.DeliverAsync(member.Account, member.Character, [reward.Item], "公会月度贡献奖励",
                ct, ItemDeliveryMode.MailOnly, new(null, deliveryKey) { MailExpiresAt = reward.MailExpiresAt }).ConfigureAwait(false);
            await contributions.MarkPaidAsync(member, start, ct).ConfigureAwait(false);
        }
        return new(candidates.Count > 0 && candidates.Count == limit ? candidates[^1].Character : null);
    }
}

internal readonly record struct GuildMonthlySettlementBatch(CharacterId? NextAfter);

internal sealed class GuildContributionMonth : IScheduledTaskSchedule
{
    public static DateTimeOffset StartAt(DateTimeOffset instant)
    {
        var local = instant.ToOffset(IDailyResetRuleService.BusinessOffset);
        var first = new DateTimeOffset(local.Year, local.Month, 1, 6, 0, 0, local.Offset);
        return local < first ? first.AddMonths(-1) : first;
    }

    public DateTimeOffset LastOccurrence(DateTimeOffset at) => StartAt(at).ToUniversalTime();

    public DateTimeOffset NextOccurrence(DateTimeOffset after) => StartAt(after).AddMonths(1).ToUniversalTime();
}
