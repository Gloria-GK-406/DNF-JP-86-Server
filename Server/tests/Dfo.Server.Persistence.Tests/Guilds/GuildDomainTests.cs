using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Accounts;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Guilds;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Content;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Dfo.Server.Persistence.Accounts;
using Dfo.Server.Persistence.Characters;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Guilds;
using Dfo.Server.Persistence.Inventory;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Dfo.Server.Persistence.Tests.Guilds;

public sealed class GuildDomainTests : IDisposable
{
    private const int Fee = 300_000;
    private readonly string path = Path.Combine(Path.GetTempPath(), $"dfo-guild-{Guid.NewGuid():N}.db");
    private readonly DomainTransactionScope scope = TestRequired.TransactionScope();
    private readonly SqliteGuildDomainStore store;
    private readonly SqliteGuildPointAccount points;
    private readonly SqliteGuildWarehouseStore warehouse;
    private readonly SqliteCharacterTable characters;
    private readonly GuildMembershipDomainService guilds;
    private readonly GuildProfileDomainService profiles;
    private readonly GuildDonationDomainService donations;
    private readonly CharacterDomainService players;
    private readonly GuildBenefitDomainService benefits;
    private readonly GuildContentPurchaseDomainService purchases;
    private readonly GuildMembershipQueryDomainService query;
    private readonly GuildDiscoveryDomainService discovery;
    private readonly EconomyRules economy = new();
    private readonly GuildTestClock clock = new();

    public GuildDomainTests()
    {
        var database = new SqliteDatabase(path, scope);
        store = new SqliteGuildDomainStore(database);
        points = new(database);
        warehouse = new(database);
        characters = new(database);
        players = new CharacterDomainService(new SqliteCharacterCarriedGoldTable(database),
            TestRequired.Absent<ICarriedGoldRuleService>(), characters, TestRequired.Absent<ICharacterSkillPointDomainService>(),
            TestRequired.Absent<ICharacterStatRuleService>(), TestRequired.Absent<ICharacterSkillStateDomainService>(),
            TestRequired.Absent<IExperienceRuleService>(), TestRequired.Absent<IExperienceCapsuleDomainService>(),
            TestRequired.InitialHonor, scope, new DiscardingEventService());
        query = new(store);
        guilds = new(store, query, warehouse, new(economy), players, characters, TimeProvider.System);
        profiles = new(store, points, new(economy));
        donations = new(store, points, new(economy), players, TimeProvider.System);
        discovery = new(store);
        benefits = new(store, new(economy), clock);
        purchases = new(store, points, new(economy), benefits, new(warehouse, new(economy)));
        Execute("""
            INSERT INTO accounts(account_id,m_id) VALUES(1,'guild-test');
            INSERT INTO characters(character_id,account_id,name) VALUES
                (11,1,'Leader'),(12,1,'Other'),(13,1,'Applicant'),(14,1,'Member'),(15,1,'Pending'),(16,1,'Deleted');
            INSERT INTO character_stats(character_id,carried_gold) VALUES
                (11,900000),(12,900000),(13,100),(16,900000);
            UPDATE characters SET delete_flag=1 WHERE character_id=16;
            """);
    }

    [Fact]
    public async Task Medal_aura_is_shared_by_current_members_and_only_the_leader_can_change_it()
    {
        var created = await Write(() => guilds.CreateAsync(new(11), "Iris", "", default));
        var id = created.Guild!.Value;
        await Write(async () =>
        {
            await store.AddMemberAsync(id, new(14), false, DateTimeOffset.UtcNow, default);
            return true;
        });
        var style = new GuildMedalAura(2, 5);
        Assert.Equal(GuildMutationStatus.LeaderRequired, await Write(() => profiles.SetMedalAuraAsync(new(14), style, default)));
        Assert.Equal(GuildMutationStatus.InvalidMedalAura, await Write(() => profiles.SetMedalAuraAsync(new(11), new(25, 3), default)));
        Assert.Equal(GuildMutationStatus.InvalidMedalAura, await Write(() => profiles.SetMedalAuraAsync(new(11), new(-1, 3), default)));
        Assert.Equal(GuildMedalAura.None, (await query.FindMembershipAsync(new(14), default))!.Guild.MedalAura);
        Assert.Equal(GuildMutationStatus.Success, await Write(() => profiles.SetMedalAuraAsync(new(11), style, default)));
        Assert.Equal(style, (await query.FindMembershipAsync(new(14), default))!.Guild.MedalAura);
        Assert.Equal(GuildMutationStatus.Success, (await Write(() => guilds.LeaveAsync(new(14), default))).Status);
        Assert.Null(await query.FindMembershipAsync(new(14), default));
        Assert.Equal(style, (await query.FindMembershipAsync(new(11), default))!.Guild.MedalAura);
        Assert.Equal(GuildMutationStatus.Success, await Write(() => profiles.SetMedalAuraAsync(new(11), GuildMedalAura.None, default)));
        Assert.Equal(GuildMedalAura.None, (await query.FindMembershipAsync(new(11), default))!.Guild.MedalAura);
    }

    [Fact]
    public async Task Dungeon_extension_preserves_the_same_progress_for_one_reset_only()
    {
        var guild = (await Write(() => guilds.CreateAsync(new(11), "Iris", "", default))).Guild!.Value;
        var otherGuild = (await Write(() => guilds.CreateAsync(new(12), "Other", "", default))).Guild!.Value;
        var reset = new DateTimeOffset(2026, 12, 1, 6, 0, 0, TimeSpan.FromHours(8));
        var beforeReset = reset.AddSeconds(-1).ToUniversalTime();
        var calendar = new GuildDungeonPeriod(reset.AddMonths(-1).ToUnixTimeSeconds(), reset.ToUnixTimeSeconds());
        var extended = new GuildDungeonPeriod(calendar.StartUnix, reset.AddMonths(1).ToUnixTimeSeconds());
        var dungeons = new SqliteGuildDungeonStore(new(path, scope), points);
        var catalog = new GuildDungeonCatalog(0, new Dictionary<int, GuildDungeonDefinition>());
        var service = new GuildDungeonDomainService(new(TestRequired.Absent<IGuildDungeonContentSource>((_, _) => catalog)),
            dungeons, store, points, TestRequired.Absent<ISelectedSessionDomainService>(), TestRequired.Absent<IDungeonEntryIntentDomainService>(),
            TestRequired.Absent<IDungeonAdmissionDomainService>(), TestRequired.Absent<IDungeonVisitDomainService>(),
            TestRequired.Absent<IDungeonStructureRuleService>(), TestRequired.Absent<IItemDeliveryDomainService>(),
            new DiscardingEventService(), clock, players);
        Assert.Equal(calendar, await dungeons.ResolvePeriodAsync(guild, calendar, default));
        await Write(async () =>
        {
            Assert.True(await dungeons.TryRecordClearAsync(Guid.NewGuid(), new(11), new(guild, 0, calendar), 8500, 0, default));
            Assert.True(await points.TryCreditAsync(guild, 30, default));
            await store.AddMemberAsync(guild, new(14), false, beforeReset, default);
            return true;
        });

        Assert.False(await Write(() => service.TryExtendAsync(new(14), 10, beforeReset, default)));
        Assert.False(await Write(() => service.TryExtendAsync(new(11), 0, beforeReset, default)));
        Assert.False(await Write(() => service.TryExtendAsync(new(11), 31, beforeReset, default)));
        Assert.True(await Write(() => service.TryExtendAsync(new(11), 10, beforeReset, default)));
        Assert.False(await Write(() => service.TryExtendAsync(new(11), 10, beforeReset, default)));
        Assert.False(await Write(() => service.TryExtendAsync(new(11), 10, reset, default)));
        Assert.Equal(20L, Execute($"SELECT gm_points FROM guilds WHERE guild_id={guild.Value}"));
        clock.Now = reset;
        Assert.Equal(extended, (await service.ReadAsync(new(11), default))!.Admission.Period);

        var december = GuildDungeonPeriod.At(reset);
        Assert.Equal(extended, await dungeons.ResolvePeriodAsync(guild, calendar, default));
        Assert.Equal(extended, await dungeons.ResolvePeriodAsync(guild, december, default));
        Assert.Equal(december, await dungeons.ResolvePeriodAsync(otherGuild, december, default));
        var initialShop = new GuildDungeonAdmission(guild, 0, calendar).MedalShopId;
        Assert.Equal(initialShop, new GuildDungeonAdmission(guild, 0, extended).MedalShopId);
        Assert.Equal(1, (await dungeons.ReadProgressAsync(new(guild, 0, extended), default))[8500]);
        var january = GuildDungeonPeriod.At(reset.AddMonths(1));
        Assert.Equal(january, await dungeons.ResolvePeriodAsync(guild, january, default));
        Assert.NotEqual(initialShop, new GuildDungeonAdmission(guild, 0, january).MedalShopId);
        Assert.Empty(await dungeons.ReadProgressAsync(new(guild, 0, january), default));
        clock.Now = reset.AddMonths(1);
        Assert.Equal(january, (await service.ReadAsync(new(11), default))!.Admission.Period);
        Assert.True(await Write(() => service.TryExtendAsync(new(11), 10, clock.Now, default)));
    }

    [Fact]
    public async Task Monthly_contribution_uses_the_six_am_boundary_and_pays_each_qualifying_character_once()
    {
        var guild = (await Write(() => guilds.CreateAsync(new(11), "Iris", "", default))).Guild!.Value;
        await Write(() => guilds.ApplyAsync(new(13), guild, "", default));
        await Write(() => guilds.ApproveAsync(new(11), new(13), default));
        var boundary = new DateTimeOffset(2026, 12, 1, 6, 0, 0, TimeSpan.FromHours(8));
        var start = boundary.AddMonths(-1);
        Execute($"UPDATE guild_members SET joined_at={start.AddDays(-1).ToUnixTimeSeconds()}");
        var monthlyStore = new SqliteGuildMonthlyContributionStore(new(path, scope));
        economy.Monthly = new(10, 1100, 10153528, 1);
        var mail = new List<(CharacterId Recipient, ItemDeliveryGrant Grant, string Key)>();
        var delivery = TestRequired.Absent<IItemDeliveryDomainService>((method, args) =>
        {
            if (method.Name != nameof(IItemDeliveryDomainService.DeliverAsync))
                return null;
            Assert.Equal(ItemDeliveryMode.MailOnly, args![5]);
            mail.Add(((CharacterId)args[1]!, Assert.Single((IReadOnlyList<ItemDeliveryGrant>)args[2]!),
                ((ItemDeliveryOptions)args[6]!).MailDeliveryKey));
            return ValueTask.FromResult(new ItemDeliveryReceipt([], [1], 1));
        });
        var monthly = new GuildMonthlyContributionDomainService(store, monthlyStore, new(economy,
            TestRequired.Absent<IDungeonDefinitionSource>()), delivery, clock);

        clock.Now = boundary.AddSeconds(-1).ToUniversalTime();
        Assert.True(await Write(() => monthly.RecordAttendanceAsync(new(11), clock.Now, default)));
        Assert.False(await Write(() => monthly.RecordAttendanceAsync(new(11), clock.Now.ToOffset(boundary.Offset), default)));
        Assert.True(await Write(() => monthly.RecordAttendanceAsync(new(13), clock.Now, default)));
        var history = await monthly.ReadHistoryAsync(new(11), 10, default);
        Assert.NotNull(history);
        Assert.Equal(100, Assert.Single(history, entry => entry.Source == GuildContributionSource.LeaderAttendance).Points);
        Assert.Equal(10, Assert.Single(history, entry => entry.Source == GuildContributionSource.Attendance).Points);
        await Write(async () =>
        {
            await store.AddDonationAsync(guild, new(11), 989_000, 989, clock.Now, default);
            await store.AddDonationAsync(guild, new(13), 1_090_000, 1090, clock.Now, default);
            Assert.True(await monthlyStore.RecordAsync(guild, new(11), GuildContributionSource.RecommendedClear,
                "run", 10, clock.Now, default));
            Assert.False(await monthlyStore.RecordAsync(guild, new(11), GuildContributionSource.RecommendedClear,
                "run", 10, clock.Now, default));
            return true;
        });
        var november = await monthly.ReadTotalsAsync(guild, default);
        Assert.Equal(new(2199, 2199), november.Guild);
        Assert.Equal(new(1099, 1099), november.Members[new(11)]);
        Assert.Equal(new(1100, 1100), november.Members[new(13)]);

        clock.Now = boundary.ToUniversalTime();
        Assert.Equal(boundary, new GuildContributionMonth().LastOccurrence(clock.Now));
        Assert.Equal(boundary, new GuildContributionMonth().NextOccurrence(clock.Now.AddSeconds(-1)));
        Assert.True(await Write(() => monthly.RecordAttendanceAsync(new(11), clock.Now, default)));
        var december = await monthly.ReadTotalsAsync(guild, default);
        Assert.Equal(new(2299, 100), december.Guild);
        Assert.Equal(new(1199, 100), december.Members[new(11)]);
        Assert.Equal(new(1100, 0), december.Members[new(13)]);

        var first = await Write(() => monthly.SettleBatchAsync(clock.Now, null, 1, default));
        Assert.Equal(new CharacterId(11), first.NextAfter);
        Assert.Empty(mail);
        var second = await Write(() => monthly.SettleBatchAsync(boundary, first.NextAfter, 1, default));
        Assert.Equal(new CharacterId(13), second.NextAfter);
        Assert.Null((await Write(() => monthly.SettleBatchAsync(boundary, second.NextAfter, 1, default))).NextAfter);
        Assert.Null((await Write(() => monthly.SettleBatchAsync(boundary, null, 64, default))).NextAfter);
        var sent = Assert.Single(mail);
        Assert.Equal(new CharacterId(13), sent.Recipient);
        Assert.Equal(new ItemDeliveryGrant(10153528, 1), sent.Grant);
        Assert.Equal("guild-monthly-contribution:202611:13", sent.Key);
    }

