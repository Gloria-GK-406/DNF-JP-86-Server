using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol;
using Dfo.Server.Protocol.Game.LearnSkill;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Characters;

public sealed class SkillLearningCommandTests
{
    [Fact]
    public async Task Handler_acknowledges_only_after_the_whole_batch_is_committed()
    {
        var sender = new LearningSender();
        await using var fixture = Fixture(sender);
        await fixture.SeedAsync();
        sender.BeforeSend = () =>
        {
            Assert.Equal(4, fixture.Scalar("SELECT level FROM character_skills WHERE character_id=71 AND mode='pve' AND page_index=0 AND skill_id=1"));
            Assert.Equal(3, fixture.Scalar("SELECT level FROM character_skills WHERE character_id=71 AND mode='pve' AND page_index=0 AND skill_id=2"));
        };
        await Handle(fixture, [new(1, 0, 2), new(2, 0, 3)]);
        var response = Assert.IsType<LearnSkillResponse>(Assert.Single(sender.Messages));
        Assert.NotNull(response.Receipt);
        Assert.Equal(855, response.Receipt.Ledger.For(new(0)).Sp);
        Assert.Equal([4, 3], response.Receipt.Skills.Select(skill => (int)skill.Level));
    }

    [Fact]
    public async Task A_late_SQLite_failure_rolls_back_and_never_sends_a_success()
    {
        var sender = new LearningSender();
        await using var fixture = Fixture(sender);
        await fixture.SeedAsync();
        fixture.Sql("CREATE TRIGGER refuse_learning BEFORE INSERT ON character_skills WHEN NEW.skill_id=2 BEGIN SELECT RAISE(ABORT,'test learning failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => Handle(fixture, [new(1, 0, 2), new(2, 0, 3)]).AsTask());
        Assert.Equal(2, fixture.Scalar("SELECT level FROM character_skills WHERE character_id=71 AND mode='pve' AND page_index=0 AND skill_id=1"));
        Assert.Equal(0, fixture.Scalar("SELECT COUNT(*) FROM character_skills WHERE character_id=71 AND mode='pve' AND page_index=0 AND skill_id=2"));
        Assert.Empty(sender.Messages);
    }

    [Fact]
    public async Task Unsupported_refund_returns_a_failure_without_changing_the_skill()
    {
        var sender = new LearningSender();
        await using var fixture = Fixture(sender);
        await fixture.SeedAsync();
        await Handle(fixture, [new(1, 1, 1)]);
        Assert.Null(Assert.IsType<LearnSkillResponse>(Assert.Single(sender.Messages)).Receipt);
        Assert.Equal(2, fixture.Scalar("SELECT level FROM character_skills WHERE character_id=71 AND mode='pve' AND page_index=0 AND skill_id=1"));
    }

    [Fact]
    public async Task Concurrent_learning_commands_spend_each_increment_against_current_state()
    {
        await using var fixture = Fixture(new LearningSender());
        await fixture.SeedAsync();
        var users = fixture.Services.GetRequiredService<IOnlineUserQueryDomainService>();
        var online = TestGameplay.Run(fixture.Services, () =>
        {
            Assert.True(users.TryGetByConnection(fixture.Session.ConnectionId, out var online));
            return online;
        });
        var commands = fixture.Services.GetRequiredService<ICommandBus>();
        var command = new LearnSkillCommand(online!.OnlineUserId, fixture.Session, 0, [new(1, 0, 1)], 0);
        var results = await Task.WhenAll(commands.SendAsync(command).AsTask(), commands.SendAsync(command).AsTask());
        Assert.All(results, result => Assert.NotNull(result.Receipt));
        Assert.Equal(new byte[] { 3, 4 }, results.Select(result => Assert.Single(result.Receipt!.Skills).Level).Order().ToArray());
        Assert.Equal(4, fixture.Scalar("SELECT level FROM character_skills WHERE character_id=71 AND mode='pve' AND page_index=0 AND skill_id=1"));
    }

    [Fact]
    public async Task A_send_failure_leaves_the_committed_learning_for_relogin()
    {
        var sender = new LearningSender { Failure = () => new IOException("closed socket") };
        await using var fixture = Fixture(sender);
        await fixture.SeedAsync();
        await Assert.ThrowsAsync<IOException>(() => Handle(fixture, [new(1, 0, 2)]).AsTask());
        Assert.Equal(4, fixture.Scalar("SELECT level FROM character_skills WHERE character_id=71 AND mode='pve' AND page_index=0 AND skill_id=1"));
    }

    private static LearningFixture Fixture(LearningSender sender) => new(configure: services =>
    {
        new ProtocolModule().ConfigureServices(services, new ConfigurationBuilder().Build());
        sender.Use(RecordedOutbound.AddTo(services));
    });

    private static async ValueTask Handle(LearningFixture fixture, IReadOnlyList<CharacterSkillLearningChange> changes) =>
        await TestGameplay.RunAsync(fixture.Services, async () =>
        {
            var request = new LearnSkillRequest(GameMessageMetadata.Empty, 0, changes, 0);
            await TestEffectFootprints.Effect(fixture.Services, fixture.Session, request.Metadata, request.MinimumReplayNeeds,
                () => fixture.Services.GetRequiredService<LearnSkillHandler>().HandleAsync(
                    new(new(fixture.Session.ConnectionId, NetworkEndpointKind.Game, 10011)), request, default));
        });

    /// <summary>
    /// What the handler registered, read from the outbox through the container's real registration adapter (TASK-25).
    /// <see cref="Failure"/> makes every single send fail to encode where it is registered; an explicit delivery fails
    /// there too, as the old double refused one.
    /// </summary>
    private sealed class LearningSender
    {
        private RecordedOutbound? outbound;
        public Action? BeforeSend { get; set; }
        public Func<Exception>? Failure { get; init; }
        public RecordedMessages<IServerNetworkMessage> Messages => Outbound.Messages;
        private RecordedOutbound Outbound => outbound ?? throw new InvalidOperationException("The fixture has not been built.");

        public void Use(RecordedOutbound recorded)
        {
            outbound = recorded;
            recorded.BeforeSend = () => BeforeSend?.Invoke();
            if (Failure is { } failure)
                recorded.FailAny(_ => recorded.ExplicitInFlight is null, failure);
            recorded.FailAny(_ => recorded.ExplicitInFlight is not null, () => new NotSupportedException());
        }
    }
}
