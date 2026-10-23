using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.MarriageProposal;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Marriage;

public sealed class MarriageCeremonyTests
{
    private sealed class Content : IMarriageCeremonyContentSource
    {
        public MarriageCeremonyContent Load() => new(
            [new MarriageCeremonyTier(0, 500_000, 0, 10)], [300, 600, 900]) { CeremonyPositions = [new(670, 330, 0), new(685, 360, 0)] };
    }

    internal static async Task<(LearningFixture Fixture, SelectedSessionIdentity Peer,
        RecordingQuestLogs Logs, MarriageProposalTests.Clock Clock)> Fixture(Action<IServiceCollection>? configure = null)
    {
        var logs = new RecordingQuestLogs();
        var clock = new MarriageProposalTests.Clock();
        var (fixture, peer) = await MarriageProposalTests.Fixture(clock, services =>
        {
            services.AddSingleton<IMarriageCeremonyContentSource, Content>();
            services.AddSingleton<ICharacterQuestLogDomainService>(logs);
            services.AddCommandHandler<MarriageCeremonyCommand, MarriageCeremonyResult,
                MarriageCeremonyCommandHandler>(CommandExecutionKind.Transactional);
            configure?.Invoke(services);
        });
        return (fixture, peer, logs, clock);
    }

    internal static async Task<Guid> MakeEligibleAsync(LearningFixture fixture,
        SelectedSessionIdentity peer, RecordingQuestLogs logs, int applicantGold = 500_000)
    {
        return await TestGameplay.RunAsync(fixture.Services, async () =>
        {
        Assert.True((await MarriageProposalTests.Run(fixture,
            x => x.ProposeAsync(fixture.Session, peer, default))).Succeeded);
        var accepted = await MarriageProposalTests.Run(fixture,
            x => x.RespondProposalAsync(peer, fixture.Session, 0, default));
        var relation = accepted.Proposal!.Id;
        logs.Cleared[71] = [new ClearedQuest(20737, 1), new ClearedQuest(20738, 1)];
        logs.Cleared[72] = [new ClearedQuest(20737, 1), new ClearedQuest(20738, 1)];
        await using (var tx = fixture.Scope.Begin())
        {
            var bindings = fixture.Services.GetRequiredService<IMarriageQuestDomainStore>();
            foreach (var character in new[] { fixture.Session.CharacterId, peer.CharacterId })
                foreach (var quest in new[] { 20737, 20738 })
                    await bindings.BindAsync(character, quest, relation, DateTimeOffset.UtcNow, default);
            await tx.CommitAsync(default);
        }
        fixture.Sql($"""
            INSERT INTO character_stats(character_id,carried_gold) VALUES(71,{applicantGold})
            ON CONFLICT(character_id) DO UPDATE SET carried_gold=excluded.carried_gold;
            INSERT INTO character_stats(character_id,carried_gold) VALUES(72,0)
            ON CONFLICT(character_id) DO NOTHING;
            """);
        return relation;
        });
    }

    internal static async Task<MarriageCeremonyResult> RunAsync(LearningFixture fixture,
        Func<IMarriageCoupleDomainService, ValueTask<MarriageCeremonyResult>> action,
        bool commit = true)
    {
        return await TestGameplay.RunAsync(fixture.Services, async () =>
        {
            await using var tx = fixture.Scope.Begin();
            var result = await action(fixture.Services.GetRequiredService<IMarriageCoupleDomainService>());
            if (commit) await tx.CommitAsync(default);
            return result;
        });
    }