    [Fact]
    public async Task Same_second_rejoin_keeps_guild_history_and_dedupe_keys_but_starts_a_new_personal_ledger()
    {
        var guild = (await Write(() => guilds.CreateAsync(new(11), "Iris", "", default))).Guild!.Value;
        var database = new SqliteDatabase(path, scope);
        var monthly = new SqliteGuildMonthlyContributionStore(database);
        var daily = new SqliteGuildContributionStore(database);
        var actor = new CharacterId(13);
        var at = clock.Now;
        var run = Guid.NewGuid();
        await Write(async () =>
        {
            await store.AddMemberAsync(guild, actor, false, at, default);
            await store.AddDonationAsync(guild, actor, 1000, 1, at, default);
            Assert.True(await monthly.RecordAsync(guild, actor, GuildContributionSource.Attendance, "day", 10, at, default));
            Assert.True(await daily.TryRecordDailyAsync(guild, actor, GuildDailyContributionKind.Chat, 1, at.ToUnixTimeSeconds(), 2, default));
            Assert.True(await daily.TryRecordClearAsync(guild, actor, run, GuildClearContributionKind.PartyDungeon,
                1, at.ToUnixTimeSeconds(), 4, default));
            return true;
        });
        var previous = (await query.FindMembershipAsync(actor, default))!.Member;
        Assert.True(previous.MembershipId > 0);
        Assert.Equal(new(11, 11), (await monthly.ReadTotalsAsync(guild, at, at.AddDays(1), default)).Members[actor]);
        Assert.Equal(11, Assert.Single(await monthly.ReadUnpaidAsync(at, at.AddDays(1), null, 10, default)).Points);
        Assert.Equal(6, (await daily.ReadLeaderboardAsync(guild, default)).Single(row => row.Character == actor).Contribution);

        await Write(async () =>
        {
            await store.RemoveMemberAsync(guild, actor, default);
            await store.AddMemberAsync(guild, actor, false, at, default);
            return true;
        });
        var current = (await query.FindMembershipAsync(actor, default))!.Member;
        Assert.Equal(previous.JoinedAt, current.JoinedAt);
        Assert.NotEqual(previous.MembershipId, current.MembershipId);
        Assert.Equal(current.MembershipId, (await query.ReadRosterAsync(actor, default)).Single(member => member.Character == actor).MembershipId);
        var totals = await monthly.ReadTotalsAsync(guild, at, at.AddDays(1), default);
        Assert.Equal(new(11, 11), totals.Guild);
        Assert.Equal(new(0, 0), totals.Members[actor]);
        Assert.Empty(await monthly.ReadUnpaidAsync(at, at.AddDays(1), null, 10, default));
        Assert.Equal(0, (await daily.ReadLeaderboardAsync(guild, default)).Single(row => row.Character == actor).Contribution);
        await Write(async () =>
        {
            Assert.False(await monthly.RecordAsync(guild, actor, GuildContributionSource.Attendance, "day", 10, at, default));
            Assert.False(await daily.TryRecordDailyAsync(guild, actor, GuildDailyContributionKind.Chat, 1, at.ToUnixTimeSeconds(), 2, default));
            Assert.False(await daily.TryRecordClearAsync(guild, actor, run, GuildClearContributionKind.PartyDungeon,
                1, at.ToUnixTimeSeconds(), 4, default));
            await store.AddDonationAsync(guild, actor, 2000, 2, at, default);
            return true;
        });
        totals = await monthly.ReadTotalsAsync(guild, at, at.AddDays(1), default);
        Assert.Equal(new(13, 13), totals.Guild);
        Assert.Equal(new(2, 2), totals.Members[actor]);
    }

