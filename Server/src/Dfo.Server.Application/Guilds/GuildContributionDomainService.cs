using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildContributionDomainService(IGuildDomainStore guilds, IGuildContributionStore store,
    GuildRedPacketRuleService rules, IItemDeliveryDomainService delivery, TimeProvider clock) : IGuildContributionDomainService
{
    public async ValueTask<ItemDeliveryReceipt?> AwardDailyAsync(SelectedSessionIdentity actor,
        GuildDailyContributionKind kind, CancellationToken ct)
    {
        var points = rules.DailyContribution(kind);
        if (points <= 0 || await guilds.FindMembershipAsync(actor.CharacterId, ct).ConfigureAwait(false) is not { } membership)
            return null;

        var now = clock.GetLocalNow();
        var day = GuildRedPacketDomainService.Day(now);
        var joined = TimeZoneInfo.ConvertTime(membership.Member.JoinedAt, clock.LocalTimeZone);
        if (kind == GuildDailyContributionKind.Attendance && GuildRedPacketDomainService.Day(joined) == day)
            return null;

        if (!await store.TryRecordDailyAsync(membership.Guild.Id, actor.CharacterId, kind, day,
                now.ToUnixTimeSeconds(), points, ct).ConfigureAwait(false))
            return null;

        var title = kind == GuildDailyContributionKind.Attendance ? "公会签到贡献奖励" : "公会聊天贡献奖励";
        return await DeliverAsync(actor.AccountId, actor.CharacterId, points, title,
            FormattableString.Invariant($"guild-daily:{actor.CharacterId.Value}:{day}:{(byte)kind}"), now, ct)
            .ConfigureAwait(false);
    }

    public async ValueTask<ItemDeliveryReceipt?> AwardClearAsync(AccountId account, CharacterId character, DungeonRunState run,
        DateTimeOffset now, CancellationToken ct)
    {
        if (rules.Read() is not { } config || !run.ClearOpen || run.RunId.Value == Guid.Empty || run.ClearedAt is not { } at
            || run.ClearParticipants.FirstOrDefault(member => member.CharacterId == character && member.AccountId == account) is not { } participant)
            return null;

        var (kind, rule) = run.Layout.Mode switch
        {
            DungeonRunMode.GuildDungeon => (GuildClearContributionKind.GuildDungeon, config.GuildDungeonContribution),
            { Kind: DungeonRunKind.Ordinary } when run.Layout.ConsumesFatigue =>
                (GuildClearContributionKind.PartyDungeon, config.PartyDungeonContribution),
            _ => (default, null),
        };
        if (rule is null || await guilds.FindMembershipAsync(character, ct).ConfigureAwait(false) is not { } membership)
            return null;

        var guild = membership.Guild.Id;
        var day = GuildRedPacketDomainService.Day(TimeZoneInfo.ConvertTime(at, clock.LocalTimeZone));
        int points;
        if (run.Layout.Mode is DungeonRunMode.GuildDungeon dungeon)
        {
            if (!dungeon.Includes(membership))
                return null;
            var priorClears = await store.CountClearsAsync(character, kind, day, ct).ConfigureAwait(false);
            points = GuildRedPacketRuleService.GuildDungeonClearContribution(rule, priorClears);
        }
        else
        {
            if (!participant.HasMembership(membership.Member))
                return null;
            var count = await CountGuildPartyMembersAsync(membership, run.ClearParticipants, ct).ConfigureAwait(false);
            if (GuildRedPacketRuleService.PartyClearContribution(rule, count) is not { } award)
                return null;
            points = award;
        }
        if (!await store.TryRecordClearAsync(guild, character, run.RunId.Value, kind, day, at.ToUnixTimeSeconds(), points, ct)
                .ConfigureAwait(false) || points == 0)
            return null;

        var title = kind == GuildClearContributionKind.GuildDungeon ? "公会地下城贡献奖励" : "会友组队通关贡献奖励";
        return await DeliverAsync(account, character, points, title,
            FormattableString.Invariant($"guild-clear:{run.RunId.Value:N}:{character.Value}"), now, ct).ConfigureAwait(false);
    }

    private async ValueTask<int> CountGuildPartyMembersAsync(GuildMembership actor,
        IReadOnlyList<DungeonClearParticipant> participants, CancellationToken ct)
    {
        var count = 1;
        foreach (var participant in participants)
        {
            if (participant.CharacterId != actor.Member.Character
                && await guilds.FindMembershipAsync(participant.CharacterId, ct).ConfigureAwait(false) is { } member
                && member.Guild.Id == actor.Guild.Id && participant.HasMembership(member.Member))
                count++;
        }
        return count;
    }

    private ValueTask<ItemDeliveryReceipt> DeliverAsync(AccountId account, CharacterId character, int points,
        string title, string key, DateTimeOffset now, CancellationToken ct) => delivery.DeliverAsync(account, character,
            [new(GuildRedPacketRules.ContributionCoin, points)], title, ct, ItemDeliveryMode.InventoryThenMail,
            new(null, key) { MailExpiresAt = now.AddDays(15) });

    public async ValueTask<IReadOnlyList<GuildContributionMember>> ReadLeaderboardAsync(CharacterId character, CancellationToken ct) =>
        await guilds.FindMembershipAsync(character, ct).ConfigureAwait(false) is { } membership
            ? await store.ReadLeaderboardAsync(membership.Guild.Id, ct).ConfigureAwait(false) : [];
}