    [Fact]
    public async Task Eligible_pair_reserves_once_without_spending_gold_and_restores_after_reopen()
    {
        var (fixture, peer, logs, _) = await Fixture(); await using var owned = fixture;
        var relation = await MakeEligibleAsync(fixture, peer, logs);
        var first = await RunAsync(fixture,
            x => x.RequestReservationAsync(fixture.Session, peer, 0, 0, default));
        Assert.True(first.Succeeded); Assert.True(first.Changed);
        Assert.Equal(MarriageCeremonyState.Pending, first.Ceremony!.State);
        var duplicate = await RunAsync(fixture,
            x => x.RequestReservationAsync(fixture.Session, peer, 0, 0, default));
        Assert.Equal(first.Ceremony.Id, duplicate.Ceremony!.Id); Assert.False(duplicate.Changed);
        var reserved = await RunAsync(fixture,
            x => x.RespondReservationAsync(peer, fixture.Session, 0, default));
        Assert.Equal(MarriageCeremonyState.Reserved, reserved.Ceremony!.State);
        var replay = await RunAsync(fixture,
            x => x.RespondReservationAsync(peer, fixture.Session, 0, default));
        Assert.True(replay.Succeeded); Assert.False(replay.Changed);
        Assert.Equal(500_000, fixture.Scalar("SELECT carried_gold FROM character_stats WHERE character_id=71"));
        var engagement = fixture.Services.GetRequiredService<IMarriageQueryDomainService>();
        Assert.True((await engagement.SnapshotAsync(new(1), new(71), default)).IsCeremonyReserved);
        Assert.True((await engagement.SnapshotAsync(new(2), new(72), default)).IsCeremonyReserved);
        Assert.Equal(1, fixture.Scalar($"SELECT COUNT(*) FROM marriage_ceremonies WHERE relation_id='{relation:N}'"));
        await using var reopened = new LearningFixture(existing: fixture.Path);
        Assert.True((await reopened.Services.GetRequiredService<IMarriageQueryDomainService>()
            .SnapshotAsync(new(1), new(71), default)).IsCeremonyReserved);
    }

    [Fact]
    public async Task Reserved_attempt_rejects_changed_reply_and_changed_request()
    {
        var (fixture, peer, logs, _) = await Fixture(); await using var owned = fixture;
        await MakeEligibleAsync(fixture, peer, logs);
        Assert.True((await RunAsync(fixture,
            x => x.RequestReservationAsync(fixture.Session, peer, 0, 0, default))).Succeeded);
        Assert.True((await RunAsync(fixture,
            x => x.RespondReservationAsync(peer, fixture.Session, 0, default))).Succeeded);
        Assert.Equal(MarriageCeremonyFailure.Stale, (await RunAsync(fixture,
            x => x.RespondReservationAsync(peer, fixture.Session, 1, default))).Failure);
        Assert.Equal(MarriageCeremonyFailure.Occupied, (await RunAsync(fixture,
            x => x.RequestReservationAsync(fixture.Session, peer, 0, 0, default))).Failure);
        Assert.Equal(MarriageCeremonyFailure.Occupied, (await RunAsync(fixture,
            x => x.RequestReservationAsync(fixture.Session, peer, 0, 1, default))).Failure);
        Assert.Equal(1, fixture.Scalar("SELECT COUNT(*) FROM marriage_ceremonies"));
    }

    [Fact]
    public async Task Expired_attempt_is_idempotent_and_releases_relation_for_a_new_request()
    {
        var (fixture, peer, logs, clock) = await Fixture(); await using var owned = fixture;
        await MakeEligibleAsync(fixture, peer, logs);
        var first = await RunAsync(fixture,
            x => x.RequestReservationAsync(fixture.Session, peer, 0, 0, default));
        clock.Now += TimeSpan.FromMinutes(2);
        Assert.Equal(MarriageCeremonyFailure.Stale, (await RunAsync(fixture,
            x => x.RespondReservationAsync(peer, fixture.Session, 0, default))).Failure);
        var expiryReplay = await RunAsync(fixture,
            x => x.RespondReservationAsync(peer, fixture.Session, 2, default));
        Assert.True(expiryReplay.Succeeded); Assert.False(expiryReplay.Changed);
        Assert.Equal(MarriageCeremonyState.Expired, expiryReplay.Ceremony!.State);
        var next = await RunAsync(fixture,
            x => x.RequestReservationAsync(fixture.Session, peer, 0, 0, default));
        Assert.NotEqual(first.Ceremony!.Id, next.Ceremony!.Id);
    }

