using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Characters;

/// <summary>
/// What an online character is like right now: one record per character, owned by exactly one
/// session generation, changed without a lock.
/// </summary>
public sealed class OnlineCharacterRuntimeTests
{
    private static readonly CharacterId Character = new(5101);
    private static readonly ItemCooldownKey Potion = new(IsShared: false, Value: 7);
    private static readonly CharacterPositionState Town = new(3, 1, 100, 200, 0);

    [Fact]
    public void A_record_begins_with_the_published_position_and_vitals_and_nothing_else()
    {
        TestGameplay.Run(() =>
        {
            var runtime = Runtime(out _);
            var session = Session("Game:begin", 2);

            runtime.Begin(session, Town, 140);

            Assert.Equal(Town, runtime.PositionOf(session));
            Assert.Equal(Town, runtime.PositionOf(Character));
            Assert.Equal((byte)100, runtime.HpPercentOf(Character));
            Assert.False(runtime.IsCoolingDown(Character, Potion));
            Assert.Equal(0, runtime.SpiritPotionBonusRateOf(Character));
        });
    }

    [Fact]
    public void Movement_cooldowns_and_potions_belong_to_the_owning_session()
    {
        TestGameplay.Run(() =>
        {
            var runtime = Runtime(out var clock);
            var session = Session("Game:own", 2);
            runtime.Begin(session, Town, 80);

            var stepped = runtime.StepTo(session, 120, 220, 1)!.Position;
            runtime.CrossInto(session, new TownAreaKey(11, 4, 2), 10, 20, true);
            var crossed = runtime.PositionOf(session);
            runtime.StartCooldown(session, Potion, TimeSpan.FromSeconds(30));
            runtime.ApplySpiritPotion(session, 0.5, TimeSpan.FromMinutes(10));

            Assert.Equal(Town with { X = 120, Y = 220, Direction = 1 }, stepped);
            Assert.Equal(stepped with { TownId = 4, AreaId = 2, X = 10, Y = 20 }, crossed);
            Assert.Equal(crossed, runtime.StandingOf(session)!.Position);
            Assert.True(runtime.IsCoolingDown(Character, Potion));
            Assert.Equal(0.5, runtime.SpiritPotionBonusRateOf(Character));

            clock.Advance(TimeSpan.FromMinutes(11));

            Assert.False(runtime.IsCoolingDown(Character, Potion));
            Assert.Equal(0, runtime.SpiritPotionBonusRateOf(Character));
        });
    }

    [Fact]
    public void A_replaced_session_can_neither_read_nor_change_its_successor()
    {
        TestGameplay.Run(() =>
        {
            var runtime = Runtime(out _);
            var stale = Session("Game:stale", 2);
            var successor = Session("Game:successor", 5);
            runtime.Begin(stale, Town, 50);
            runtime.Begin(successor, Town with { X = 1 }, 60);

            Assert.Null(runtime.StepTo(stale, 9, 9, 0));
            Assert.Null(runtime.CrossInto(stale, new TownAreaKey(11, 9, 9), 9, 9, true));
            runtime.StartCooldown(stale, Potion, TimeSpan.FromMinutes(1));
            runtime.ApplySpiritPotion(stale, 1.0, TimeSpan.FromMinutes(1));

            Assert.Null(runtime.PositionOf(stale));
            Assert.Equal(Town with { X = 1 }, runtime.PositionOf(successor));
            Assert.Equal(Town with { X = 1 }, runtime.PositionOf(Character));
            Assert.Equal((byte)60, runtime.HpPercentOf(Character));
            Assert.False(runtime.IsCoolingDown(Character, Potion));
            Assert.Equal(0, runtime.SpiritPotionBonusRateOf(Character));

            Assert.False(runtime.EndExact(stale));
            Assert.Equal(Town with { X = 1 }, runtime.PositionOf(successor));
        });
    }

    [Fact]
    public void Ending_removes_everything_and_late_writes_do_not_bring_it_back()
    {
        TestGameplay.Run(() =>
        {
            var runtime = Runtime(out _);
            var session = Session("Game:end", 2);
            runtime.Begin(session, Town, 70);
            runtime.StartCooldown(session, Potion, TimeSpan.FromMinutes(1));

            Assert.True(runtime.EndExact(session));

            Assert.Null(runtime.StepTo(session, 1, 1, 0));
            runtime.StartCooldown(session, Potion, TimeSpan.FromMinutes(1));
            runtime.ApplySpiritPotion(session, 1.0, TimeSpan.FromMinutes(1));
            Assert.Null(runtime.PositionOf(session));
            Assert.Null(runtime.PositionOf(Character));
            Assert.Equal((byte)0, runtime.HpPercentOf(Character));
            Assert.False(runtime.IsCoolingDown(Character, Potion));
            Assert.Equal(0, runtime.SpiritPotionBonusRateOf(Character));
            Assert.False(runtime.EndExact(session));
        });
    }

