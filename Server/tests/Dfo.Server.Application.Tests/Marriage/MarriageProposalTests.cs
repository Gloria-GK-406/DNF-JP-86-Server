using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Application.Users;
using Dfo.Server.Application.Commands;
using Dfo.Server.Protocol.Game.MarriageProposal;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Marriage;

public sealed class MarriageProposalTests
{
    private sealed class Content : IMarriageProposalContentSource { public MarriageProposalContent Load() => new(30); }
    internal sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    internal static async Task<(LearningFixture F, SelectedSessionIdentity Peer)> Fixture(Clock? clock = null, Action<IServiceCollection>? configure = null)
    {
        var f = new LearningFixture(configure: s =>
        {
            s.AddSingleton<IMarriageProposalContentSource, Content>();
            MarriageTestCommands.Configure(s);
            if (clock is not null) s.AddSingleton<TimeProvider>(clock);
            configure?.Invoke(s);
        });
        await f.SeedAsync();
        f.Sql("UPDATE characters SET job=0 WHERE character_id=72;");
        return TestGameplay.Run(f.Services, () =>
        {
            var registry = f.Services.GetRequiredService<OnlineStateRegistry>();
            var connection = new NetworkConnectionContext(new("marriage-peer"), NetworkEndpointKind.Game, 10011);
            var user = registry.RegisterConnection(connection).OnlineUserId;
            registry.Authenticate(user, new(2));
            var peer = SelectedSessionIdentity.Create(new(2), new(72), connection.ConnectionId, registry.GetRequiredUser(user).Generation, 10011);
            registry.Select(peer);
            registry.EnterWorld(peer);
            f.Services.GetRequiredService<ISelectedSessionDomainService>().Select(peer);
            var positions = f.Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>();
            positions.Begin(f.Session, new(21, 1, 100, 100, 0), 100);
            positions.Begin(peer, new(21, 1, 120, 100, 0), 100);
            var areas = f.Services.GetRequiredService<ITownAreaDomainService>();
            areas.Enter(new(f.Session.CharacterId, f.Session.TownAreaLease(), new(1, 21, 1), new(21, 1, 100, 100, 0), true));
            areas.Enter(new(peer.CharacterId, peer.TownAreaLease(), new(1, 21, 1), new(21, 1, 120, 100, 0), true));
            return (f, peer);
        });
    }
    internal static async Task<MarriageProposalResult> Run(LearningFixture f, Func<IMarriageCoupleDomainService, ValueTask<MarriageProposalResult>> action, bool commit = true)
    {
        return await TestGameplay.RunAsync(f.Services, async () =>
        {
            await using var tx = f.Scope.Begin();
            var result = await action(f.Services.GetRequiredService<IMarriageCoupleDomainService>());
            if (commit) await tx.CommitAsync(default);
            return result;
        });
    }
    [Fact]
    public async Task Acceptance_is_persistent_symmetric_and_replay_does_not_duplicate_relation()
    {
        var (f, peer) = await Fixture(); await using var fixture = f;
        var proposal = await Run(f, s => s.ProposeAsync(f.Session, peer, default));
        Assert.True(proposal.Succeeded);
        var duplicate = await Run(f, s => s.ProposeAsync(f.Session, peer, default));
        Assert.Equal(proposal.Proposal!.Id, duplicate.Proposal!.Id); Assert.False(duplicate.Changed);
        var accept = await Run(f, s => s.RespondProposalAsync(peer, f.Session, 0, default));
        Assert.Equal(MarriageProposalState.Engaged, accept.Proposal!.State);
        var replay = await Run(f, s => s.RespondProposalAsync(peer, f.Session, 0, default));
        Assert.True(replay.Succeeded); Assert.False(replay.Changed);
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM marriage_proposals"));
        Assert.Equal(2, f.Scalar("SELECT COUNT(*) FROM marriage_proposal_participants WHERE active=1"));
        await using var restarted = new LearningFixture(existing: f.Path);
        var store = restarted.Services.GetRequiredService<IMarriageProposalDomainStore>();
        var first = await store.FindActiveAsync(f.Session.CharacterId, default);
        var second = await store.FindActiveAsync(peer.CharacterId, default);
        Assert.Equal(first!.Id, second!.Id); Assert.Equal(MarriageProposalState.Engaged, first.State);
    }
    [Theory]
    [InlineData(1, MarriageProposalState.Rejected)] [InlineData(2, MarriageProposalState.Expired)]
    public async Task Refusal_and_client_timeout_release_both_slots(byte outcome, MarriageProposalState state)
    {
        var (f, peer) = await Fixture(); await using var fixture = f;
        await Run(f, s => s.ProposeAsync(f.Session, peer, default));
        var response = await Run(f, s => s.RespondProposalAsync(peer, f.Session, outcome, default));
        Assert.Equal(state, response.Proposal!.State);
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM marriage_proposal_participants WHERE active=1"));
        Assert.True((await Run(f, s => s.ProposeAsync(f.Session, peer, default))).Succeeded);
    }
    [Fact]
    public async Task Rollback_removes_relation_and_both_participant_reservations()
    {
        var (f, peer) = await Fixture(); await using var fixture = f;
        Assert.True((await Run(f, s => s.ProposeAsync(f.Session, peer, default), false)).Succeeded);
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM marriage_proposals"));
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM marriage_proposal_participants"));
    }
    [Fact]
    public async Task Only_invited_exact_generation_can_answer_and_pending_pair_cannot_be_reversed()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var (f, peer) = await Fixture(); await using var fixture = f;
            await Run(f, s => s.ProposeAsync(f.Session, peer, default));
            Assert.False((await Run(f, s => s.ProposeAsync(peer, f.Session, default))).Succeeded);
            Assert.False((await Run(f, s => s.RespondProposalAsync(f.Session, peer, 0, default))).Succeeded);
            var changed = peer with { ConnectionGeneration = peer.ConnectionGeneration + 1 };
            f.Services.GetRequiredService<ISelectedSessionDomainService>().Select(changed);
            Assert.False((await Run(f, s => s.RespondProposalAsync(changed, f.Session, 0, default))).Succeeded);
            Assert.Equal(0, f.Scalar("SELECT state FROM marriage_proposals"));
        });
    }
    [Fact]
    public async Task Server_expiry_cannot_accept_and_a_new_request_can_claim_slots_again()
    {
        var clock = new Clock(); var (f, peer) = await Fixture(clock); await using var fixture = f;
        await Run(f, s => s.ProposeAsync(f.Session, peer, default));
        clock.Now += TimeSpan.FromMinutes(2);
        Assert.False((await Run(f, s => s.RespondProposalAsync(peer, f.Session, 0, default))).Succeeded);
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM marriage_proposal_participants WHERE active=1"));
        Assert.True((await Run(f, s => s.ProposeAsync(f.Session, peer, default))).Succeeded);
    }
    [Fact]
    public async Task Same_gender_wrong_town_and_self_requests_do_not_create_rows()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var (f, peer) = await Fixture(); await using var fixture = f;
            Assert.False((await Run(f, s => s.ProposeAsync(f.Session, f.Session, default))).Succeeded);
            f.Sql("UPDATE characters SET job=12 WHERE character_id=72;");
            Assert.Equal(MarriageProposalFailure.Ineligible, (await Run(f, s => s.ProposeAsync(f.Session, peer, default))).Failure);
            f.Sql("UPDATE characters SET job=0 WHERE character_id=72;");
            f.Services.GetRequiredService<ITownAreaDomainService>().TransferExact(peer.CharacterId, peer.TownAreaLease(), new(1, 22, 1), new(22, 1, 120, 100, 0), true);
            Assert.False((await Run(f, s => s.ProposeAsync(f.Session, peer, default))).Succeeded);
            Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM marriage_proposals"));
        });
    }
    [Fact]
    public async Task Leaving_town_prevents_acceptance_but_does_not_prevent_refusal()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var (f, peer) = await Fixture(); await using var fixture = f;
            await Run(f, s => s.ProposeAsync(f.Session, peer, default));
            f.Services.GetRequiredService<ITownAreaDomainService>().LeaveExact(peer.CharacterId, peer.TownAreaLease(), TownAreaDepartureReason.DungeonEntry);
            Assert.False((await Run(f, s => s.RespondProposalAsync(peer, f.Session, 0, default))).Succeeded);
            Assert.True((await Run(f, s => s.RespondProposalAsync(peer, f.Session, 1, default))).Succeeded);
        });
    }
    [Fact]
    public async Task Concurrent_cross_proposals_through_real_command_pipeline_commit_only_one_relationship()
    {
        var (f, peer) = await Fixture(); await using var fixture = f;
        var bus = f.Services.GetRequiredService<ICommandBus>();
        var results = await Task.WhenAll(
            bus.SendAsync(new ProposalTestCommand(f.Session, peer, null)).AsTask(),
            bus.SendAsync(new ProposalTestCommand(peer, f.Session, null)).AsTask()).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(results, x => x.Succeeded);
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM marriage_proposals"));
        Assert.Equal(2, f.Scalar("SELECT COUNT(*) FROM marriage_proposal_participants WHERE active=1"));
    }
}