    [Theory]
    [InlineData(1, MarriageCeremonyState.Rejected)]
    [InlineData(2, MarriageCeremonyState.Expired)]
    public async Task Refusal_and_client_timeout_release_relation_for_a_new_request(
        byte response, MarriageCeremonyState state)
    {
        var (fixture, peer, logs, _) = await Fixture(); await using var owned = fixture;
        await MakeEligibleAsync(fixture, peer, logs);
        var first = await RunAsync(fixture,
            x => x.RequestReservationAsync(fixture.Session, peer, 0, 0, default));
        var ended = await RunAsync(fixture,
            x => x.RespondReservationAsync(peer, fixture.Session, response, default));
        Assert.Equal(state, ended.Ceremony!.State);
        var next = await RunAsync(fixture,
            x => x.RequestReservationAsync(fixture.Session, peer, 0, 0, default));
        Assert.True(next.Succeeded);
        Assert.NotEqual(first.Ceremony!.Id, next.Ceremony!.Id);
    }

    [Fact]
    public async Task Rolled_back_request_leaves_no_ceremony()
    {
        var (fixture, peer, logs, _) = await Fixture(); await using var owned = fixture;
        await MakeEligibleAsync(fixture, peer, logs);
        Assert.True((await RunAsync(fixture,
            x => x.RequestReservationAsync(fixture.Session, peer, 0, 0, default), false)).Succeeded);
        Assert.Equal(0, fixture.Scalar("SELECT COUNT(*) FROM marriage_ceremonies"));
    }

    [Fact]
    public async Task Concurrent_cross_requests_through_command_bus_commit_only_one_pending()
    {
        var (fixture, peer, logs, _) = await Fixture(); await using var owned = fixture;
        await MakeEligibleAsync(fixture, peer, logs);
        fixture.Sql("UPDATE character_stats SET carried_gold=500000 WHERE character_id=72");
        var bus = fixture.Services.GetRequiredService<ICommandBus>();
        var results = await Task.WhenAll(
            bus.SendAsync(new MarriageCeremonyCommand(fixture.Session, peer, 0, 0, null)).AsTask(),
            bus.SendAsync(new MarriageCeremonyCommand(peer, fixture.Session, 0, 0, null)).AsTask())
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(results, x => x.Succeeded);
        Assert.Single(results, x => x.Failure == MarriageCeremonyFailure.Occupied);
        Assert.Equal(1, fixture.Scalar("SELECT COUNT(*) FROM marriage_ceremonies WHERE state=0"));
    }

    [Fact]
    public async Task Failed_acceptance_recheck_expires_pending_and_releases_relation_immediately()
    {
        var (fixture, peer, logs, _) = await Fixture(); await using var owned = fixture;
        await MakeEligibleAsync(fixture, peer, logs);
        var first = await RunAsync(fixture,
            x => x.RequestReservationAsync(fixture.Session, peer, 0, 0, default));
        fixture.Sql("UPDATE character_stats SET carried_gold=499999 WHERE character_id=71");
        var failed = await RunAsync(fixture,
            x => x.RespondReservationAsync(peer, fixture.Session, 0, default));
        Assert.Equal(MarriageCeremonyFailure.InsufficientFunds, failed.Failure);
        Assert.True(failed.Changed);
        Assert.Equal(MarriageCeremonyState.Expired, failed.Ceremony!.State);
        Assert.Equal((byte)2, failed.Ceremony.Response);
        fixture.Sql("UPDATE character_stats SET carried_gold=500000 WHERE character_id=71");
        var next = await RunAsync(fixture,
            x => x.RequestReservationAsync(fixture.Session, peer, 0, 0, default));
        Assert.True(next.Succeeded);
        Assert.NotEqual(first.Ceremony!.Id, next.Ceremony!.Id);
    }

