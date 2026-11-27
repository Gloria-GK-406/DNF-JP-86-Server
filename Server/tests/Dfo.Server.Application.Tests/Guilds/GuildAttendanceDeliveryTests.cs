using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Application.Guilds;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Tests.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.GuildAttendance;
using Dfo.Server.Protocol.Game.GuildInfo;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using static Dfo.Server.Application.Tests.Commands.ProtocolCommandExecutionTests;

namespace Dfo.Server.Application.Tests.Guilds;

[Trait("Category", "Unit")]
public sealed class GuildAttendanceDeliveryTests
{
    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, false, false, true)]
    public async Task Entry_settles_both_records_together_before_delivering_the_panel(
        bool newAttendance, bool newContribution, bool contributionFails, bool dailyRewardFails)
    {
        await using var world = new CommandWorld();
        var transactions = world.Services.GetRequiredService<IDomainTransactionAccessor>();
        var actor = SelectedSessionIdentity.Create(new(1), new(11), new("viewer"), 1, 10010);
        var guild = new Guild(new(3), "Iris", "", 1);
        var member = new GuildMember(guild.Id, actor.CharacterId, true, DateTimeOffset.UnixEpoch);
        var participant = new RecordingParticipant("attendance", [], false);
        var dailyParticipant = new RecordingParticipant("daily-reward", [], false);
        IDomainTransaction? attendanceTransaction = null;
        var recordTimes = new List<DateTimeOffset>();
        var deliveries = new List<Type>();
        var panelReads = 0;
        var attendance = TestRequired.Absent<IGuildAttendanceDomainService>((method, args) =>
        {
            if (method.Name != nameof(IGuildAttendanceDomainService.RecordAttendanceAsync)) return null;
            attendanceTransaction = transactions.Current!;
            attendanceTransaction.Enlist("attendance", () => participant);
            recordTimes.Add((DateTimeOffset)args![1]!);
            return ValueTask.FromResult(newAttendance);
        });
        var monthly = TestRequired.Absent<IGuildMonthlyContributionDomainService>((method, args) =>
        {
            if (method.Name == nameof(IGuildMonthlyContributionDomainService.ReadTotalsAsync))
                return ValueTask.FromResult(new GuildContributionTotals(new(0, 0),
                    new Dictionary<CharacterId, GuildContributionAmounts>()));
            if (method.Name != nameof(IGuildMonthlyContributionDomainService.RecordAttendanceAsync)) return null;
            Assert.Same(attendanceTransaction, transactions.Current);
            recordTimes.Add((DateTimeOffset)args![1]!);
            if (contributionFails) throw new InvalidOperationException("Cannot save monthly contribution");
            return ValueTask.FromResult(newContribution);
        });
        var query = TestRequired.Absent<IGuildMembershipQueryDomainService>((method, _) =>
        {
            if (method.Name == nameof(IGuildMembershipQueryDomainService.FindMembershipAsync))
            {
                panelReads++;
                Assert.True(participant.Applied);
                Assert.True(transactions.Current!.IsReadOnly);
                return ValueTask.FromResult<GuildMembership?>(new(guild, member));
            }
            if (method.Name == nameof(IGuildMembershipQueryDomainService.ReadRosterAsync))
                return ValueTask.FromResult<IReadOnlyList<GuildMember>>([member]);
            return null;
        });
        var projections = TestRequired.Absent<ICommandProjections>((_, args) =>
        {
            var audience = Assert.IsType<NotificationAudience.TheseCharacters>(args![0]);
            Assert.Equal(actor.CharacterId, Assert.Single(audience.CharacterIds));
            deliveries.Add(args[1]!.GetType());
            return ValueTask.FromResult(new NotificationDelivery([]));
        });
        var outbox = TestRequired.Absent<IGameCommandOutbox>((method, args) =>
        {
            Assert.Equal(nameof(IGameCommandOutbox.RegisterNotiAsync), method.Name);
            Assert.Equal(actor.ConnectionId, args![0]);
            deliveries.Add(args[1]!.GetType());
            return ValueTask.CompletedTask;
        });
        var overviews = new GuildOverviewDomainService(query, attendance, monthly,
            TestRequired.Absent<IGuildBenefitDomainService>(), TestRequired.Absent<IGuildAllianceDomainService>());
        var sessions = TestRequired.Absent<ISelectedSessionDomainService>((_, _) => true);
        var units = world.Services.GetRequiredService<ICommandWorkUnits>();
        var delivery = new GuildAttendanceDelivery(attendance, monthly, overviews,
            units, sessions, projections, outbox, TimeProvider.System, NullLogger<GuildAttendanceDelivery>.Instance);
        var contributions = TestRequired.Absent<IGuildContributionDomainService>((method, args) =>
        {
            Assert.Equal(nameof(IGuildContributionDomainService.AwardDailyAsync), method.Name);
            Assert.Equal(GuildDailyContributionKind.Attendance, args![1]);
            Assert.NotSame(attendanceTransaction, transactions.Current);
            transactions.Current!.Enlist("daily-reward", () => dailyParticipant);
            if (dailyRewardFails) throw new InvalidOperationException("Cannot deliver daily reward");
            return ValueTask.FromResult<ItemDeliveryReceipt?>(null);
        });
        var daily = new GuildDailyContributionDelivery(contributions, units, sessions, outbox,
            TimeProvider.System, NullLogger<GuildDailyContributionDelivery>.Instance);
        world.Settings.Action = async context =>
        {
            await delivery.RecordAsync(context, actor, default);
            await daily.AwardAsync(context, actor, GuildDailyContributionKind.Attendance, default);
            return 1;
        };

        Assert.Equal(1, await world.Internal(new InternalProbe()));
        Assert.Equal(2, recordTimes.Count);
        Assert.Equal(recordTimes[0], recordTimes[1]);
        Assert.Equal(contributionFails, participant.RolledBack);
        Assert.Equal(dailyRewardFails, dailyParticipant.RolledBack);
        Assert.Equal(!dailyRewardFails, dailyParticipant.Applied);
        Assert.Equal(contributionFails ? 0 : 1, panelReads);
        if (contributionFails) Assert.Empty(deliveries);
        else if (newAttendance || newContribution) Assert.Equal([typeof(GuildInfoPanel)], deliveries);
        else Assert.Equal([typeof(GuildInfoMessage), typeof(GuildHallInfoMessage)], deliveries);
    }
}