    [Fact]
    public void Beginning_again_starts_without_the_previous_sessions_cooldowns_or_potion()
    {
        TestGameplay.Run(() =>
        {
            var runtime = Runtime(out _);
            var first = Session("Game:first", 2);
            runtime.Begin(first, Town, 70);
            runtime.StartCooldown(first, Potion, TimeSpan.FromMinutes(5));
            runtime.ApplySpiritPotion(first, 0.3, TimeSpan.FromMinutes(5));

            runtime.Begin(Session("Game:second", 3), Town, 70);

            Assert.False(runtime.IsCoolingDown(Character, Potion));
            Assert.Equal(0, runtime.SpiritPotionBonusRateOf(Character));
        });
    }

    [Fact]
    public async Task Concurrent_updates_of_one_session_are_all_kept()
    {
        var runtime = Runtime(out _);
        var session = Session("Game:concurrent", 2);
        TestGameplay.Run(() =>
        {
            runtime.Begin(session, Town, 70);
        });
        var keys = Enumerable.Range(1, 200).Select(value => new ItemCooldownKey(false, value)).ToArray();

        using var start = new ManualResetEventSlim();
        var cooldowns = Task.Run(() =>
        {
            start.Wait();
            TestGameplay.Run(() =>
            {
                foreach (var key in keys) runtime.StartCooldown(session, key, TimeSpan.FromHours(1));
            });
        });
        var steps = Task.Run(() =>
        {
            start.Wait();
            TestGameplay.Run(() =>
            {
                for (short x = 0; x < 200; x++) runtime.StepTo(session, x, 0, 0);
            });
        });
        start.Set();
        await Task.WhenAll(cooldowns, steps);

        TestGameplay.Run(() =>
        {
            Assert.All(keys, key => Assert.True(runtime.IsCoolingDown(Character, key)));
            Assert.Equal((short)199, runtime.PositionOf(session)!.X);
        });
    }

    [Fact]
    public async Task Ending_while_writes_are_in_flight_leaves_no_record()
    {
        var runtime = Runtime(out _);
        var session = Session("Game:race", 2);
        for (var round = 0; round < 50; round++)
        {
            TestGameplay.Run(() =>
            {
                runtime.Begin(session, Town, 70);
            });
            using var start = new ManualResetEventSlim();
            var writes = Task.Run(() =>
            {
                start.Wait();
                TestGameplay.Run(() =>
                {
                    for (var value = 0; value < 100; value++)
                    {
                        runtime.StartCooldown(session, new ItemCooldownKey(false, value), TimeSpan.FromHours(1));
                        runtime.ApplySpiritPotion(session, 0.1, TimeSpan.FromHours(1));
                    }
                });
            });
            var end = Task.Run(() =>
            {
                start.Wait();
                TestGameplay.Run(() =>
                {
                    runtime.EndExact(session);
                });
            });
            start.Set();
            await Task.WhenAll(writes, end);

            TestGameplay.Run(() =>
            {
                Assert.Null(runtime.PositionOf(Character));
                Assert.Equal(0, runtime.SpiritPotionBonusRateOf(Character));
            });
        }
    }

    [Fact]
    public void The_runtime_takes_no_lock()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "Server", "src", "Dfo.Server.Application", "Characters",
            "OnlineCharacterRuntimeDomainService.cs"));

        foreach (var primitive in new[] { "lock (", "lock(", "Monitor.", "SemaphoreSlim", "Mutex", "new Lock(", " Lock " })
            Assert.DoesNotContain(primitive, source, StringComparison.Ordinal);
    }

    private static OnlineCharacterRuntimeDomainService Runtime(out SteppedClock clock)
    {
        clock = new SteppedClock(new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero));
        return new OnlineCharacterRuntimeDomainService(clock, new OnlineCharacterLocationDomainService());
    }

    private static SelectedSessionIdentity Session(string connection, long generation) =>
        SelectedSessionIdentity.Create(
            new AccountId(51), Character, new NetworkConnectionId(connection), generation, 10011);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "Server", "src")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private sealed class SteppedClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset now = start;

        public void Advance(TimeSpan by) => now = now.Add(by);

        public override DateTimeOffset GetUtcNow() => now;
    }
}