    [Fact]
    public async Task Reconnected_participant_expires_old_pending_before_new_request()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var (fixture, peer, logs, _) = await Fixture(); await using var owned = fixture;
            await MakeEligibleAsync(fixture, peer, logs);
            var first = await RunAsync(fixture,
                x => x.RequestReservationAsync(fixture.Session, peer, 0, 0, default));
            var reconnected = MarriageHarness.Reselect(fixture, peer);
            fixture.Services.GetRequiredService<ITownAreaDomainService>().Enter(
                new(reconnected.CharacterId, reconnected.TownAreaLease(), new(1, 21, 1),
                    new(21, 1, 120, 100, 0), true));
            var next = await RunAsync(fixture,
                x => x.RequestReservationAsync(fixture.Session, reconnected, 0, 0, default));
            Assert.True(next.Succeeded);
            Assert.True(next.Changed);
            Assert.NotEqual(first.Ceremony!.Id, next.Ceremony!.Id);
            Assert.Equal(3, fixture.Scalar("SELECT state FROM marriage_ceremonies WHERE id='" + first.Ceremony.Id.ToString("N") + "'"));
        });
    }

    [Fact]
    public async Task Invalid_content_quest_binding_gold_session_and_target_do_not_reserve()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var (fixture, peer, logs, _) = await Fixture(); await using var owned = fixture;
            Assert.Equal(MarriageCeremonyFailure.Invalid, (await RunAsync(fixture,
                x => x.RequestReservationAsync(fixture.Session, peer, 1, 0, default))).Failure);
            var relation = await MakeEligibleAsync(fixture, peer, logs, 499_999);
            Assert.Equal(MarriageCeremonyFailure.Invalid, (await RunAsync(fixture,
                x => x.RequestReservationAsync(fixture.Session, peer, 0, 3, default))).Failure);
            Assert.Equal(MarriageCeremonyFailure.InsufficientFunds, (await RunAsync(fixture,
                x => x.RequestReservationAsync(fixture.Session, peer, 0, 0, default))).Failure);
            fixture.Sql("UPDATE character_stats SET carried_gold=500000 WHERE character_id=71");
            logs.Cleared[72].RemoveAll(x => x.QuestId == 20738);
            Assert.Equal(MarriageCeremonyFailure.Ineligible, (await RunAsync(fixture,
                x => x.RequestReservationAsync(fixture.Session, peer, 0, 0, default))).Failure);
            logs.Cleared[72].Add(new ClearedQuest(20738, 1));
            fixture.Sql("DELETE FROM marriage_quest_bindings WHERE character_id=72 AND quest_id=20738");
            Assert.Equal(MarriageCeremonyFailure.Ineligible, (await RunAsync(fixture,
                x => x.RequestReservationAsync(fixture.Session, peer, 0, 0, default))).Failure);
            await using (var tx = fixture.Scope.Begin())
            {
                await fixture.Services.GetRequiredService<IMarriageQuestDomainStore>()
                    .BindAsync(peer.CharacterId, 20738, relation, DateTimeOffset.UtcNow, default);
                await tx.CommitAsync(default);
            }
            var stale = peer with { ConnectionGeneration = peer.ConnectionGeneration + 1 };
            Assert.Equal(MarriageCeremonyFailure.Stale, (await RunAsync(fixture,
                x => x.RequestReservationAsync(fixture.Session, stale, 0, 0, default))).Failure);
            var pending = await RunAsync(fixture,
                x => x.RequestReservationAsync(fixture.Session, peer, 0, 0, default));
            Assert.True(pending.Succeeded);
            Assert.Equal(MarriageCeremonyFailure.Stale, (await RunAsync(fixture,
                x => x.RespondReservationAsync(fixture.Session, peer, 0, default))).Failure);
            Assert.Equal(MarriageCeremonyState.Pending,
                (await fixture.Services.GetRequiredService<IMarriageQueryDomainService>()
                    .ReceivedReservationAsync(peer.CharacterId, default))!.State);
        });
    }
}
