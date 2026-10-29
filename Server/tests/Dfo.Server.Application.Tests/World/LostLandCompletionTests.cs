using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Game;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.World;

public sealed class LostLandCompletionTests
{
    [Fact]
    public void Deadline_blocks_further_room_changes_and_monster_rewards_without_deleting_run()
    {
        using var fixture = Fixture();
        TestGameplay.Run(fixture.Services, () =>
        {
            var runs = fixture.Services.GetRequiredService<IDungeonRunDomainService>();
            var run = Begin(fixture);
            fixture.Clock.Advance(TimeSpan.FromSeconds(60));
            Assert.Null(runs.TryMove(fixture.Session.CharacterId, run.RunId, run.RoomVersion, new(1, 0),
                DungeonCellSeed.CompleteEmpty(new(1, 0), 61001)));
            Assert.Null(runs.TryClaimActorDeath(fixture.Session.CharacterId, 1));
            Assert.Equal(run.RunId, runs.Find(fixture.Session.CharacterId)!.RunId);
        });
    }

    [Fact]
    public async Task Deadline_starts_on_first_loaded_room_and_survives_room_changes()
    {
        using var fixture = Fixture();
        var run = TestGameplay.Run(fixture.Services, () => Begin(fixture, loaded: false));
        fixture.Clock.Advance(TimeSpan.FromMinutes(3));
        Assert.Empty(await Capture(fixture));
        TestGameplay.Run(fixture.Services, () =>
        {
            var runs = fixture.Services.GetRequiredService<IDungeonRunDomainService>();
            runs.TryMarkParticipantReady(fixture.Session.CharacterId, run.RunId, run.RoomVersion);
            Assert.Equal(fixture.Clock.GetUtcNow().AddSeconds(60), runs.Find(fixture.Session.CharacterId)!.LostLandExploration!.EndsAt);
            fixture.Clock.Advance(TimeSpan.FromSeconds(30));
            var moved = runs.TryMove(fixture.Session.CharacterId, run.RunId, run.RoomVersion, new(1, 0),
                DungeonCellSeed.CompleteEmpty(new(1, 0), 61001))!;
            runs.TryMarkParticipantReady(fixture.Session.CharacterId, moved.RunId, moved.RoomVersion);
        });
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(29999));
        Assert.Empty(await Capture(fixture));
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Single(await Capture(fixture));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_completion_is_sent_once_after_loading_and_two_a_finishes_the_retained_run(bool loading)
    {
        using var fixture = Fixture();
        var run = TestGameplay.Run(fixture.Services, () => Begin(fixture));
        if (loading) TestGameplay.Run(fixture.Services, () =>
        {
            fixture.Services.GetRequiredService<IDungeonRunDomainService>().TryMove(fixture.Session.CharacterId,
                run.RunId, run.RoomVersion, new(1, 0), DungeonCellSeed.CompleteEmpty(new(1, 0), 61001));
        });
        fixture.Clock.Advance(TimeSpan.FromSeconds(60));
        var completion = Assert.Single(await Capture(fixture));
        Assert.True(await Complete(fixture, completion));
        Assert.False(await Complete(fixture, completion));
        var current = TestGameplay.Run(fixture.Services, () => fixture.Services
            .GetRequiredService<IDungeonRunDomainService>().Find(fixture.Session.CharacterId)!);
        Assert.True(current.LostLandExploration!.IsEnding);
        Assert.Equal(run.RunId, current.RunId);
        if (loading)
        {
            Assert.Empty(ClearMessages(fixture));
            Assert.Empty(await Capture(fixture));
            TestGameplay.Run(fixture.Services, () => fixture.Services.GetRequiredService<IDungeonRunDomainService>()
                .TryMarkParticipantReady(fixture.Session.CharacterId, current.RunId, current.RoomVersion));
            Assert.True(await Complete(fixture, Assert.Single(await Capture(fixture))));
        }
        Assert.Single(ClearMessages(fixture));
        Assert.Empty(await Capture(fixture));
        await EffectRequestTransport.SendRaw(fixture, new GameMessageMetadata(1, 0x002A, 0, 17), []);
        TestGameplay.Run(fixture.Services, () => Assert.Null(fixture.Services.GetRequiredService<IDungeonRunDomainService>()
            .Find(fixture.Session.CharacterId)));
        Assert.False(await Complete(fixture, completion));
        Assert.Single(ClearMessages(fixture));
    }