    [Fact]
    public async Task Guild_gift_limits_each_direction_by_local_day_and_preserves_unclaimed_gifts()
    {
        clock.Now = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 11, 26, 23, 59, 59), clock.LocalTimeZone);
        var database = new SqliteDatabase(path, scope);
        var gifts = new GuildGiftDomainService(store, points, new SqliteGuildAllianceStore(database), new SqliteGuildGiftStore(database),
            benefits, new(economy), clock);
        var first = (await Write(() => guilds.CreateAsync(new(11), "Iris", "", default))).Guild!.Value;
        var second = (await Write(() => guilds.CreateAsync(new(12), "Violet", "", default))).Guild!.Value;
        Execute($"""
            INSERT INTO guild_alliances(first_id,second_id) VALUES({first.Value},{second.Value});
            UPDATE guilds SET gm_points=1000;
            """);

        Assert.Equal(GuildGiftStatus.UnsupportedContent, (await Write(() => gifts.SendAsync(new(11), second, 1, default))).Status);
        Assert.Equal(GuildGiftStatus.UnsupportedContent, (await Write(() => gifts.SendAsync(new(11), second, 99, default))).Status);
        Assert.Equal(1000L, Execute($"SELECT gm_points FROM guilds WHERE guild_id={first.Value}"));
        Assert.Empty((await gifts.ReadAsync(new(12), false, default))!.Rows);
        var sent = await Write(() => gifts.SendAsync(new(11), second, 3, default));
        Assert.Equal(GuildGiftStatus.Success, sent.Status);
        Assert.Equal(800L, Execute($"SELECT gm_points FROM guilds WHERE guild_id={first.Value}"));
        Assert.Equal(GuildGiftStatus.DailyLimit, (await Write(() => gifts.SendAsync(new(11), second, 2, default))).Status);
        Assert.Equal(800L, Execute($"SELECT gm_points FROM guilds WHERE guild_id={first.Value}"));
        Assert.Empty((await gifts.ReadAsync(new(11), true, default))!.Rows);
        Assert.Equal(GuildGiftStatus.Success, (await Write(() => gifts.SendAsync(new(12), first, 2, default))).Status);
        Assert.Equal(second, Assert.Single((await gifts.ReadAsync(new(11), true, default))!.Rows).Sender);
        Assert.Equal(first, Assert.Single((await gifts.ReadAsync(new(12), true, default))!.Rows).Sender);
        clock.Now = clock.Now.AddSeconds(1);
        var nextGift = await Write(() => gifts.SendAsync(new(11), second, 3, default));
        Assert.Equal(GuildGiftStatus.Success, nextGift.Status);

        Execute("DELETE FROM guild_alliances");
        var key = new GuildGiftKey(first, 3, (byte)GuildContentKind.Experience, checked((uint)sent.Gift!.SentAt));
        Assert.Equal(GuildGiftStatus.Success, (await Write(() => gifts.ClaimAsync(new(12), key, default))).Status);
        Assert.Equal(GuildGiftStatus.MissingGift, (await Write(() => gifts.ClaimAsync(new(12), key, default))).Status);
        Assert.Equal(800L, Execute($"SELECT gm_points FROM guilds WHERE guild_id={second.Value}"));
        Assert.Single((await gifts.ReadAsync(new(12), false, default))!.Rows);
        Assert.Equal(2, (await gifts.ReadAsync(new(12), true, default))!.Rows.Count);
        Assert.Equal(clock.Now.AddDays(1).ToUnixTimeSeconds(),
            Assert.Single(await benefits.ReadContentsAsync(second, default)).ExpiresAtUnixSeconds);

        var nextKey = key with { SentAt = checked((uint)nextGift.Gift!.SentAt) };
        clock.Now = DateTimeOffset.FromUnixTimeSeconds(int.MaxValue).AddDays(-1);
        Assert.Equal(GuildGiftStatus.ActivationUnavailable, (await Write(() => gifts.ClaimAsync(new(12), nextKey, default))).Status);
        Assert.Single((await gifts.ReadAsync(new(12), false, default))!.Rows);
        Assert.Empty(await benefits.ReadContentsAsync(second, default));
    }

    [Fact]
    public async Task A_member_can_change_and_clear_only_their_own_roster_memo()
    {
        var guild = (await Write(() => guilds.CreateAsync(new(11), "Iris", "", default))).Guild!.Value;
        await Write(() => guilds.ApplyAsync(new(13), guild, "", default));
        await Write(() => guilds.ApproveAsync(new(11), new(13), default));

        Assert.Equal(GuildMutationStatus.Success,
            await Write(() => guilds.SetMemberMemoAsync(new(13), "周末组队", default)));
        Assert.Equal(GuildMutationStatus.NotMember,
            await Write(() => guilds.SetMemberMemoAsync(new(12), "Not a member", default)));
        Assert.Equal(GuildMutationStatus.InvalidText,
            await Write(() => guilds.SetMemberMemoAsync(new(13), new string('测', 21), default)));

        var roster = await query.ReadRosterEntriesAsync(new(11), default);
        Assert.Equal("", roster.Single(row => row.Membership.Character == new CharacterId(11)).Membership.Memo);
        Assert.Equal("周末组队", roster.Single(row => row.Membership.Character == new CharacterId(13)).Membership.Memo);

        Assert.Equal(GuildMutationStatus.Success,
            await Write(() => guilds.SetMemberMemoAsync(new(13), "", default)));
        Assert.Equal("", (await query.FindMembershipAsync(new(13), default))!.Member.Memo);
    }

    [Fact]
    public async Task Leadership_transfer_requires_the_current_leader_and_commits_both_ranks()
    {
        var guild = (await Write(() => guilds.CreateAsync(new(11), "Iris", "", default))).Guild!.Value;
        await Write(() => guilds.CreateAsync(new(12), "Other Guild", "", default));
        await Write(() => guilds.ApplyAsync(new(13), guild, "", default));
        await Write(() => guilds.ApproveAsync(new(11), new(13), default));
        await Write(() => guilds.AssignRankAsync(new(11), "Applicant"u8.ToArray(), GuildRankSettings.ViceLeader, default));
        var target = "Applicant"u8.ToArray();

        Assert.Null(await Write(() => guilds.TransferLeadershipAsync(new(13), "Leader"u8.ToArray(), default)));
        Assert.Null(await Write(() => guilds.TransferLeadershipAsync(new(11), "Other"u8.ToArray(), default)));
        Assert.Null(await Write(() => guilds.TransferLeadershipAsync(new(11), "Leader"u8.ToArray(), default)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Write(async () =>
        {
            await store.TransferLeadershipAsync(guild, new(11), new(15), default);
            return true;
        }));
        Assert.Equal(new CharacterId(11), Assert.Single(await query.ReadRosterAsync(new(11), default),
            member => member.IsLeader).Character);

        Assert.Equal(new CharacterId(13), await Write(() => guilds.TransferLeadershipAsync(new(11), target, default)));
        Assert.Null(await Write(() => guilds.TransferLeadershipAsync(new(11), target, default)));
        var roster = await query.ReadRosterAsync(new(11), default);
        Assert.Equal(new CharacterId(13), Assert.Single(roster, member => member.IsLeader).Character);
        Assert.Equal(GuildRankSettings.Member, roster.Single(member => member.Character == new CharacterId(11)).Rank);
        Assert.Equal(GuildRankSettings.Leader, (await query.FindMembershipAsync(new(13), default))!.Member.Rank);
    }

    [Fact]
    public async Task Content_purchase_preserves_contributions_and_counts_teleport_uses()
    {
        var created = await Write(() => guilds.CreateAsync(new(11), "Iris", "", default));
        var guild = created.Guild!.Value;
        await Write(() => guilds.ApplyAsync(new(13), guild, "", default));
        await Write(() => guilds.ApproveAsync(new(11), new(13), default));
        Execute("UPDATE guilds SET gm_points=98000,spent_gm_points=2000;");
        Assert.Equal(GuildMutationStatus.LeaderRequired,
            (await Write(() => purchases.PurchaseAsync(new(13), 13, default))).Status);
        Assert.Equal(GuildMutationStatus.UnsupportedContent,
            (await Write(() => purchases.PurchaseAsync(new(11), 4, default))).Status);
        Assert.Equal(GuildMutationStatus.Success,
            (await Write(() => purchases.PurchaseAsync(new(11), 1, default))).Status);
        Assert.Equal(GuildMutationStatus.ContentAlreadyActive,
            (await Write(() => purchases.PurchaseAsync(new(11), 1, default))).Status);
        Assert.Equal(GuildMutationStatus.Success,
            (await Write(() => purchases.PurchaseAsync(new(11), 13, default))).Status);
        var balance = (await query.FindMembershipAsync(new(13), default))!.Guild;
        Assert.Equal(61_700, balance.GmPoints);
        Assert.Equal(100_000, balance.ContributionPoints);
        Assert.Equal(169, Assert.Single(await benefits.ReadAttributeBonusesAsync(guild, default)).BuffId);
        Assert.Equal(10, (await benefits.ReadContentsAsync(guild, default)).Single(x => x.Kind == GuildContentKind.PartyTeleport).RemainingUses);

        Assert.Equal(GuildMutationStatus.Success, (await Write(() => benefits.ConsumeTeleportAsync(new(13), default))).Status);
        Assert.Equal(9, (await benefits.ReadContentsAsync(guild, default)).Single(x => x.Kind == GuildContentKind.PartyTeleport).RemainingUses);
        for (var i = 0; i < 9; i++)
            Assert.Equal(GuildMutationStatus.Success, (await Write(() => benefits.ConsumeTeleportAsync(new(11), default))).Status);
        Assert.Equal(GuildMutationStatus.UnsupportedContent, (await Write(() => benefits.ConsumeTeleportAsync(new(11), default))).Status);
        Assert.Single(await benefits.ReadContentsAsync(guild, default));

        await Write(() => guilds.RemoveMemberAsync(new(11), new(13), default));
        Assert.Null(await query.FindMembershipAsync(new(13), default));
        Assert.Equal(GuildMutationStatus.NotMember,
            (await Write(() => benefits.ConsumeTeleportAsync(new(13), default))).Status);
    }

    [Fact]
    public async Task Purchase_checks_the_benefit_before_funds_and_keeps_a_rejected_purchase_atomic()
    {
        var guild = (await Write(() => guilds.CreateAsync(new(11), "Iris", "", default))).Guild!.Value;
        Assert.Equal(GuildMutationStatus.InsufficientGuildPoints,
            (await Write(() => purchases.PurchaseAsync(new(11), 2, default))).Status);
        Assert.Empty(await benefits.ReadContentsAsync(guild, default));

        clock.Now = DateTimeOffset.FromUnixTimeSeconds(int.MaxValue - 60);
        Assert.Equal(GuildMutationStatus.ContentDurationLimit,
            (await Write(() => purchases.PurchaseAsync(new(11), 2, default))).Status);

        Execute("UPDATE guilds SET gm_points=500");
        Execute("""
            CREATE TRIGGER reject_guild_content BEFORE INSERT ON guild_contents
            BEGIN SELECT RAISE(ABORT, 'reject grant'); END
            """);
        await Assert.ThrowsAsync<SqliteException>(() => Write(() => purchases.PurchaseAsync(new(11), 13, default)));
        Assert.Equal(500L, Execute("SELECT gm_points FROM guilds"));
        Assert.Equal(0L, Execute("SELECT spent_gm_points FROM guilds"));
        Assert.Empty(await benefits.ReadContentsAsync(guild, default));
    }

    [Fact]
    public async Task Timed_attributes_renew_without_adding_another_buff_and_expire_independently_of_experience()
    {
        var guild = (await Write(() => guilds.CreateAsync(new(11), "Iris", "", default))).Guild!.Value;
        Execute("UPDATE guilds SET gm_points=98000;");
        foreach (uint product in new uint[] { 1, 2, 2, 3 })
            Assert.Equal(GuildMutationStatus.Success,
                (await Write(() => purchases.PurchaseAsync(new(11), product, default))).Status);

        Assert.Equal(new[] { 167, 169 }, (await benefits.ReadAttributeBonusesAsync(guild, default)).Select(bonus => bonus.BuffId));
        Assert.Equal(0.1, await benefits.ReadMonsterExperienceRateAsync(new(11), default));
        Assert.Equal(0, await benefits.ReadMonsterExperienceRateAsync(new(13), default));
        Assert.Equal(61_400, (await query.FindMembershipAsync(new(11), default))!.Guild.GmPoints);
        clock.Now = clock.Now.AddDays(1);
        Assert.Equal(0, await benefits.ReadMonsterExperienceRateAsync(new(11), default));
        Assert.Equal(2, (await benefits.ReadAttributeBonusesAsync(guild, default)).Count);
        clock.Now = clock.Now.AddDays(1);
        var removed = await Write(() => benefits.RemoveExpiredAsync(clock.Now.ToUnixTimeSeconds(), 64, default));
        Assert.Equal(2, removed.Count);
        Assert.Equal(169, Assert.Single(await benefits.ReadAttributeBonusesAsync(guild, default)).BuffId);
        Assert.Empty(await Write(() => benefits.RemoveExpiredAsync(clock.Now.ToUnixTimeSeconds(), 64, default)));
    }

    [Fact]
    public async Task Warehouse_expansions_use_shared_capacity_and_refuse_overflow_without_charging()
    {
        var guild = (await Write(() => guilds.CreateAsync(new(11), "Iris", "", default))).Guild!.Value;
        Execute("UPDATE guilds SET gm_points=98000;");
        Assert.Equal(0, await warehouse.ReadWarehouseCapacityAsync(guild, default));
        foreach (uint product in new uint[] { 12, 11, 11, 11 })
            Assert.Equal(GuildMutationStatus.Success,
                (await Write(() => purchases.PurchaseAsync(new(11), product, default))).Status);

        Assert.Equal(56, await warehouse.ReadWarehouseCapacityAsync(guild, default));
        Assert.Equal(GuildMutationStatus.WarehouseCapacityLimit,
            (await Write(() => purchases.PurchaseAsync(new(11), 12, default))).Status);
        Assert.Equal(63_000, (await query.FindMembershipAsync(new(11), default))!.Guild.GmPoints);
        Assert.Empty(await benefits.ReadContentsAsync(guild, default));
    }

    [Fact]
    public async Task Mark_change_preserves_validation_order_and_balance_when_pricing_is_missing()
    {
        var guild = (await Write(() => guilds.CreateAsync(new(11), "Iris", "", default))).Guild!.Value;
        Execute("UPDATE guilds SET gm_points=2100");
        Assert.Equal(GuildMutationStatus.Success, await Write(() => profiles.SetMarkAsync(new(11), 0, default)));
        economy.MarkPricing = null;

        Assert.Equal(GuildMutationStatus.InvalidMark, await Write(() => profiles.SetMarkAsync(new(13), 16, default)));
        Assert.Equal(GuildMutationStatus.LeaderRequired, await Write(() => profiles.SetMarkAsync(new(13), 15, default)));
        Assert.Equal(GuildMutationStatus.Success, await Write(() => profiles.SetMarkAsync(new(11), 0, default)));
        Assert.Equal(GuildMutationStatus.MarkPricingUnavailable, await Write(() => profiles.SetMarkAsync(new(11), 15, default)));
        var state = (await query.FindMembershipAsync(new(11), default))!.Guild;
        Assert.Equal(guild, state.Id);
        Assert.Equal(0, state.Mark);
        Assert.Equal(2100, state.GmPoints);
    }

    [Fact]
    public async Task Leader_edits_announcement_and_changes_marks_without_losing_contribution()
    {
        var created = await Write(() => guilds.CreateAsync(new(11), "Iris", "Promotion", default));
        var guildId = created.Guild!.Value;
        await Write(() => guilds.ApplyAsync(new(13), guildId, "", default));
        await Write(() => guilds.ApproveAsync(new(11), new(13), default));
        Execute("UPDATE guilds SET gm_points=2100;");

        Assert.Equal(GuildMutationStatus.LeaderRequired,
            await Write(() => profiles.SetAnnouncementAsync(new(13), "Other", default)));
        Assert.Equal(GuildMutationStatus.LeaderRequired,
            await Write(() => profiles.SetMarkAsync(new(13), 5, default)));
        Assert.Equal(GuildMutationStatus.InvalidText,
            await Write(() => profiles.SetAnnouncementAsync(new(11), new string('测', 41), default)));
        Assert.Equal(GuildMutationStatus.Success,
            await Write(() => profiles.SetAnnouncementAsync(new(11), "公会公告", default)));
        Assert.Equal(GuildMutationStatus.InvalidMark, await Write(() => profiles.SetMarkAsync(new(11), 16, default)));
        Assert.Equal(GuildMutationStatus.Success, await Write(() => profiles.SetMarkAsync(new(11), 0, default)));
        Assert.Equal(2100, (await query.FindMembershipAsync(new(13), default))!.Guild.GmPoints);
        Assert.Equal(GuildMutationStatus.Success, await Write(() => profiles.SetMarkAsync(new(11), 5, default)));
        Assert.Equal(GuildMutationStatus.Success, await Write(() => profiles.SetMarkAsync(new(11), 5, default)));
        Assert.Equal(GuildMutationStatus.InsufficientGuildPoints,
            await Write(() => profiles.SetMarkAsync(new(11), 6, default)));

        var guild = (await query.FindMembershipAsync(new(13), default))!.Guild;
        Assert.Equal("公会公告", guild.Announcement);
        Assert.Equal("Promotion", guild.Promotion);
        Assert.Equal(5, guild.Mark);
        Assert.Equal(100, guild.GmPoints);
        Assert.Equal(2100, guild.ContributionPoints);
        Assert.Equal(5, (await query.FindPublicIdentityAsync(new(13), default))!.Mark);
    }

    [Fact]
    public async Task Application_review_uses_the_rank_permission_and_scopes_cancel_and_reject()
    {
        var guild = (await Write(() => guilds.CreateAsync(new(11), "Iris", "", default))).Guild!.Value;
        var other = (await Write(() => guilds.CreateAsync(new(12), "Violet", "", default))).Guild!.Value;
        await Write(() => guilds.ApplyAsync(new(13), guild, "", default));
        await Write(() => guilds.ApproveAsync(new(11), new(13), default));
        await Write(() => guilds.AssignRankAsync(new(11), "Applicant"u8.ToArray(), GuildRankSettings.ViceLeader, default));
        await Write(() => guilds.ApplyAsync(new(15), guild, "Join", default));

        Assert.Null(await query.ReadApplicationEntriesAsync(new(13), default));
        Assert.Equal(GuildMutationStatus.LeaderRequired,
            (await Write(() => guilds.RejectApplicationAsync(new(13), new(15), default))).Status);
        Assert.Equal(GuildMutationStatus.ApplicationNotFound,
            (await Write(() => guilds.RejectApplicationAsync(new(12), new(15), default))).Status);
        Assert.Equal(GuildMutationStatus.ApplicationNotFound,
            (await Write(() => guilds.CancelApplicationAsync(new(15), other, default))).Status);
        Assert.Equal("Iris", (await query.FindPendingApplicationAsync(new(15), default))!.GuildName);

        await Write(() => guilds.SetRankPermissionsAsync(new(11), GuildRankSettings.ViceLeader,
            GuildRankSettings.ReviewApplicationsPermission, default));
        var entry = Assert.Single((await query.ReadApplicationEntriesAsync(new(13), default))!);
        Assert.Equal("Pending"u8.ToArray(), entry.Name);
        Assert.Equal("Join", entry.Application.Message);
        Assert.Equal(GuildMutationStatus.Success,
            (await Write(() => guilds.RejectApplicationAsync(new(13), new(15), default))).Status);
        Assert.Null(await query.FindMembershipAsync(new(15), default));
        Assert.Null(await query.FindPendingApplicationAsync(new(15), default));

        Assert.Equal(GuildMutationStatus.Success, (await Write(() => guilds.ApplyAsync(new(15), guild, "", default))).Status);
        Assert.Equal(GuildMutationStatus.Success,
            (await Write(() => guilds.CancelApplicationAsync(new(15), guild, default))).Status);
        Assert.Empty((await query.ReadApplicationEntriesAsync(new(13), default))!);

        await Write(() => guilds.ApplyAsync(new(15), guild, "", default));
        Assert.Equal(GuildMutationStatus.Success,
            (await Write(() => guilds.ApproveAsync(new(13), new(15), default))).Status);
        Assert.Null(await query.FindPendingApplicationAsync(new(15), default));
        Assert.Equal(GuildRankSettings.Member, (await query.FindMembershipAsync(new(15), default))!.Member.Rank);
    }

    [Fact]
    public async Task Guild_attendance_is_per_guild_character_and_local_date()
    {
        var first = (await Write(() => guilds.CreateAsync(new(11), "Iris", "", default))).Guild!.Value;
        await Write(() => guilds.CreateAsync(new(12), "Violet", "", default));
        await Write(() => guilds.ApplyAsync(new(13), first, "", default));
        await Write(() => guilds.ApproveAsync(new(11), new(13), default));
        var clock = new AttendanceClock();
        var attendance = new GuildAttendanceDomainService(store, clock);

        Assert.Empty(await attendance.ReadAttendanceTodayAsync(new(11), default));
        Assert.False(await Write(() => attendance.RecordAttendanceAsync(new(14), clock.Now, default)));
        Assert.True(await Write(() => attendance.RecordAttendanceAsync(new(11), clock.Now, default)));
        Assert.False(await Write(() => attendance.RecordAttendanceAsync(new(11), clock.Now, default)));
        Assert.True(await Write(() => attendance.RecordAttendanceAsync(new(13), clock.Now, default)));
        Assert.True(await Write(() => attendance.RecordAttendanceAsync(new(12), clock.Now, default)));
        Assert.Collection(await attendance.ReadAttendanceTodayAsync(new(11), default),
            name => Assert.Equal("Leader"u8.ToArray(), name),
            name => Assert.Equal("Applicant"u8.ToArray(), name));
        Assert.Equal("Other"u8.ToArray(), Assert.Single(await attendance.ReadAttendanceTodayAsync(new(12), default)));
        Assert.Empty(await attendance.ReadAttendanceTodayAsync(new(14), default));
        Assert.Equal(2, await attendance.CountWeeklyVisitorsAsync(first, default));
        await Write(() => guilds.LeaveAsync(new(13), default));
        Assert.Equal("Leader"u8.ToArray(), Assert.Single(await attendance.ReadAttendanceTodayAsync(new(11), default)));

        clock.Now = clock.Now.AddMinutes(2);
        Assert.Empty(await attendance.ReadAttendanceTodayAsync(new(11), default));
        Assert.True(await Write(() => attendance.RecordAttendanceAsync(new(11), clock.Now, default)));
        Assert.Equal(4L, Execute("SELECT COUNT(*) FROM guild_attendance"));
        Assert.Equal(2L, Execute("SELECT COUNT(DISTINCT day) FROM guild_attendance"));
        Assert.Equal(1, await attendance.CountWeeklyVisitorsAsync(first, default));
        clock.Now = clock.Now.AddDays(6);
        Assert.Equal(1, await attendance.CountWeeklyVisitorsAsync(first, default));
        clock.Now = clock.Now.AddDays(1);
        Assert.Equal(0, await attendance.CountWeeklyVisitorsAsync(first, default));
    }

    [Fact]
    public async Task Guild_board_validates_display_width_before_recording_a_post()
    {
        await Write(() => guilds.CreateAsync(new(11), "Iris", "", default));
        var board = new GuildBoardDomainService(store, clock);
        var text = new string('a', 116) + "会😀";
        var accepted = await Write(() => board.WriteAsync(new(11), text, default));
        Assert.True(accepted.Written);
        Assert.Equal(text, Assert.Single(accepted.Snapshot!.Posts).Text);
        foreach (var invalid in new[] { text + "a", "", "  ", "a\nb" })
        {
            var rejected = await Write(() => board.WriteAsync(new(11), invalid, default));
            Assert.False(rejected.Written);
            Assert.Equal(text, Assert.Single(rejected.Snapshot!.Posts).Text);
        }
    }

    [Fact]
    public async Task Guild_creation_lookup_and_edits_preserve_their_text_limits()
    {
        var promotion = new string('会', 40);
        var created = await Write(() => guilds.CreateAsync(new(11), "Café", promotion, default));
        Assert.Equal(GuildMutationStatus.Success, created.Status);
        Assert.NotNull(await discovery.FindGuildAsync("cafe\u0301", default));
        Assert.Null(await discovery.FindGuildAsync(" Café", default));

        Assert.Equal(GuildMutationStatus.InvalidText, await Write(() => profiles.SetPromotionAsync(new(11), promotion, default)));
        Assert.Equal(promotion, (await query.FindMembershipAsync(new(11), default))!.Guild.Promotion);
        var eightyBytes = new string('会', 26) + "ab";
        Assert.Equal(GuildMutationStatus.Success, await Write(() => profiles.SetPromotionAsync(new(11), eightyBytes, default)));
        Assert.Equal(GuildMutationStatus.InvalidText, await Write(() => profiles.SetPromotionAsync(new(11), eightyBytes + "c", default)));
        Assert.Equal(GuildMutationStatus.InvalidText, (await Write(() => guilds.ApplyAsync(new(13), created.Guild!.Value, "\t", default))).Status);
        Assert.Equal(GuildMutationStatus.InvalidRankSetting, await Write(() => guilds.SetRankNameAsync(new(11), 4, "  ", default)));
    }

    [Fact]
    public async Task Guild_management_edits_are_leader_only_and_preserve_both_rank_fields()
    {
        var guild = (await Write(() => guilds.CreateAsync(new(11), "Iris", "Original", default))).Guild!.Value;
        var another = (await Write(() => guilds.CreateAsync(new(12), "Violet", "Another", default))).Guild!.Value;
        await Write(() => guilds.ApplyAsync(new(13), guild, "", default));
        await Write(() => guilds.ApproveAsync(new(11), new(13), default));

        var defaults = await query.ReadRankSettingsAsync(guild, default);
        Assert.Equal(6, defaults.Count);
        Assert.Equal(GuildRankSettings.LeaderDefaultPermissions, defaults[1].PermissionMask);
        Assert.Equal("普通成员", defaults[4].Name);
        Assert.Equal(GuildMutationStatus.LeaderRequired,
            await Write(() => guilds.SetRankPermissionsAsync(new(13), 4, 0xFFFF, default)));
        Assert.Equal(GuildMutationStatus.LeaderRequired,
            await Write(() => guilds.SetRankNameAsync(new(13), 4, "forbidden", default)));
        Assert.Equal(GuildMutationStatus.LeaderRequired,
            await Write(() => profiles.SetPromotionAsync(new(13), "forbidden", default)));
        Assert.Equal(GuildMutationStatus.InvalidRankSetting,
            await Write(() => guilds.SetRankPermissionsAsync(new(11), 4, 0x10000, default)));
        Assert.Equal(GuildMutationStatus.InvalidRankSetting,
            await Write(() => guilds.SetRankNameAsync(new(11), 4, new string('会', 8), default)));

        Assert.Equal(GuildMutationStatus.Success,
            await Write(() => guilds.SetRankNameAsync(new(11), 4, "考察成员", default)));
        Assert.Equal(GuildMutationStatus.Success,
            await Write(() => guilds.SetRankPermissionsAsync(new(11), 4, 0x40, default)));
        Assert.Equal(GuildMutationStatus.Success,
            await Write(() => guilds.SetRankPermissionsAsync(new(11), 1, 0, default)));
        Assert.Equal(GuildMutationStatus.Success,
            await Write(() => profiles.SetPromotionAsync(new(11), "本会宣传", default)));

        var settings = await query.ReadRankSettingsAsync(guild, default);
        Assert.Equal(new GuildRankSetting(4, 0x40, "考察成员"), settings[4]);
        Assert.Equal(GuildRankSettings.LeaderDefaultPermissions, settings[1].PermissionMask);
        Assert.Equal("本会宣传", (await query.FindMembershipAsync(new(13), default))!.Guild.Promotion);
        Assert.Equal("Another", (await query.FindMembershipAsync(new(12), default))!.Guild.Promotion);
        Assert.Equal(defaults[4], (await query.ReadRankSettingsAsync(another, default))[4]);
    }

    [Fact]
    public async Task Guild_appointment_requires_a_leader_or_a_delegated_vice_leader()
    {
        var guild = (await Write(() => guilds.CreateAsync(new(11), "Iris", "", default))).Guild!.Value;
        await Write(() => guilds.CreateAsync(new(12), "Outside", "", default));
        foreach (var member in new[] { new CharacterId(13), new CharacterId(14) })
        {
            await Write(() => guilds.ApplyAsync(member, guild, "", default));
            await Write(() => guilds.ApproveAsync(new(11), member, default));
        }

        var promoted = await Write(() => guilds.AssignRankAsync(new(11), "Applicant"u8.ToArray(), 2, default));
        Assert.Equal(new GuildRankAssignmentResult(new(13), 2, true), promoted);
        Assert.Equal(2, (await query.FindMembershipAsync(new(13), default))!.Member.Rank);
        Assert.Null(await Write(() => guilds.AssignRankAsync(new(11), "Other"u8.ToArray(), 2, default)));
        Assert.Null(await Write(() => guilds.AssignRankAsync(new(11), "Member"u8.ToArray(), 1, default)));
        Assert.Null(await Write(() => guilds.AssignRankAsync(new(14), "Applicant"u8.ToArray(), 4, default)));
        Assert.Null(await Write(() => guilds.AssignRankAsync(new(13), "Member"u8.ToArray(), 3, default)));

        Assert.Equal(GuildMutationStatus.Success, await Write(() => guilds.SetRankPermissionsAsync(
            new(11), 2, GuildRankSettings.AppointMembersPermission, default)));
        Assert.Null(await Write(() => guilds.AssignRankAsync(new(13), "Member"u8.ToArray(), 2, default)));
        Assert.Null(await Write(() => guilds.AssignRankAsync(new(13), "Applicant"u8.ToArray(), 4, default)));
        Assert.Null(await Write(() => guilds.AssignRankAsync(new(13), "Leader"u8.ToArray(), 4, default)));
        Assert.Equal(new GuildRankAssignmentResult(new(14), 3, true),
            await Write(() => guilds.AssignRankAsync(new(13), "Member"u8.ToArray(), 3, default)));
        Assert.Equal(new GuildRankAssignmentResult(new(14), 3, false),
            await Write(() => guilds.AssignRankAsync(new(13), "Member"u8.ToArray(), 3, default)));
        var roster = await query.ReadRosterEntriesAsync(new(11), default);
        Assert.Equal(new byte[] { 1, 2, 3 }, roster.Select(row => row.Membership.Rank).ToArray());
    }

    [Fact]
    public async Task Guild_discovery_reads_public_details_and_private_account_memberships()
    {
        Execute("""
            INSERT INTO accounts(account_id,m_id) VALUES(2,'guild-other-account');
            UPDATE characters SET account_id=2 WHERE character_id=12;
            """);
        var first = (await Write(() => guilds.CreateAsync(new(11), "Iris", "First", default))).Guild!.Value;
        var second = (await Write(() => guilds.CreateAsync(new(12), "Violet", "Second", default))).Guild!.Value;
        await Write(() => guilds.ApplyAsync(new(13), second, "", default));
        await Write(() => guilds.ApproveAsync(new(12), new(13), default));

        Assert.Equal(new GuildDiscoveryDetails(second, "Violet", 2, "Second"), await discovery.FindGuildAsync("vIOLET", default));
        Assert.Null(await discovery.FindGuildAsync("Vio", default));
        Assert.Null(await query.FindMembershipAsync(new(14), default));
        Assert.Collection(await discovery.ReadAccountGuildsAsync(new(1), default),
            row => { Assert.Equal("Leader"u8.ToArray(), row.CharacterName); Assert.Equal("Iris", row.GuildName); },
            row => { Assert.Equal("Applicant"u8.ToArray(), row.CharacterName); Assert.Equal("Violet", row.GuildName); });
        var other = Assert.Single(await discovery.ReadAccountGuildsAsync(new(2), default));
        Assert.Equal("Other"u8.ToArray(), other.CharacterName);
        Assert.Equal("Violet", other.GuildName);
        Assert.Equal(new[] { second, first }, (await discovery.ReadRecommendationsAsync(new(14), default)).Select(row => row.Guild));
        Assert.Equal(second, Assert.Single(await discovery.ReadRecommendationsAsync(new(11), default)).Guild);
        Assert.Equal(first, Assert.Single(await discovery.ReadRecommendationsAsync(new(13), default)).Guild);

        Execute($"INSERT INTO guild_alliances(first_id,second_id) VALUES({first.Value},{second.Value})");
        Assert.Empty(await discovery.ReadRecommendationsAsync(new(11), default));
        Assert.Empty(await discovery.ReadRecommendationsAsync(new(12), default));
        Assert.Empty(await discovery.ReadRecommendationsAsync(new(13), default));
        Assert.Equal(new[] { second, first }, (await discovery.ReadRecommendationsAsync(new(14), default)).Select(row => row.Guild));
    }

    private sealed class AttendanceClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 26, 15, 59, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone(
            "Guild attendance tests", TimeSpan.FromHours(8), "UTC+8", "UTC+8");
    }

    [Fact]
    public async Task Creation_permit_is_bound_to_the_exact_selected_session_and_cleared_by_cancel()
    {
        var recruitment = new GuildNameOperationSessionDomainService(guilds,
            TestRequired.Absent<IGuildRenameDomainService>());
        var original = SelectedSessionIdentity.Create(new(1), new(11), new("connection"), 1, 10011);
        var replacement = SelectedSessionIdentity.Create(new(1), new(11), new("connection"), 2, 10011);
        await TestGameplay.RunAsync(async () =>
        {
            Assert.Equal(GuildMutationStatus.Success, await recruitment.StartAsync(original, "Iris", default));
            Assert.False(recruitment.Authorize(original));
            Assert.True(recruitment.SetPromotion(original, ""));
            Assert.True(recruitment.Authorize(original));
            Assert.NotNull(recruitment.AuthorizedCreation(original));
            Assert.Null(recruitment.AuthorizedCreation(replacement));
            recruitment.Cancel(original);
            Assert.Null(recruitment.AuthorizedCreation(original));
        });
    }

    [Fact]
    public async Task Cancelled_in_flight_name_check_cannot_restore_the_creation_draft()
    {
        var pending = new TaskCompletionSource<GuildMutationStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        var membership = TestRequired.Absent<IGuildMembershipDomainService>((method, _) =>
            method.Name == nameof(IGuildMembershipDomainService.CheckCreationAsync) ? new ValueTask<GuildMutationStatus>(pending.Task) : null);
        var operations = new GuildNameOperationSessionDomainService(membership, TestRequired.Absent<IGuildRenameDomainService>());
        var session = SelectedSessionIdentity.Create(new(1), new(11), new("connection"), 1, 10011);
        await TestGameplay.RunAsync(async () =>
        {
            var checking = operations.StartAsync(session, "Iris", default);
            operations.Cancel(session);
            pending.SetResult(GuildMutationStatus.Success);
            Assert.Equal(GuildMutationStatus.Success, await checking);
            Assert.False(operations.SetPromotion(session, "old draft"));
            Assert.False(operations.Authorize(session));
            Assert.Null(operations.AuthorizedCreation(session));
        });
    }

    [Fact]
    public async Task Name_rechecks_replace_creation_and_rename_permissions_for_one_selection()
    {
        var rename = new GuildRenameDomainService(store, new(economy), TestRequired.Absent<IBusinessInventorySlotDomainService>(),
            TestRequired.Absent<IItemLifecycleDomainService>(), players, clock);
        var operations = new GuildNameOperationSessionDomainService(guilds, rename);
        var session = SelectedSessionIdentity.Create(new(1), new(11), new("connection"), 1, 10011);
        var nextSelection = SelectedSessionIdentity.Create(new(1), new(11), new("connection"), 2, 10011);
        await TestGameplay.RunAsync(async () =>
        {
            Assert.Equal(GuildMutationStatus.Success, await operations.StartAsync(session, "Iris", default));
            Assert.True(operations.SetPromotion(session, ""));
            Assert.True(operations.Authorize(session));
            Assert.NotNull(operations.AuthorizedCreation(session));

            var guild = (await Write(() => guilds.CreateAsync(new(11), "Iris", "", default))).Guild!.Value;
            Assert.Equal(GuildMutationStatus.Success, await operations.StartAsync(session, "Fern", default));
            Assert.Null(operations.AuthorizedCreation(session));
            Assert.False(operations.SetPromotion(session, "stale"));
            Assert.False(operations.Authorize(session));
            Assert.Equal(new GuildRenameAuthorization(guild, "Iris", "Fern"), operations.CheckedRename(session));
            Assert.Null(operations.CheckedRename(nextSelection));

            Assert.Equal(GuildMutationStatus.NameTaken, await operations.StartAsync(session, "Iris", default));
            Assert.Null(operations.CheckedRename(session));
            Assert.Null(operations.AuthorizedCreation(session));
            Assert.Equal(GuildMutationStatus.Success, await operations.StartAsync(session, "Fern", default));
            operations.Cancel(session);
            Assert.Null(operations.CheckedRename(session));
        });
    }

    [Fact]
    public async Task Creates_one_guild_and_leader_for_one_payment()
    {
        var created = await Write(() => guilds.CreateAsync(new(11), "Iris", "Hello", default));
        Assert.Equal(GuildMutationStatus.Success, created.Status);
        Assert.Equal(600_000, created.RemainingGold);
        Assert.Equal(new GuildPublicIdentity(created.Guild!.Value, "Iris", 1),
            await query.FindPublicIdentityAsync(new(11), default));
        Assert.True(Assert.Single(await query.ReadRosterAsync(new(11), default)).IsLeader);
        Assert.Equal(GuildMutationStatus.AlreadyMember,
            (await Write(() => guilds.CreateAsync(new(11), "More", "", default))).Status);
        Assert.Equal(GuildMutationStatus.NameTaken,
            (await Write(() => guilds.CreateAsync(new(12), "iRIS", "", default))).Status);
        Assert.Equal(GuildMutationStatus.InsufficientGold,
            (await Write(() => guilds.CreateAsync(new(13), "Silver", "", default))).Status);
        Assert.Equal(GuildMutationStatus.CharacterUnavailable,
            (await Write(() => guilds.CreateAsync(new(16), "Hidden", "", default))).Status);
        Assert.Equal(1L, Execute("SELECT COUNT(*) FROM guilds"));
        Assert.Equal(600_000L, Execute("SELECT carried_gold FROM character_stats WHERE character_id=11"));
        Assert.Equal(900_000L, Execute("SELECT carried_gold FROM character_stats WHERE character_id=12"));
    }

    [Fact]
    public async Task Failure_inserting_the_leader_rolls_back_the_guild_and_payment()
    {
        Execute("""
            CREATE TRIGGER fail_guild_member BEFORE INSERT ON guild_members
            BEGIN SELECT RAISE(ABORT,'forced guild membership fault'); END;
            """);
        await Assert.ThrowsAsync<SqliteException>(() => Write(() => guilds.CreateAsync(new(11), "Iris", "", default)));
        Assert.Null(await query.FindPublicIdentityAsync(new(11), default));
        Assert.Equal(0L, Execute("SELECT COUNT(*) FROM guilds"));
        Assert.Equal(900_000L, Execute("SELECT carried_gold FROM character_stats WHERE character_id=11"));
    }

    [Fact]
    public async Task Missing_creation_price_cannot_create_a_free_guild()
    {
        economy.CreationGold = null;
        var result = await Write(() => guilds.CreateAsync(new(11), "Iris", "", default));
        Assert.Equal(GuildMutationStatus.CreationUnavailable, result.Status);
        Assert.Null(await query.FindMembershipAsync(new(11), default));
        Assert.Equal(0L, Execute("SELECT COUNT(*) FROM guilds"));
        Assert.Equal(900_000L, Execute("SELECT carried_gold FROM character_stats WHERE character_id=11"));
    }

    [Fact]
    public async Task Application_is_visible_and_only_the_intended_leader_can_approve_it_once()
    {
        var first = (await Write(() => guilds.CreateAsync(new(11), "Iris", "", default))).Guild!.Value;
        var second = (await Write(() => guilds.CreateAsync(new(12), "Violet", "", default))).Guild!.Value;
        Assert.Equal(GuildMutationStatus.Success,
            (await Write(() => guilds.ApplyAsync(new(13), first, "Let me in", default))).Status);
        var application = Assert.IsType<GuildApplication>(await query.FindApplicationAsync(new(13), default));
        Assert.Equal(first, application.Guild);
        Assert.Equal("Let me in", application.Message);
        Assert.Equal(application, Assert.Single(await query.ReadApplicationsAsync(new(11), default)));
        Assert.Empty(await query.ReadApplicationsAsync(new(12), default));
        Assert.Equal(GuildMutationStatus.ApplicationPending,
            (await Write(() => guilds.ApplyAsync(new(13), second, "", default))).Status);
        Assert.Equal(GuildMutationStatus.LeaderRequired,
            (await Write(() => guilds.ApproveAsync(new(14), new(13), default))).Status);
        Assert.Equal(GuildMutationStatus.ApplicationNotFound,
            (await Write(() => guilds.ApproveAsync(new(12), new(13), default))).Status);
        Assert.Equal(GuildMutationStatus.Success,
            (await Write(() => guilds.ApproveAsync(new(11), new(13), default))).Status);
        Assert.Equal(GuildMutationStatus.ApplicationNotFound,
            (await Write(() => guilds.ApproveAsync(new(11), new(13), default))).Status);
        Assert.Equal(GuildMutationStatus.AlreadyMember,
            (await Write(() => guilds.ApplyAsync(new(13), second, "", default))).Status);
        Assert.Null(await query.FindApplicationAsync(new(13), default));
        await Write(() => guilds.ApplyAsync(new(14), first, "Another", default));
        Assert.Equal(new CharacterId(14), Assert.Single(await query.ReadApplicationsAsync(new(11), default)).Character);
        Assert.Empty(await query.ReadApplicationsAsync(new(13), default));
        Assert.Equal(first, (await query.FindPublicIdentityAsync(new(13), default))!.Id);
        Assert.Equal(new[] { 11, 13 }, (await query.ReadRosterAsync(new(13), default)).Select(member => member.Character.Value));
        Assert.Single(await query.ReadRosterAsync(new(12), default));
        var roster = await query.ReadRosterEntriesAsync(new(13), default);
        Assert.Equal(new[] { 11, 13 }, roster.Select(entry => entry.Membership.Character.Value));
        Assert.All(roster, entry =>
        {
            Assert.Equal(first, entry.Membership.Guild);
            Assert.True(entry.Account.Value > 0);
            Assert.NotEmpty(entry.Name);
        });
        Assert.True(roster[0].Membership.IsLeader);
        Assert.False(roster[1].Membership.IsLeader);
        Assert.Equal(second, Assert.Single(await query.ReadRosterEntriesAsync(new(12), default)).Membership.Guild);
        Assert.Empty(await query.ReadRosterEntriesAsync(new(14), default));
    }

    [Fact]
    public async Task Leaving_removing_and_disbanding_clear_the_right_relations()
    {
        var guild = (await Write(() => guilds.CreateAsync(new(11), "Iris", "", default))).Guild!.Value;
        Assert.Equal(GuildMutationStatus.Success,
            (await Write(() => donations.DonateGoldAsync(new(11), 1000, default))).Status);
        await Join(new(13));
        await Join(new(14));
        Assert.Equal(GuildMutationStatus.LeaderCannotLeave, (await Write(() => guilds.LeaveAsync(new(11), default))).Status);
        Assert.Equal(GuildMutationStatus.LeaderRequired,
            (await Write(() => guilds.RemoveMemberAsync(new(13), new(14), default))).Status);
        Assert.Equal(GuildMutationStatus.Success,
            (await Write(() => guilds.RemoveMemberAsync(new(11), new(13), default))).Status);
        Assert.Null(await query.FindMembershipAsync(new(13), default));
        Assert.Equal(GuildMutationStatus.Success, (await Write(() => guilds.LeaveAsync(new(14), default))).Status);
        Assert.Null(await query.FindMembershipAsync(new(14), default));
        await Join(new(14));
        await Write(() => guilds.ApplyAsync(new(15), guild, "Pending application", default));
        Assert.Equal(GuildMutationStatus.GuildHasOtherMembers,
            (await Write(() => guilds.DisbandAsync(new(11), default))).Status);
        Assert.NotNull(await query.FindMembershipAsync(new(14), default));
        Assert.Equal(GuildMutationStatus.Success, (await Write(() => guilds.LeaveAsync(new(14), default))).Status);
        Assert.Equal(new[] { new CharacterId(11) }, await query.ReadMemberIdsAsync(guild, default));
        var destroyed = await Write(() => guilds.DisbandAsync(new(11), default));
        Assert.Equal(GuildMutationStatus.Success, destroyed.Status);
        Assert.Equal(new[] { 11, 15 }, destroyed.AffectedCharacters!.Select(character => character.Value));
        Assert.Empty(await query.ReadRosterAsync(new(11), default));
        Assert.Null(await query.FindMembershipAsync(new(14), default));
        Assert.Null(await query.FindApplicationAsync(new(15), default));
        Assert.Equal(0L, Execute("SELECT COUNT(*) FROM guilds"));
        Assert.Equal(0L, Execute("SELECT COUNT(*) FROM guild_gold_donations"));

        async Task Join(CharacterId applicant)
        {
            Assert.Equal(GuildMutationStatus.Success,
                (await Write(() => guilds.ApplyAsync(applicant, guild, "", default))).Status);
            Assert.Equal(GuildMutationStatus.Success,
                (await Write(() => guilds.ApproveAsync(new(11), applicant, default))).Status);
        }
    }

    [Fact]
    public async Task Deletion_rejects_member_and_clears_a_nonmembers_application()
    {
        var guild = (await Write(() => guilds.CreateAsync(new(11), "Iris", "", default))).Guild!.Value;
        await Write(() => guilds.ApplyAsync(new(13), guild, "", default));
        Assert.False(await Delete(new(11)));
        Assert.NotNull(await query.FindPublicIdentityAsync(new(11), default));
        Assert.Equal(0L, Execute("SELECT delete_flag FROM characters WHERE character_id=11"));
        Assert.True(await Delete(new(13)));
        Assert.Equal(1L, Execute("SELECT delete_flag FROM characters WHERE character_id=13"));
        Assert.Null(await query.FindApplicationAsync(new(13), default));

        Task<bool> Delete(CharacterId character) => TestGameplay.RunAsync(async () =>
        {
            await using var unit = scope.Begin();
            Assert.True(await characters.MarkDeletedAsync(character, default));
            if (!await guilds.PrepareCharacterDeletionAsync(character, default))
                return false;
            await unit.CommitAsync(default);
            return true;
        });
    }

    [Fact]
    public async Task Donations_charge_each_donor_and_accumulate_shared_and_personal_points()
    {
        var guild = (await Write(() => guilds.CreateAsync(new(11), "Iris", "", default))).Guild!.Value;
        await Write(() => guilds.ApplyAsync(new(12), guild, "", default));
        await Write(() => guilds.ApproveAsync(new(11), new(12), default));

        var memberDonation = await Write(() => donations.DonateGoldAsync(new(12), 10_000, default));
        Assert.Equal(GuildMutationStatus.Success, memberDonation.Status);
        Assert.Equal(890_000, memberDonation.RemainingGold);
        var leader = Assert.IsType<GuildMembership>(await query.FindMembershipAsync(new(11), default));
        Assert.Equal(10, leader.Guild.GmPoints);
        Assert.Equal(0, leader.Member.DonationPoints);

        var leaderDonation = await Write(() => donations.DonateGoldAsync(new(11), 3_000, default));
        Assert.Equal(GuildMutationStatus.Success, leaderDonation.Status);
        Assert.Equal(597_000, leaderDonation.RemainingGold);
        var member = Assert.IsType<GuildMembership>(await query.FindMembershipAsync(new(12), default));
        Assert.Equal(13, member.Guild.GmPoints);
        Assert.Equal(10, member.Member.DonationPoints);
        Assert.Equal(3, (await query.FindMembershipAsync(new(11), default))!.Member.DonationPoints);

        Execute("UPDATE characters SET name='NewName' WHERE character_id=12");
        var history = (await donations.ReadDonationHistoryAsync(new(11), 50, default))!;
        Assert.Collection(history,
            entry =>
            {
                Assert.Equal("Leader"u8.ToArray(), entry.DonorName);
                Assert.Equal((3000, 3), (entry.Gold, entry.Points));
            },
            entry =>
            {
                Assert.Equal("Other"u8.ToArray(), entry.DonorName);
                Assert.Equal((10000, 10), (entry.Gold, entry.Points));
            });
        Assert.All(history, entry => Assert.True(entry.DonatedAt > DateTimeOffset.UnixEpoch));
        Assert.Single((await donations.ReadDonationHistoryAsync(new(12), 1, default))!);
        Assert.Null(await donations.ReadDonationHistoryAsync(new(13), 50, default));
        Execute("UPDATE character_stats SET carried_gold=900000 WHERE character_id=13");
        Assert.Equal(GuildMutationStatus.Success,
            (await Write(() => guilds.CreateAsync(new(13), "Violet", "", default))).Status);
        Assert.Empty((await donations.ReadDonationHistoryAsync(new(13), 50, default))!);
    }

    [Theory]
    [InlineData(0, GuildMutationStatus.InvalidDonationAmount)]
    [InlineData(-1000, GuildMutationStatus.InvalidDonationAmount)]
    [InlineData(1500, GuildMutationStatus.InvalidDonationAmount)]
    [InlineData(10_000_000, GuildMutationStatus.InsufficientGold)]
    [InlineData(10_000_001, GuildMutationStatus.DonationTooLarge)]
    [InlineData(10_001_000, GuildMutationStatus.DonationTooLarge)]
    [InlineData(601_000, GuildMutationStatus.InsufficientGold)]
    public async Task Rejected_donation_leaves_all_balances_unchanged(int amount, GuildMutationStatus expected)
    {
        await Write(() => guilds.CreateAsync(new(11), "Iris", "", default));
        Assert.Equal(expected, (await Write(() => donations.DonateGoldAsync(new(11), amount, default))).Status);
        AssertInitialDonationBalances();
    }

    [Fact]
    public async Task Donation_requires_membership_and_a_configured_rate()
    {
        await Write(() => guilds.CreateAsync(new(11), "Iris", "", default));
        Assert.Equal(GuildMutationStatus.NotMember,
            (await Write(() => donations.DonateGoldAsync(new(12), 1000, default))).Status);
        Assert.Equal(900_000L, Execute("SELECT carried_gold FROM character_stats WHERE character_id=12"));

        economy.DonationGold = null;
        Assert.Equal(GuildMutationStatus.DonationUnavailable,
            (await Write(() => donations.DonateGoldAsync(new(11), 1000, default))).Status);
        AssertInitialDonationBalances();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Donation_that_would_overflow_either_points_balance_is_rejected(bool guildLimit)
    {
        await Write(() => guilds.CreateAsync(new(11), "Iris", "", default));
        Execute(guildLimit
            ? "UPDATE guilds SET gm_points=2147483647"
            : "UPDATE guild_members SET donation_points=2147483647");
        Assert.Equal(GuildMutationStatus.ContributionLimit,
            (await Write(() => donations.DonateGoldAsync(new(11), 1000, default))).Status);
        Assert.Equal(600_000L, Execute("SELECT carried_gold FROM character_stats WHERE character_id=11"));
        Assert.Equal(guildLimit ? int.MaxValue : 0L, Execute("SELECT gm_points FROM guilds"));
        Assert.Equal(guildLimit ? 0L : int.MaxValue, Execute("SELECT donation_points FROM guild_members"));
    }

    [Fact]
    public async Task Failure_updating_the_donor_rolls_back_gold_and_shared_points()
    {
        await Write(() => guilds.CreateAsync(new(11), "Iris", "", default));
        Execute("""
            CREATE TRIGGER fail_guild_donation BEFORE UPDATE OF donation_points ON guild_members
            BEGIN SELECT RAISE(ABORT,'forced guild donation fault'); END;
            """);
        await Assert.ThrowsAsync<SqliteException>(() => Write(() => donations.DonateGoldAsync(new(11), 1000, default)));
        AssertInitialDonationBalances();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Warehouse_custody_preserves_items_when_the_audit_fails(bool pile)
    {
        var guild = (await Write(() => guilds.CreateAsync(new(11), "Iris", "", default))).Guild!.Value;
        var items = new SqliteItemDomainStore(path, TestRequired.NoContentsEvents);
        IGuildWarehouseCustodyStore custody = items;
        var container = pile ? ContainerId.ForAccount(new(1), InventoryContainerType.SharedMaterial)
            : ContainerId.ForCharacter(new(11), InventoryContainerType.Main);
        var slot = pile ? 4 : 9;
        var itemId = pile ? 3037 : 1001;
        var initial = await Write(async () =>
        {
            await warehouse.SetWarehouseCapacityAsync(guild, 8, default);
            var session = items.SessionIn(scope.Current!);
            var record = await session.CreateAsync(new ItemSnapshot
            {
                ItemId = itemId, Count = pile ? 7 : 1, IsPile = pile, LegacyKind = pile ? (byte)11 : (byte)1,
                Type = ItemTraitClassification.For(pile ? (byte)11 : (byte)1, itemId),
            }, default);
            return (await session.PutAsync(container, slot, record, default)).Snapshot;
        });

        async ValueTask<bool> Deposit()
        {
            var inventory = items.SessionIn(scope.Current!);
            var vault = custody.SessionIn(scope.Current!);
            var owned = Assert.IsType<ItemReference>(await inventory.GetAsync(container, slot, default));
            var moving = await inventory.TakeAsync(owned, pile ? 3 : null, default);
            await vault.DepositAsync(guild, 0, moving, null, default);
            await warehouse.AppendWarehouseHistoryAsync(guild, new(11), GuildWarehouseAction.Deposit,
                itemId, moving.Snapshot.Count, clock.Now, default);
            return true;
        }

        Execute("CREATE TRIGGER audit_fault BEFORE INSERT ON guild_warehouse_history BEGIN SELECT RAISE(ABORT,'audit'); END");
        await Assert.ThrowsAsync<SqliteException>(() => Write(Deposit));
        Assert.Empty(await warehouse.ReadWarehouseItemsAsync(guild, default));
        Assert.Empty(await warehouse.ReadWarehouseHistoryAsync(guild, 5, default));
        var restored = await Write(() => items.SessionIn(scope.Current!).GetAsync(container, slot, default));
        Assert.Equal(initial.ItemUid, restored!.Snapshot.ItemUid);
        Assert.Equal(initial.Count, restored.Count);
        Execute("DROP TRIGGER audit_fault");
        Assert.True(await Write(Deposit));
        var held = Assert.Single(await warehouse.ReadWarehouseItemsAsync(guild, default));
        Assert.Equal(pile ? 3 : 1, held.Item.Count);
        Assert.Equal(initial.ItemUid, held.Item.ItemUid);
        Assert.Single(await warehouse.ReadWarehouseHistoryAsync(guild, 5, default));
    }

    [Fact]
    public async Task Rejected_gold_rename_leaves_name_and_balance_in_a_normally_committed_unit()
    {
        var guild = (await Write(() => guilds.CreateAsync(new(11), "Iris", "", default))).Guild!.Value;
        var service = new GuildRenameDomainService(store, new(economy), TestRequired.Absent<IBusinessInventorySlotDomainService>(),
            TestRequired.Absent<IItemLifecycleDomainService>(), players, clock);
        var request = new GuildRenameAuthorization(guild, "Iris", "Fern");
        var rejected = await Write(() => service.RenameWithGoldAsync(new(11), request, 22, default));
        Assert.Equal(GuildMutationStatus.InsufficientGold, rejected.Status);
        Assert.Equal("Iris", (await store.FindMembershipAsync(new(11), default))!.Guild.Name);
        Assert.Equal(600_000L, Execute("SELECT carried_gold FROM character_stats WHERE character_id=11"));
        Execute("UPDATE character_stats SET carried_gold=700000 WHERE character_id=11");
        var renamed = await Write(() => service.RenameWithGoldAsync(new(11), request, 22, default));
        Assert.Equal(GuildMutationStatus.Success, renamed.Status);
        Assert.Equal("Fern", (await store.FindMembershipAsync(new(11), default))!.Guild.Name);
        Assert.Equal(0L, Execute("SELECT carried_gold FROM character_stats WHERE character_id=11"));
    }

    [Theory]
    [InlineData(0, 49, 10, false)]
    [InlineData(79, 50, 99, false)]
    [InlineData(80, 50, 0, true)]
    [InlineData(95, 50, 0, true)]
    public async Task Guild_dungeon_repeat_bonus_persists_progress_and_delivers_once_per_clear(
        int prior, int peerLevel, int expectedProgress, bool expectBonus)
    {
        var guild = (await Write(() => guilds.CreateAsync(new(11), "Iris", "", default))).Guild!.Value;
        var member = (await query.FindMembershipAsync(new(11), default))!.Member;
        var progress = new SqliteGuildDungeonStore(new(path, scope), points);
        if (prior > 0)
            Execute($"INSERT INTO guild_dungeon_repeat_bonus(character_id,progress) VALUES(11,{prior})");

        var dungeon = new GuildDungeonDefinition(8500, 100, 5, 100, [], [new(100, 101, 1, 0)]);
        var catalog = new GuildDungeonCatalog(1, new Dictionary<int, GuildDungeonDefinition> { [8500] = dungeon })
            { RepeatBonus = new(102, 2, 10) };
        var settledAt = clock.Now.AddMinutes(1);
        var mail = new List<ItemDeliveryGrant>();
        var delivery = TestRequired.Absent<IItemDeliveryDomainService>((_, args) =>
        {
            mail.Add(Assert.Single((IReadOnlyList<ItemDeliveryGrant>)args![2]!));
            Assert.Equal(ItemDeliveryMode.MailOnly, args[5]);
            Assert.Equal(settledAt.AddDays(30), ((ItemDeliveryOptions)args[6]!).MailExpiresAt);
            return ValueTask.FromResult(new ItemDeliveryReceipt([], [1], 1));
        });
        var service = new GuildDungeonDomainService(new(TestRequired.Absent<IGuildDungeonContentSource>((_, _) => catalog)),
            progress, store, points, TestRequired.Absent<ISelectedSessionDomainService>(), TestRequired.Absent<IDungeonEntryIntentDomainService>(),
            TestRequired.Absent<IDungeonAdmissionDomainService>(), TestRequired.Absent<IDungeonVisitDomainService>(),
            TestRequired.Absent<IDungeonStructureRuleService>(), delivery, new DiscardingEventService(), clock, players);
        var mode = new DungeonRunMode.GuildDungeon(new(guild, 1, GuildDungeonPeriod.At(clock.Now)))
        {
            EntryMemberships = new Dictionary<CharacterId, long> { [new(11)] = member.MembershipId },
            EntryLevels = new Dictionary<CharacterId, int> { [new(11)] = 40, [new(12)] = peerLevel },
        };
        var layout = new DungeonLayout(8500, 0, 1, 1, default, default, 1, 1,
            new Dictionary<MazeCell, DungeonCellDefinition>()) { Mode = mode };
        var run = new DungeonRunState(layout, default, 1, new HashSet<MazeCell>(), clock.Now, RunId: new(Guid.NewGuid()),
            Participants: [new(new(1), new(11), 0, true), new(new(2), new(12), 1, false)], ClearOpen: true,
            CurrentCellState: new(default, 1, 1, 0, [], []))
            { ClearParticipants = [new(new(1), new(11), member.MembershipId), new(new(2), new(12), null)] };

        Assert.True(await Write(() => service.SettleAsync(new(1), new(11), run, settledAt, default)));
        Assert.False(await Write(() => service.SettleAsync(new(1), new(11), run, settledAt, default)));
        Assert.Equal(expectedProgress, await progress.ReadRepeatBonusAsync(new(11), default));
        ItemDeliveryGrant[] expectedMail = expectBonus ? [new(101, 1), new(102, 2)] : [new(101, 1)];
        Assert.Equal(expectedMail, mail);
    }

    [Fact]
    public async Task Red_packet_availability_and_claims_share_the_account_daily_limit()
    {
        var guild = (await Write(() => guilds.CreateAsync(new(11), "Iris", "", default))).Guild!.Value;
        await Write(() => guilds.ApplyAsync(new(13), guild, "", default));
        await Write(() => guilds.ApproveAsync(new(11), new(13), default));
        var actor = SelectedSessionIdentity.Create(new(1), new(11), new("leader"), 1, 10001);
        var alt = SelectedSessionIdentity.Create(actor.AccountId, new(13), new("alt"), 2, 10001);
        var database = new SqliteDatabase(path, scope);
        var packets = new SqliteGuildRedPacketStore(database);
        var wallets = new AccountWalletDomainService(new SqliteAccountTable(database), new AccountWalletRuleService(), scope);
        var config = new GuildRedPacketRules(new Dictionary<int, GuildRedPacketProduct>(), 1, 100, 1);
        var service = new GuildRedPacketDomainService(store, packets,
            new(TestRequired.Absent<IGuildRedPacketRuleSource>((_, _) => config)), players,
            TestRequired.Absent<ICharacterBusinessInventoryDomainService>(), TestRequired.Absent<IApplyEffectsDomainService>(),
            wallets, TestRequired.Absent<IMailboxDomainService>(), clock);
        var now = clock.Now.ToUnixTimeSeconds();
        var packet = new GuildRedPacket(0, guild, new(12), new(2), "Sender", 77, 5, 1, 5, 5, 5,
            now, now + 3600, false, null, 0);
        await Write(async () =>
        {
            await packets.AddAsync(packet, default);
            await packets.AddAsync(packet, default);
            return true;
        });
        var open = await packets.ListAsync(guild, now, default);
        Assert.Equal(2, await service.CountAvailableAsync(actor.CharacterId, actor.AccountId, default));
        Assert.Equal(GuildRedPacketStatus.Success, (await Write(() => service.TakeAsync(actor, open[0].Id, default))).Status);
        var balance = await wallets.ReadAsync(actor.AccountId, default);
        Assert.Equal(0, await service.CountAvailableAsync(alt.CharacterId, alt.AccountId, default));
        Assert.Equal(GuildRedPacketStatus.DailyTakeLimit, (await Write(() => service.TakeAsync(alt, open[1].Id, default))).Status);
        Assert.Equal(GuildRedPacketStatus.AlreadyTaken, (await Write(() => service.TakeAsync(alt, open[0].Id, default))).Status);
        Assert.Empty(await packets.HistoryAsync(open[1].Id, default));
        Assert.Equal(balance, await wallets.ReadAsync(alt.AccountId, default));
    }

    [Fact]
    public async Task Red_packet_quotes_and_purchases_apply_product_rules_before_recording_spending()
    {
        var guild = (await Write(() => guilds.CreateAsync(new(11), "Iris", "", default))).Guild!.Value;
        var product = new GuildRedPacketProduct(490003263, 75, 100, 5);
        GuildRedPacketRules? config = new(new Dictionary<int, GuildRedPacketProduct> { [product.ItemId] = product }, 2, 200, 1);
        var purchases = new GuildRedPacketPurchasePolicy(new(TestRequired.Absent<IGuildRedPacketRuleSource>((_, _) => config)),
            new SqliteGuildRedPacketStore(new(path, scope)), store, clock);
        var item = new CeraShopItemDefinition(product.ItemId, "", CeraShopItemKind.Stackable, 0, false, null, [], [], null, null, []);
        var grant = new CeraShopGrant(item, new(1, 99, new(BusinessInventorySpace.Consumable, 0, 1)), 1, 0, 0);
        var charge = new CeraShopCharge(75, 0, 100, CeraShopPaymentMode.Ordinary);
        var quote = new CeraShopQuote([75, 80], [charge, charge with { ProductId = 80 }],
            [grant, grant with { ExpireDate = 123 }, grant with { Item = item with { ItemId = 99 } }]);
        var prepared = purchases.Prepare(quote, clock.Now);
        Assert.Equal(CeraShopPaymentMode.CeraOnly, prepared.Charges[0].Mode);
        Assert.Equal(quote.Charges[1], prepared.Charges[1]);
        Assert.Equal(clock.Now.AddDays(10).ToUnixTimeSeconds(), prepared.Grants[0].ExpireDate);
        Assert.Equal(quote.Grants[1..], prepared.Grants[1..].ToArray());

        Task<string?> Buy(CeraShopQuote cart) => Write(() => purchases.RecordPurchaseAsync(new(11), cart, clock.Now, default));
        Assert.Equal("红包售价与活动配置不一致。", await Buy(quote with { Charges = [charge with { Cera = 99 }] }));
        Assert.Equal("红包售价与活动配置不一致。", await Buy(quote with { Charges = [charge with { Gold = 1 }] }));
        Assert.Equal("公会成员达到 2 人后才能购买红包。", await Buy(quote));
        await Write(() => guilds.ApplyAsync(new(13), guild, "", default));
        await Write(() => guilds.ApproveAsync(new(11), new(13), default));
        Assert.Null(await Buy(quote));
        Assert.Null(await Buy(quote));
        Assert.Equal("角色当天购买红包的上限为 200 点券。", await Buy(quote));
        config = null;
        Assert.Equal("当前无法读取公会红包规则。", await Buy(quote));
        Assert.Null(await Buy(quote with { Charges = [quote.Charges[1]], Grants = [quote.Grants[2]] }));
        Assert.Equal(200L, Execute("SELECT amount FROM guild_red_packet_purchases"));
    }

    [Fact]
    public async Task Daily_chat_can_pay_on_joining_day_and_attendance_starts_at_the_next_six_am_reset()
    {
        var guild = (await Write(() => guilds.CreateAsync(new(11), "Iris", "", default))).Guild!.Value;
        var reset = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 11, 27, 6, 0, 0), clock.LocalTimeZone);
        clock.Now = reset.AddSeconds(-1);
        await Write(async () =>
        {
            await store.AddMemberAsync(guild, new(13), false, clock.Now, default);
            return true;
        });
        var actor = SelectedSessionIdentity.Create(new(1), new(13), new("member"), 1, 10001);
        var config = new GuildRedPacketRules(new Dictionary<int, GuildRedPacketProduct>(), 1, 1, 1)
        {
            DailyContributionPoints = new Dictionary<GuildDailyContributionKind, int>
                { [GuildDailyContributionKind.Attendance] = 10, [GuildDailyContributionKind.Chat] = 2 },
        };
        var grants = new List<ItemDeliveryGrant>();
        var service = new GuildContributionDomainService(store, new SqliteGuildContributionStore(new(path, scope)),
            new(TestRequired.Absent<IGuildRedPacketRuleSource>((_, _) => config)),
            TestRequired.Absent<IItemDeliveryDomainService>((_, args) =>
            {
                grants.Add(Assert.Single((IReadOnlyList<ItemDeliveryGrant>)args![2]!));
                return ValueTask.FromResult(new ItemDeliveryReceipt([], [], 1));
            }), clock);
        Assert.Null(await Write(() => service.AwardDailyAsync(actor, GuildDailyContributionKind.Attendance, default)));
        Assert.NotNull(await Write(() => service.AwardDailyAsync(actor, GuildDailyContributionKind.Chat, default)));
        clock.Now = reset;
        Assert.NotNull(await Write(() => service.AwardDailyAsync(actor, GuildDailyContributionKind.Attendance, default)));
        Assert.Null(await Write(() => service.AwardDailyAsync(actor, GuildDailyContributionKind.Attendance, default)));
        Assert.Equal(new ItemDeliveryGrant[] { new(GuildRedPacketRules.ContributionCoin, 2), new(GuildRedPacketRules.ContributionCoin, 10) }, grants);
        Assert.Equal(2L, Execute("SELECT count(*) FROM guild_daily_contributions"));
    }

    [Fact]
    public async Task Guild_clear_contribution_awards_only_the_threshold_run_each_day()
    {
        var guild = (await Write(() => guilds.CreateAsync(new(11), "Iris", "", default))).Guild!.Value;
        var member = (await query.FindMembershipAsync(new(11), default))!.Member;
        var ledger = new SqliteGuildContributionStore(new(path, scope));
        var rules = new GuildRedPacketRules(new Dictionary<int, GuildRedPacketProduct>(), 1, 1, 1)
            { GuildDungeonContribution = new(2, 7) };
        var delivered = new List<ItemDeliveryGrant>();
        var service = new GuildContributionDomainService(store, ledger,
            new(TestRequired.Absent<IGuildRedPacketRuleSource>((_, _) => rules)),
            TestRequired.Absent<IItemDeliveryDomainService>((_, args) =>
            {
                delivered.Add(Assert.Single((IReadOnlyList<ItemDeliveryGrant>)args![2]!));
                return ValueTask.FromResult(new ItemDeliveryReceipt([], [], 1));
            }), clock);
        var mode = new DungeonRunMode.GuildDungeon(new(guild, 1, GuildDungeonPeriod.At(clock.Now)))
            { EntryMemberships = new Dictionary<CharacterId, long> { [new(11)] = member.MembershipId } };
        var layout = new DungeonLayout(8500, 0, 1, 1, default, default, 1, 1,
            new Dictionary<MazeCell, DungeonCellDefinition>()) { Mode = mode };
        var template = new DungeonRunState(layout, default, 1, new HashSet<MazeCell>(), clock.Now, ClearOpen: true,
            CurrentCellState: new(default, 1, 1, 0, [], []))
            { ClearParticipants = [new(new(1), new(11), member.MembershipId)] };

        for (var day = 0; day < 2; day++)
        {
            var at = clock.Now.AddDays(day);
            for (var clear = 1; clear <= 4; clear++)
            {
                var run = template with { RunId = new(Guid.NewGuid()), ClearedAt = at };
                var reward = await Write(() => service.AwardClearAsync(new(1), new(11), run, at, default));
                Assert.Equal(clear == 2, reward is not null);
                Assert.Null(await Write(() => service.AwardClearAsync(new(1), new(11), run, at, default)));
            }
        }
        Assert.Equal(8L, Execute("SELECT count(*) FROM guild_clear_contributions"));
        Assert.Equal(14, Assert.Single(await ledger.ReadLeaderboardAsync(guild, default)).Contribution);
        Assert.Equal(2, delivered.Count);
        Assert.All(delivered, grant => Assert.Equal(new ItemDeliveryGrant(GuildRedPacketRules.ContributionCoin, 7), grant));
    }

    [Fact]
    public async Task Dungeon_credit_and_donations_respect_spent_points_and_rollback_failed_clear()
    {
        var guild = (await Write(() => guilds.CreateAsync(new(11), "Iris", "", default))).Guild!.Value;
        var dungeons = new SqliteGuildDungeonStore(new(path, scope), points);
        var admission = new GuildDungeonAdmission(guild, 1, GuildDungeonPeriod.At(clock.Now));
        Execute("UPDATE guilds SET gm_points=2,spent_gm_points=2147483644");
        var run = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Write(() => dungeons.TryRecordClearAsync(run, new(11), admission, 8500, 2, default)));
        Assert.Equal(0L, Execute("SELECT COUNT(*) FROM guild_dungeon_clears"));
        Assert.Equal(0L, Execute("SELECT COUNT(*) FROM guild_dungeon_progress"));
        Assert.True(await Write(() => dungeons.TryRecordClearAsync(run, new(11), admission, 8500, 1, default)));
        Assert.False(await Write(() => dungeons.TryRecordClearAsync(run, new(11), admission, 8500, 1, default)));
        Assert.Equal(GuildMutationStatus.ContributionLimit, (await Write(() => donations.DonateGoldAsync(new(11), 1000, default))).Status);
        Assert.True(await Write(() => points.TrySpendAsync(guild, 2, default)));
        Assert.False(await Write(() => points.TryCreditAsync(guild, 1, default)));
        Assert.Equal(1L, Execute("SELECT gm_points FROM guilds"));
        Assert.Equal(2147483646L, Execute("SELECT spent_gm_points FROM guilds"));
    }

    [Fact]
    public async Task Failure_recording_donation_history_rolls_back_the_payment_and_both_points_updates()
    {
        await Write(() => guilds.CreateAsync(new(11), "Iris", "", default));
        Execute("""
            CREATE TRIGGER fail_guild_donation_history BEFORE INSERT ON guild_gold_donations
            BEGIN SELECT RAISE(ABORT,'forced guild donation history fault'); END;
            """);
        await Assert.ThrowsAsync<SqliteException>(() => Write(() => donations.DonateGoldAsync(new(11), 1000, default)));
        AssertInitialDonationBalances();
    }

    private void AssertInitialDonationBalances()
    {
        Assert.Equal(600_000L, Execute("SELECT carried_gold FROM character_stats WHERE character_id=11"));
        Assert.Equal(0L, Execute("SELECT gm_points FROM guilds"));
        Assert.Equal(0L, Execute("SELECT donation_points FROM guild_members"));
        Assert.Equal(0L, Execute("SELECT COUNT(*) FROM guild_gold_donations"));
    }

    private Task<T> Write<T>(Func<ValueTask<T>> action) => TestGameplay.RunAsync(async () =>
    {
        await using var unit = scope.Begin();
        var result = await action();
        await unit.CommitAsync(default);
        return result;
    });

    private object? Execute(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(path);
    }

    private sealed class GuildTestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class EconomyRules : IGuildEconomySource
    {
        public int? CreationGold { get; set; } = Fee;
        public int? DonationGold { get; set; } = 1000;
        public int? ReadGoldCost() => CreationGold;
        public int? ReadDonationGoldPerPoint() => DonationGold;
        public GuildMonthlyContributionRule? Monthly { get; set; }
        public GuildMonthlyContributionRule? ReadMonthlyContribution() => Monthly;
        public GuildMarkPricing? MarkPricing { get; set; } = new(5, 2000);
        public GuildMarkPricing? ReadMarkPricing() => MarkPricing;
        public GuildMedalAuraCatalog? ReadMedalAuraCatalog() => new(25, 25);
        public GuildContentCatalog? ReadContentCatalog() => new(
            new Dictionary<uint, GuildContentDefinition>
            {
                [1] = new(1, GuildContentKind.PermanentAttributes, 169, 36_000),
                [2] = new(2, GuildContentKind.TemporaryAttributes, 167, 200, 1, CanGift: true),
                [3] = new(3, GuildContentKind.Experience, 10, 200, 1, CanGift: true),
                [11] = new(11, GuildContentKind.WarehouseExpansion, 16, 10_000),
                [12] = new(12, GuildContentKind.WarehouseExpansion, 8, 5_000),
                [13] = new(13, GuildContentKind.PartyTeleport, 10, 300),
            },
            new(169, 60, 60, 60, 60), new(167, 60, 60, 60, 60), 56, new(22, 700_000));
    }
}
