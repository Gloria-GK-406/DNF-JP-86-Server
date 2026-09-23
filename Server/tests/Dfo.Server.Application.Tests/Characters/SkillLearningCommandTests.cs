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
        Assert.True(users.TryGetByConnection(fixture.Session.ConnectionId, out var online));
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
        var sender = new LearningSender { BeforeSend = () => throw new IOException("closed socket") };
        await using var fixture = Fixture(sender);
        await fixture.SeedAsync();
        await Assert.ThrowsAsync<IOException>(() => Handle(fixture, [new(1, 0, 2)]).AsTask());
        Assert.Equal(4, fixture.Scalar("SELECT level FROM character_skills WHERE character_id=71 AND mode='pve' AND page_index=0 AND skill_id=1"));
    }

    private static LearningFixture Fixture(LearningSender sender) => new(configure: services =>
    {
        new ProtocolModule().ConfigureServices(services, new ConfigurationBuilder().Build());
        services.AddSingleton<INetworkMessageSender>(sender);
    });

    private static ValueTask Handle(LearningFixture fixture, IReadOnlyList<CharacterSkillLearningChange> changes) =>
        fixture.Services.GetRequiredService<LearnSkillHandler>().HandleAsync(
            new(new(fixture.Session.ConnectionId, NetworkEndpointKind.Game, 10011)),
            new LearnSkillRequest(GameMessageMetadata.Empty, 0, changes, 0), default);

    private sealed class LearningSender : INetworkMessageSender
    {
        public Action? BeforeSend { get; set; }
        public List<IServerNetworkMessage> Messages { get; } = [];
        public ValueTask SendAsync(NetworkConnectionId id, IServerNetworkMessage message, CancellationToken ct)
        {
            BeforeSend?.Invoke();
            Messages.Add(message);
            return ValueTask.CompletedTask;
        }
        public ValueTask SendExplicitAsync(NetworkConnectionId id, ExplicitMessageDelivery delivery, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
