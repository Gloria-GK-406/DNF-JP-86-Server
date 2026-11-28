using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Events;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Application.Tests.World;
using Dfo.Server.Data.Events;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Events;

public sealed class LuckyCharacterEventTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task Burning_cannot_change_while_a_character_occupies_a_dungeon()
    {
        var owner = new DungeonParticipationOwner(DungeonParticipationMode.Ordinary, Guid.NewGuid());
        var player = new LuckyCharacterPlayerDomainService(
            TestRequired.Absent<ILuckyCharacterEventQuery>(), TestRequired.Absent<ILuckyCharacterBurning>(),
            TestRequired.Absent<IChannelEntryDomainService>(), TestRequired.Absent<IOnlineCharacterRuntimeDomainService>(),
            TestRequired.Absent<ICharacterDomainService>(), TestRequired.Absent<ICharacterFatigueDomainService>(),
            TestRequired.Absent<IDungeonEntryIntentDomainService>(),
            TestRequired.Absent<IDungeonParticipationDomainService>((method, _) =>
                method.Name == nameof(IDungeonParticipationDomainService.Find) ? owner : null),
            TestGameplayBenefits.LuckyRules, TimeProvider.System);
        var session = new SelectedSessionIdentity(new(1), new(1), new("lucky-character"), 1, 1, 10011);

        var result = await player.SetBurningAsync(session, true, default);

        Assert.Equal(new(LuckyCharacterBurningError.OutsideTown), result);
    }

    [Fact]
    public async Task Saved_period_is_closed_by_default_and_applies_only_at_configured_boundaries() =>
        await TestGameplay.RunAsync(async () =>
        {
            await using var fixture = new LearningFixture();
            var query = fixture.Services.GetRequiredService<ILuckyCharacterEventQuery>();
            var now = DateTimeOffset.UtcNow;
            var begin = now.AddHours(1);
            var end = begin.AddHours(2);
            Assert.Equal(LuckyCharacterPeriod.Closed, await query.ReadAtAsync(now, default));

            await Change(fixture, Configure(begin, end, new(false, [12, 0])), now);
            Assert.Empty((await query.ReadAtAsync(begin, default)).Jobs);
            await Change(fixture, new(LuckyCharacterEventDomainService.ActivityId, 1, "enable"), now);

            Assert.Empty((await query.ReadAtAsync(begin.AddTicks(-1), default)).Jobs);
            var open = await query.ReadAtAsync(begin, default);
            Assert.Equal(2, open.Revision);
            Assert.Equal(new[] { 0, 12 }, open.Jobs);
            Assert.True(open.Includes(12));
            Assert.False(open.Includes(11));
            Assert.Empty((await query.ReadAtAsync(end, default)).Jobs);
            await Change(fixture, new(LuckyCharacterEventDomainService.ActivityId, 2, "disable"), begin);
            Assert.Empty((await query.ReadAtAsync(begin, default)).Jobs);
        });

    [Theory]
    [InlineData(false, 0, 0)]
    [InlineData(false, 0, 14)]
    [InlineData(true, 0, 1)]
    public async Task Invalid_job_configuration_preserves_the_revision(bool allJobs, int first, int second) =>
        await TestGameplay.RunAsync(async () =>
        {
            await using var fixture = new LearningFixture();
            var now = DateTimeOffset.UtcNow;
            await Assert.ThrowsAsync<ArgumentException>(() =>
                Change(fixture, Configure(now, now.AddHours(1), new(allJobs, [first, second])), now));
            var query = fixture.Services.GetRequiredService<ILuckyCharacterEventQuery>();
            Assert.Equal(LuckyCharacterPeriod.Closed, await query.ReadAtAsync(now, default));
        });

    private static EventChange Configure(DateTimeOffset begin, DateTimeOffset end,
        LuckyCharacterSettings settings) =>
        new(LuckyCharacterEventDomainService.ActivityId, 0, "configure", "Lucky character", "Lucky", "", "", "",
            begin, end, LuckyCharacter: settings);

    private static async Task Change(LearningFixture fixture, EventChange change, DateTimeOffset now)
    {
        await using var tx = fixture.Scope.Begin();
        await fixture.Services.GetRequiredService<IEventManagementDomainService>().ChangeAsync(change, "test", now, default);
        await tx.CommitAsync(default);
    }
}