    [Fact]
    public async Task Completion_rollback_and_stale_run_cannot_consume_the_new_run_deadline()
    {
        using var fixture = Fixture();
        TestGameplay.Run(fixture.Services, () => Begin(fixture));
        fixture.Clock.Advance(TimeSpan.FromSeconds(60));
        var completion = Assert.Single(await Capture(fixture));
        await TestGameplay.RunAsync(fixture.Services, async () =>
        {
            var runs = fixture.Services.GetRequiredService<IDungeonRunDomainService>();
            var scope = (DomainTransactionScope)fixture.Services.GetRequiredService<IDomainTransactionAccessor>();
            await using (var tx = scope.Begin())
            {
                Assert.True(runs.StageLostLandCompletion(completion, fixture.Clock.GetUtcNow(), true));
                Assert.False(runs.Find(fixture.Session.CharacterId)!.LostLandExploration!.IsEnding);
            }
            Assert.False(runs.Find(fixture.Session.CharacterId)!.LostLandExploration!.IsEnding);
        });
        Assert.Equal(completion, Assert.Single(await Capture(fixture)));
        TestGameplay.Run(fixture.Services, () =>
        {
            var runs = fixture.Services.GetRequiredService<IDungeonRunDomainService>();
            fixture.InTransaction(() => runs.End(fixture.Session.CharacterId, completion.RunId));
            Begin(fixture);
        });
        Assert.False(await Complete(fixture, completion));
        Assert.Empty(ClearMessages(fixture));
        Assert.Empty(await Capture(fixture));
    }

    private static IGameNotiMessage[] ClearMessages(ShopFixture fixture) => fixture.Sender.Messages
        .OfType<IGameNotiMessage>().Where(message => message.MessageType == ServerMessageType.GameEnableClearDungeon).ToArray();
    private static InternalCommandOrigin Origin() => new(GameplayCallSource.Timer,
        GameplayCallChain.NewRoot(GameplayCallSource.Timer), "timer:lost-land-completion-test");
    private static ValueTask<IReadOnlyList<LostLandCompletion>> Capture(ShopFixture fixture) => fixture.Services
        .GetRequiredService<ICommandBus>().SendInternalAsync(new CaptureLostLandCompletionsCommand(), Origin());
    private static ValueTask<bool> Complete(ShopFixture fixture, LostLandCompletion completion) => fixture.Services
        .GetRequiredService<ICommandBus>().SendInternalAsync(new CompleteLostLandCommand(completion), Origin());

    private static ShopFixture Fixture()
    {
        var fixture = new ShopFixture(services =>
        {
            services.AddSingleton<QuestTestCommands.PublishedView>();
            services.AddSingleton<ISelectedCharacterCurrentViewDomainService, ExitView>();
            services.AddSingleton<IDungeonRunDomainService>(provider => TestParties.Runs(
                provider.GetRequiredService<IDomainTransactionAccessor>(),
                parties: provider.GetRequiredService<Dfo.Server.Abstractions.Parties.IPartyService>(),
                participation: provider.GetRequiredService<IDungeonParticipationDomainService>(),
                clock: provider.GetRequiredService<TimeProvider>()));
        });
        fixture.Sql("INSERT INTO character_condition(character_id,pc_room_state) VALUES(1,0); INSERT INTO character_settings(character_id) VALUES(1); INSERT INTO character_pvp(character_id) VALUES(1); INSERT INTO character_expert_job(character_id) VALUES(1);");
        return fixture;
    }

    private sealed class ExitView(QuestTestCommands.PublishedView inner) : ISelectedCharacterCurrentViewDomainService
    {
        public async ValueTask<SelectCharacterResult?> TryLoadAsync(SelectedSessionIdentity session, CancellationToken token)
        {
            var result = (await inner.TryLoadAsync(session, token))!;
            return result with { World = result.World with { Channel = result.World.Channel with {
                Spawn = new(0, 0, 0, 0, 0, 0, false) } } };
        }
    }

    private static DungeonRunState Begin(ShopFixture fixture, bool loaded = true)
    {
        var layout = new DungeonLayout(4000, 0, 2, 1, new(0, 0), new(1, 0), 61000, 17,
            new Dictionary<MazeCell, DungeonCellDefinition>
            {
                [new(0, 0)] = DungeonCellDefinition.Fixed(61000),
                [new(1, 0)] = DungeonCellDefinition.Fixed(61001),
            }) { Mode = new DungeonRunMode.LostLand(new(60, 1200, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2), [], [])),
                ConsumesFatigue = false, NoGiveupPenalty = true };
        var runs = fixture.Services.GetRequiredService<IDungeonRunDomainService>();
        var run = fixture.InTransaction(() => runs.PreparePartyEntry([new(fixture.Session.AccountId, fixture.Session.CharacterId, 0, true)],
            layout, 0, new(layout.StartCell, 61000, 1,
                [new DungeonCellActorState(1, new(3001, 1, 0) { IsHostile = true }, false, false, [], IsMonster: true)], []),
            DungeonRunId.New(), fixture.Clock.GetUtcNow()).Run);
        fixture.Services.GetRequiredService<IDungeonRunProjectionDomainService>().Register(run.RunId, [fixture.Session]);
        if (loaded) runs.TryMarkParticipantReady(fixture.Session.CharacterId, run.RunId, run.RoomVersion);
        return run;
    }
}
