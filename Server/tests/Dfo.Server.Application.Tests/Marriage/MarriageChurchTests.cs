using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Marriage;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Application.Tests.World;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.MarriageProposal;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Tests.Marriage;

internal sealed class ChurchTestTowns : ITownDefinitionSource
{
    private static readonly TownDefinition Town = new(2, "test", [
        new(0, "home.map", TownAreaKind.Gate, 100, 200),
        new(1, "square.map", TownAreaKind.Normal, -1, -1),
        new(12, "Church/Church.map", TownAreaKind.Normal, -1, -1),
        new(13, "Church/Church.map", TownAreaKind.Normal, -1, -1),
        new(63, "Church/Church.map", TownAreaKind.Normal, -1, -1)]);
    public ValueTask<IReadOnlyList<TownDefinition>> GetAllAsync(CancellationToken ct) => ValueTask.FromResult<IReadOnlyList<TownDefinition>>([Town]);
    public ValueTask<TownDefinition?> GetAsync(int id, CancellationToken ct) => ValueTask.FromResult(id == 2 ? Town : null);
    public ValueTask<MapGeometry?> GetMapGeometryAsync(string path, CancellationToken ct) =>
        ValueTask.FromResult<MapGeometry?>(new([], [new(100, 200, 500, 300)]));
}

public sealed class MarriageChurchTests
{
    private static async Task<(LearningFixture Fixture, SelectedSessionIdentity Peer,
        RecordingQuestLogs Logs, MarriageProposalTests.Clock Clock)> Fixture(Action<IServiceCollection>? configure = null)
    {
        var setup = await MarriageCeremonyTests.Fixture(s =>
        {
            s.AddSingleton<ITownDefinitionSource, ChurchTestTowns>();
            configure?.Invoke(s);
        });
        var areas = setup.Fixture.Services.GetRequiredService<ITownAreaDomainService>();
        var positions = setup.Fixture.Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>();
        foreach (var actor in new[] { setup.Fixture.Session, setup.Peer })
        {
            var moved = positions.CrossInto(actor, 2, 1, 100, 200);
            areas.TransferExact(actor, new(1, 2, 1), moved, true);
        }
        return setup;
    }
    private static async Task<(LearningFixture F, SelectedSessionIdentity Peer, MarriageProposalTests.Clock Clock)> Reserved(Action<IServiceCollection>? configure = null)
    {
        var (f, peer, logs, clock) = await Fixture(s =>
        {
            s.AddSingleton<ITownDefinitionSource, ChurchTestTowns>();
            configure?.Invoke(s);
        });
        await MarriageCeremonyTests.MakeEligibleAsync(f, peer, logs);
        Assert.True((await MarriageCeremonyTests.RunAsync(f, x => x.RequestReservationAsync(f.Session, peer, 0, 0, default))).Succeeded);
        Assert.True((await MarriageCeremonyTests.RunAsync(f, x => x.RespondReservationAsync(peer, f.Session, 0, default))).Succeeded);
        return (f, peer, clock);
    }

    private static async Task<T> Run<T>(LearningFixture f, Func<IMarriageVenueDomainService, ValueTask<T>> action, bool commit = true)
    {
        await using var tx = f.Scope.Begin();
        var result = await action(f.Services.GetRequiredService<IMarriageVenueDomainService>());
        if (commit) await tx.CommitAsync(default);
        return result;
    }
    private static async Task<T> Ask<T>(LearningFixture f, Func<IMarriageQueryDomainService, ValueTask<T>> action)
    {
        await using var tx = f.Scope.Begin();
        var result = await action(f.Services.GetRequiredService<IMarriageQueryDomainService>());
        await tx.CommitAsync(default);
        return result;
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Native_cinema_names_are_bride_then_groom_for_either_applicant(bool maleApplicant)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var (f, peer, logs, _) = await Fixture(); await using var owned = f;
            await MarriageCeremonyTests.MakeEligibleAsync(f, peer, logs);
            f.Sql("UPDATE character_stats SET carried_gold=500000 WHERE character_id=72");
            var actor = maleApplicant ? peer : f.Session; var other = maleApplicant ? f.Session : peer;
            Assert.True((await MarriageCeremonyTests.RunAsync(f, x => x.RequestReservationAsync(actor, other, 0, 0, default))).Succeeded);
            Assert.True((await MarriageCeremonyTests.RunAsync(f, x => x.RespondReservationAsync(other, actor, 0, default))).Succeeded);
            var female = (await f.Services.GetRequiredService<ICharacterDomainService>().Get(f.Session.CharacterId).IdentityAsync(default))!;
            var male = (await f.Services.GetRequiredService<ICharacterDomainService>().Get(peer.CharacterId).IdentityAsync(default))!;
            var church = (await Run(f, x => x.PrepareChurchAsync(actor, default)))!;
            Assert.Equal(female.Name, church.ApplicantName); Assert.Equal(male.Name, church.PartnerName);
            var ceremony = (await f.Services.GetRequiredService<IMarriageCeremonyDomainStore>().FindLatestReceivedAsync(other.CharacterId, default))!;
            var entries = await Pair(f, x => x.OpenWeddingAsync(ceremony, default));
            Assert.All(entries, e => { Assert.Equal(female.Name, e.Church.ApplicantName); Assert.Equal(male.Name, e.Church.PartnerName); });
        });
    }
    [Fact]
    public async Task Unreserved_characters_cannot_prepare_or_cross_into_any_church_area()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var (f, peer, _, _) = await Fixture();
            await using var owned = f;
            Assert.Null(await Run(f, x => x.PrepareChurchAsync(f.Session, default)));
            Assert.False(await Ask(f, x => x.CanCrossAsync(peer, 1, 2, 12, default)));
            Assert.False(await Ask(f, x => x.CanCrossAsync(peer, 1, 2, 63, default)));
            Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM marriage_church_rooms"));
        });
    }

    [Fact]
    public async Task Two_reserved_couples_get_distinct_rooms_and_cannot_cross_into_each_others_room()
    {
        await TestGameplay.RunAsync(async () =>
        {
        var (f, peer, logs, _) = await Fixture();
        await using var owned = f;
        await MarriageCeremonyTests.MakeEligibleAsync(f, peer, logs);
        await MarriageCeremonyTests.RunAsync(f, x => x.RequestReservationAsync(f.Session, peer, 0, 0, default));
        await MarriageCeremonyTests.RunAsync(f, x => x.RespondReservationAsync(peer, f.Session, 0, default));
        var firstRoom = (await Run(f, x => x.PrepareChurchAsync(f.Session, default)))!;
        f.Sql("""
            INSERT INTO accounts(account_id,m_id) VALUES(3,'groom2'),(4,'bride2');
            INSERT INTO characters(character_id,account_id,name,job,grow_type,level,exp,bonus_sp,bonus_tp)
            VALUES(73,3,'groom2',0,0,85,0,0,0),(74,4,'bride2',12,0,85,0,0,0);
            UPDATE characters SET exp=(SELECT exp FROM characters WHERE character_id=71) WHERE character_id IN(73,74);
            INSERT INTO character_stats(character_id,carried_gold) VALUES(73,500000),(74,0);
            """);
        SelectedSessionIdentity Publish(int account, int character)
        {
            var registry = f.Services.GetRequiredService<OnlineStateRegistry>();
            var connection = new NetworkConnectionContext(new($"couple2-{account}"), NetworkEndpointKind.Game, 10011);
            var user = registry.RegisterConnection(connection).OnlineUserId;
            registry.Authenticate(user, new(account));
            var session = SelectedSessionIdentity.Create(new(account), new(character), connection.ConnectionId,
                registry.GetRequiredUser(user).Generation, 10011);
            registry.Select(session);
            registry.EnterWorld(session);
            var position = new CharacterPositionState(2, 1, 100, 200, 0);
            f.Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>().Begin(session, position, 100);
            f.Services.GetRequiredService<ITownAreaDomainService>().Enter(new(session, new(1, 2, 1), position, true));
            return session;
        }
        var groom = Publish(3, 73); var bride = Publish(4, 74);
        Assert.Equal(MarriageProposalFailure.None, (await MarriageProposalTests.Run(f, x => x.ProposeAsync(groom, bride, default))).Failure);
        var relation = (await MarriageProposalTests.Run(f, x => x.RespondProposalAsync(bride, groom, 0, default))).Proposal!;
        logs.Cleared[73] = [new ClearedQuest(20737, 1), new ClearedQuest(20738, 1)];
        logs.Cleared[74] = [new ClearedQuest(20737, 1), new ClearedQuest(20738, 1)];
        await using (var tx = f.Scope.Begin())
        {
            var bindings = f.Services.GetRequiredService<IMarriageQuestDomainStore>();
            foreach (var character in new[] { groom.CharacterId, bride.CharacterId })
                foreach (var quest in new[] { 20737, 20738 })
                    await bindings.BindAsync(character, quest, relation.Id, DateTimeOffset.UtcNow, default);
            await tx.CommitAsync(default);
        }
        Assert.True((await MarriageCeremonyTests.RunAsync(f, x => x.RequestReservationAsync(groom, bride, 0, 0, default))).Succeeded);
        Assert.True((await MarriageCeremonyTests.RunAsync(f, x => x.RespondReservationAsync(bride, groom, 0, default))).Succeeded);
        var secondRoom = (await Run(f, x => x.PrepareChurchAsync(groom, default)))!;
        Assert.NotEqual(firstRoom.Room.Token, secondRoom.Room.Token);
        Assert.NotEqual(firstRoom.Room.AreaId, secondRoom.Room.AreaId);
        Assert.True((await Run(f, x => x.EnterChurchAsync(f.Session, (uint)peer.CharacterId.Value, firstRoom.Room.AreaId, 0, default))).Succeeded);
        Assert.True((await Run(f, x => x.EnterChurchAsync(groom, (uint)bride.CharacterId.Value, secondRoom.Room.AreaId, 0, default))).Succeeded);
        Assert.False(await Ask(f, x => x.CanCrossAsync(groom, 1, 2, firstRoom.Room.AreaId, default)));
        Assert.False(await Ask(f, x => x.CanCrossAsync(f.Session, 1, 2, secondRoom.Room.AreaId, default)));
        Assert.Equal(2, f.Scalar("SELECT COUNT(*) FROM marriage_church_rooms"));
        });
    }

    [Fact]
    public async Task Both_partners_share_one_persistent_room_and_preparation_does_not_charge_or_marry()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var (f, peer, _) = await Reserved(); await using var owned = f;
            var first = (await Run(f, x => x.PrepareChurchAsync(f.Session, default)))!;
            var second = (await Run(f, x => x.PrepareChurchAsync(peer, default)))!;
            Assert.Equal(first.Room, second.Room);
            Assert.Equal(peer.CharacterId, first.Peer); Assert.Equal(f.Session.CharacterId, second.Peer);
            Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM marriage_church_rooms"));
            Assert.Equal(500000, f.Scalar("SELECT SUM(carried_gold) FROM character_stats"));
            Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM marriage_proposals WHERE state=1"));
            await using var reopened = new LearningFixture(existing: f.Path);
            var restored = await reopened.Services.GetRequiredService<IMarriageChurchDomainStore>().ReadAsync(first.Room.CeremonyId, default);
            Assert.Equal(first.Room, restored);
        });
    }

    [Fact]
    public async Task Entry_requires_exact_partner_church_area_and_flag_and_admission_is_idempotent()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var (f, peer, _) = await Reserved(); await using var owned = f;
            var church = (await Run(f, x => x.PrepareChurchAsync(f.Session, default)))!;
            foreach (var (target, area, flag) in new[] {
                ((uint)peer.CharacterId.Value + 100u, (uint)church.Room.AreaId, (byte)0),
                ((uint)peer.CharacterId.Value, 0u, (byte)0),
                ((uint)peer.CharacterId.Value, (uint)church.Room.AreaId + 1, (byte)0),
                ((uint)peer.CharacterId.Value, (uint)church.Room.AreaId, (byte)1) })
                Assert.False((await Run(f, x => x.EnterChurchAsync(f.Session, target, area, flag, default))).Succeeded);
            Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM marriage_church_admissions"));
            Assert.False(await Ask(f, x => x.CanCrossAsync(f.Session, 1, 2, church.Room.AreaId, default)));
            for (var replay = 0; replay < 2; replay++)
                Assert.True((await Run(f, x => x.EnterChurchAsync(f.Session, (uint)peer.CharacterId.Value, church.Room.AreaId, 0, default))).Succeeded);
            Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM marriage_church_admissions"));
            Assert.Equal(2, f.Scalar("SELECT town_id FROM characters WHERE character_id=71"));
            Assert.Equal(church.Room.AreaId, f.Scalar("SELECT area_id FROM characters WHERE character_id=71"));
            Assert.True(await Ask(f, x => x.CanCrossAsync(f.Session, 1, 2, church.Room.AreaId, default)));
            Assert.False(await Ask(f, x => x.CanCrossAsync(peer, 1, 2, church.Room.AreaId, default)));
            Assert.False(await Ask(f, x => x.CanCrossAsync(f.Session, 2, 2, church.Room.AreaId, default)));
            Assert.False(await Ask(f, x => x.CanCrossAsync(f.Session, 1, 2, 13, default)));
            Assert.True(await Ask(f, x => x.CanCrossAsync(f.Session, 1, 2, 1, default)));
            var peerView = (await Run(f, x => x.PrepareChurchAsync(peer, default)))!;
            Assert.True((await Run(f, x => x.EnterChurchAsync(peer, (uint)f.Session.CharacterId.Value, church.Room.AreaId, 0, default))).Succeeded);
            Assert.Equal(2, f.Scalar("SELECT COUNT(*) FROM marriage_church_admissions"));
            Assert.Equal(500000, f.Scalar("SELECT SUM(carried_gold) FROM character_stats"));
        });
    }

    [Fact]
    public async Task Rolled_back_preparation_and_entry_leave_no_room_or_access_grant()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var (f, peer, _) = await Reserved(); await using var owned = f;
            Assert.NotNull(await Run(f, x => x.PrepareChurchAsync(f.Session, default), false));
            Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM marriage_church_rooms"));
            var church = (await Run(f, x => x.PrepareChurchAsync(f.Session, default)))!;
            var priorTown = f.Scalar("SELECT town_id FROM characters WHERE character_id=71");
            var priorArea = f.Scalar("SELECT area_id FROM characters WHERE character_id=71");
            Assert.True((await Run(f, x => x.EnterChurchAsync(f.Session, (uint)peer.CharacterId.Value, church.Room.AreaId, 0, default), false)).Succeeded);
            Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM marriage_church_admissions"));
            Assert.Equal(priorTown, f.Scalar("SELECT town_id FROM characters WHERE character_id=71"));
            Assert.Equal(priorArea, f.Scalar("SELECT area_id FROM characters WHERE character_id=71"));
            Assert.False(await Ask(f, x => x.CanCrossAsync(f.Session, 1, 2, church.Room.AreaId, default)));
        });
    }

    [Fact]
    public async Task Expired_preparation_does_not_extend_when_partner_relogs()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var (f, peer, clock) = await Reserved(); await using var owned = f;
            var before = (await Run(f, x => x.PrepareChurchAsync(f.Session, default)))!;
            clock.Now = before.Room.ReadyUntil + TimeSpan.FromSeconds(1);
            Assert.False((await Run(f, x => x.EnterChurchAsync(f.Session, (uint)peer.CharacterId.Value, before.Room.AreaId, 0, default))).Succeeded);
            Assert.Null(await Run(f, x => x.PrepareChurchAsync(peer, default)));
            var after = await f.Services.GetRequiredService<IMarriageChurchDomainStore>().ReadAsync(before.Room.CeremonyId, default);
            Assert.Equal(before.Room, after);
            Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM marriage_ceremonies WHERE state=1"));
        });
    }

    [Fact]
    public async Task A_reserved_payload_bound_to_another_pair_cannot_allocate_or_reuse_a_church()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var (f, peer, _) = await Reserved(); await using var owned = f;
            var before = (await Run(f, x => x.PrepareChurchAsync(f.Session, default)))!;
            f.Sql("UPDATE marriage_ceremonies SET payload_json=json_set(payload_json,'$.Partner.CharacterId.Value',999) WHERE state=1");
            Assert.Null(await Run(f, x => x.PrepareChurchAsync(f.Session, default)));
            Assert.False((await Run(f, x => x.EnterChurchAsync(f.Session, (uint)peer.CharacterId.Value, before.Room.AreaId, 0, default))).Succeeded);
            Assert.False(await Ask(f, x => x.CanCrossAsync(f.Session, 1, 2, before.Room.AreaId, default)));
            Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM marriage_church_admissions"));
        });
    }

    [Fact]
    public async Task Stale_generation_cannot_use_the_room()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var (f, peer, _) = await Reserved(); await using var owned = f;
            var church = (await Run(f, x => x.PrepareChurchAsync(f.Session, default)))!;
            var stale = SelectedSessionIdentity.Create(f.Session.AccountId, f.Session.CharacterId,
                f.Session.ConnectionId, f.Session.ConnectionGeneration + 1, f.Session.ListenerPort);
            Assert.Null(await Run(f, x => x.PrepareChurchAsync(stale, default)));
            Assert.False((await Run(f, x => x.EnterChurchAsync(stale, (uint)peer.CharacterId.Value, church.Room.AreaId, 0, default))).Succeeded);
            Assert.False(await Ask(f, x => x.CanCrossAsync(stale, 1, 2, church.Room.AreaId, default)));
        });
    }
    [Fact]
    public async Task Wedding_preparation_charges_once_and_admits_both_in_one_transaction()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var (f, peer, _) = await Reserved(); await using var owned = f;
            var ceremony = (await f.Services.GetRequiredService<IMarriageCeremonyDomainStore>()
                .FindLatestReceivedAsync(peer.CharacterId, default))!;
            var flow = f.Services.GetRequiredService<IMarriageCoupleDomainService>();
            await using (var tx = f.Scope.Begin())
            {
                var entries = await flow.OpenWeddingAsync(ceremony, default);
                Assert.Equal(2, entries.Count);
                await tx.CommitAsync(default);
            }
            Assert.Equal(0, f.Scalar("SELECT carried_gold FROM character_stats WHERE character_id=71"));
            Assert.Equal(2, f.Scalar("SELECT COUNT(*) FROM marriage_church_admissions"));
            Assert.Equal(f.Scalar("SELECT area_id FROM characters WHERE character_id=71"), f.Scalar("SELECT area_id FROM characters WHERE character_id=72"));
            await using (var tx = f.Scope.Begin())
            {
                Assert.Empty(await flow.OpenWeddingAsync(ceremony, default));
                await tx.CommitAsync(default);
            }
            Assert.Equal(0, f.Scalar("SELECT carried_gold FROM character_stats WHERE character_id=71"));
        });
    }

    private static async Task<T> Flow<T>(LearningFixture f, Func<IMarriageVenueDomainService, ValueTask<T>> action, bool commit = true)
    {
        await using var tx = f.Scope.Begin();
        var result = await action(f.Services.GetRequiredService<IMarriageVenueDomainService>());
        if (commit) await tx.CommitAsync(default);
        return result;
    }
    private static async Task<T> Pair<T>(LearningFixture f, Func<IMarriageCoupleDomainService, ValueTask<T>> action, bool commit = true)
    {
        await using var tx = f.Scope.Begin();
        var result = await action(f.Services.GetRequiredService<IMarriageCoupleDomainService>());
        if (commit) await tx.CommitAsync(default);
        return result;
    }
    private static void Arrive(LearningFixture f, MarriageWeddingAdmission entry)
    {
        var actor = entry.Actor; var room = entry.Church.Room;
        var moved = f.Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>().CrossInto(actor, room.TownId, room.AreaId, 300, 350);
        f.Services.GetRequiredService<ITownAreaDomainService>().TransferExact(actor, new(room.ChannelId, room.TownId, room.AreaId), moved, true);
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Opening_time_commits_marriage_only_with_both_present_and_settlement_is_once(bool both)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var (f, peer, clock) = await Reserved(); await using var owned = f;
            var ceremony = (await f.Services.GetRequiredService<IMarriageCeremonyDomainStore>().FindLatestReceivedAsync(peer.CharacterId, default))!;
            var entries = await Pair(f, x => x.OpenWeddingAsync(ceremony, default));
            Arrive(f, entries[0]); if (both) Arrive(f, entries[1]);
            Assert.Null(await Flow(f, x => x.AdvanceAsync(ceremony.Id, default)));
            clock.Now = entries[0].Church.Room.ReadyUntil;
            if (both)
            {
                var priorX = f.Scalar("SELECT pos_x FROM characters WHERE character_id=71");
                Assert.NotNull(await Flow(f, x => x.AdvanceAsync(ceremony.Id, default), false));
                Assert.Equal(0, f.Scalar("SELECT phase FROM marriage_weddings"));
                Assert.Equal(0, f.Scalar("SELECT completed_at FROM marriage_weddings"));
                Assert.Equal(priorX, f.Scalar("SELECT pos_x FROM characters WHERE character_id=71"));
            }
            var result = (await Flow(f, x => x.AdvanceAsync(ceremony.Id, default)))!;
            Assert.Equal(both ? MarriageWeddingPhase.Completed : MarriageWeddingPhase.Cancelled, result.Wedding.Phase);
            Assert.Equal(both, (await f.Services.GetRequiredService<IMarriageQueryDomainService>().SnapshotAsync(f.Session.AccountId, f.Session.CharacterId, default)).IsMarried);
            if (both)
            {
                Assert.Equal(clock.Now, (await f.Services.GetRequiredService<IMarriageQueryDomainService>().SnapshotAsync(f.Session.AccountId, f.Session.CharacterId, default)).MarriedAt);
                Assert.Equal(2, result.Standing.Count);
                var bride = result.Standing.Single(x => x.Actor.CharacterId == f.Session.CharacterId).Position;
                var groom = result.Standing.Single(x => x.Actor.CharacterId == peer.CharacterId).Position;
                Assert.Equal((670, 330), ((int)bride.X, (int)bride.Y)); Assert.Equal((685, 360), ((int)groom.X, (int)groom.Y));
                Assert.Equal(670, f.Scalar("SELECT pos_x FROM characters WHERE character_id=71"));
            }
            else Assert.Empty(result.Standing);

            Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM marriage_proposals WHERE state=1"));
            Assert.Equal(both ? 0 : 500000, f.Scalar("SELECT COALESCE(SUM(a.gold_amount),0) FROM mailbox_messages m JOIN mailbox_attachments a ON a.message_id=m.message_id AND a.asset_kind='gold' WHERE title='婚礼退款'"));
            Assert.Equal(both ? 0 : 1, result.Departures.Count);
            Assert.Equal(both ? 0 : 1, result.Cleanup.Count);
            var retry = await Flow(f, x => x.AdvanceAsync(ceremony.Id, default));
            if (both) Assert.Null(retry);
            else { Assert.NotNull(retry); Assert.Empty(retry.Participants); Assert.Single(retry.Departures); Assert.Single(retry.Cleanup); }
            Assert.Equal(both ? 0 : 500000, f.Scalar("SELECT COALESCE(SUM(a.gold_amount),0) FROM mailbox_messages m JOIN mailbox_attachments a ON a.message_id=m.message_id AND a.asset_kind='gold' WHERE title='婚礼退款'"));
            if (!both)
            {
                Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM marriage_church_rooms"));
                var moved = f.Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>().CrossInto(f.Session, 2, 1, 100, 200);
                f.Services.GetRequiredService<ITownAreaDomainService>().TransferExact(f.Session, new(1, 2, 1), moved, true);
                var cleaned = (await Flow(f, x => x.AdvanceAsync(ceremony.Id, default)))!;
                Assert.Empty(cleaned.Departures); Assert.Equal(2, cleaned.Cleanup.Count);
                Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM marriage_church_rooms"));
                // Failed delivery leaves a durable queue even after the room is recycled.
                Assert.Equal(2, (await Flow(f, x => x.AdvanceAsync(ceremony.Id, default)))!.Cleanup.Count);
                foreach (var item in cleaned.Cleanup)
                    await Flow(f, async x => { await x.CompleteCleanupAsync(ceremony.Id, item.Actor.CharacterId, default); return true; });
                Assert.Null(await Flow(f, x => x.AdvanceAsync(ceremony.Id, default)));
            }
            await using var restored = new LearningFixture(existing: f.Path);
            Assert.Equal(both, await restored.Services.GetRequiredService<IMarriageQueryDomainService>().IsMarriedAsync(ceremony.RelationId, default));
            if (both) Assert.Equal(clock.Now.ToUnixTimeMilliseconds(), (await restored.Services.GetRequiredService<CoupleLedger>().ReadAsync(ceremony.RelationId, default))!.MarriedAt!.Value.ToUnixTimeMilliseconds());
        });
    }
    [Fact]
    public async Task Preparation_rollback_restores_fee_room_admission_and_positions()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var (f, peer, _) = await Reserved(); await using var owned = f;
            var ceremony = (await f.Services.GetRequiredService<IMarriageCeremonyDomainStore>().FindLatestReceivedAsync(peer.CharacterId, default))!;
            var priorArea = f.Scalar("SELECT area_id FROM characters WHERE character_id=71");
            Assert.Equal(2, (await Pair(f, x => x.OpenWeddingAsync(ceremony, default), false)).Count);
            Assert.Equal(500000, f.Scalar("SELECT carried_gold FROM character_stats WHERE character_id=71"));
            Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM marriage_weddings"));
            Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM marriage_church_admissions"));
            Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM marriage_church_rooms"));
            Assert.Equal(priorArea, f.Scalar("SELECT area_id FROM characters WHERE character_id=71"));
        });
    }
    [Fact]
    public async Task Invitations_and_gifts_are_durable_idempotent_and_end_before_opening()
    {
        await TestGameplay.RunAsync(async () =>
        {
        var (f, peer, clock) = await Reserved(); await using var owned = f;
        var ceremony = (await f.Services.GetRequiredService<IMarriageCeremonyDomainStore>().FindLatestReceivedAsync(peer.CharacterId, default))!;
        var entries = await Pair(f, x => x.OpenWeddingAsync(ceremony, default));
        var guest = SeatGuest(f);
        Assert.Equal((ushort)10, await f.Services.GetRequiredService<IMarriageQueryDomainService>().RemainingInvitationsAsync(ceremony.Id, f.Session.CharacterId, default));
        var sent = await Flow(f, x => x.InviteAsync(f.Session, 0, 73, default));
        Assert.Equal(0, sent.Status); Assert.Equal((ushort)9, sent.Remaining);
        var key = Assert.Single(sent.Letters).Key;
        Assert.NotEqual(0, (await Flow(f, x => x.InviteAsync(peer, 0, 73, default))).Status);
        Assert.Equal(-6, (await Flow(f, x => x.EnterChurchAsync(guest, key, entries[0].Church.Room.AreaId, 1, default))).Status);
        Assert.Equal(0, await Flow(f, x => x.GiftAsync(guest, key, 10000, default)));
        Assert.Equal(0, await Flow(f, x => x.GiftAsync(guest, key, 10000, default)));
        Assert.Equal(40000, f.Scalar("SELECT carried_gold FROM character_stats WHERE character_id=73"));
        Assert.True((await Flow(f, x => x.EnterChurchAsync(guest, key, entries[0].Church.Room.AreaId, 1, default))).Succeeded);
        var comments = f.Services.GetRequiredService<IMarriageVenueDomainService>();
        async Task<T> Comment<T>(Func<IMarriageVenueDomainService, ValueTask<T>> action)
        { await using var tx = f.Scope.Begin(); var value = await action(comments); await tx.CommitAsync(default); return value; }
        Assert.NotEqual(0, await Comment(x => x.PostGuestbookAsync(guest, key, "good"u8.ToArray(), default))); // Not physically inside yet.
        Arrive(f, new(guest, entries[0].Church));
        Assert.Equal(0, await Comment(x => x.PostGuestbookAsync(guest, key, new byte[] { 0xD0, 0xC2, 0xBB, 0xE9 }, default)));
        Assert.NotEqual(0, await Comment(x => x.PostGuestbookAsync(guest, key, new byte[21], default)));
        Assert.NotEqual(0, await Comment(x => x.PostGuestbookAsync(peer, checked((uint)f.Session.CharacterId.Value), "good"u8.ToArray(), default)));
        var written = Assert.Single((await comments.ReadGuestbookAsync(f.Session, key, default)).Comments);
        Assert.Equal(guest.CharacterId, written.Author);
        Assert.NotEqual(0, await Comment(x => x.DeleteGuestbookAsync(guest, key, written.Id, default)));
        Assert.NotEqual(0, await Comment(x => x.DeleteGuestbookAsync(f.Session, 123456, written.Id, default)));
        await using (var restored = new LearningFixture(existing: f.Path))
            Assert.Equal(written.Text, Assert.Single(await restored.Services.GetRequiredService<IMarriageGuestbookDomainStore>().ReadAsync(ceremony.RelationId, default)).Text);
        Assert.Equal(0, await Comment(x => x.DeleteGuestbookAsync(peer, checked((uint)f.Session.CharacterId.Value), written.Id, default)));
        Assert.Empty((await comments.ReadGuestbookAsync(f.Session, key, default)).Comments);
        clock.Now = entries[0].Church.Room.ReadyUntil.AddSeconds(-60);
        Assert.NotEqual(0, (await Flow(f, x => x.InviteAsync(f.Session, 1, 0, default))).Status);
        clock.Now = entries[0].Church.Room.ReadyUntil.AddSeconds(-59);
        Assert.NotEqual(0, (await Flow(f, x => x.InviteAsync(f.Session, 1, 0, default))).Status);
        clock.Now = entries[0].Church.Room.ReadyUntil;
        Assert.Equal(MarriageWeddingPhase.Cancelled, (await Flow(f, x => x.AdvanceAsync(ceremony.Id, default)))!.Wedding.Phase);
        Assert.Equal(10000, f.Scalar("SELECT COALESCE(SUM(a.gold_amount),0) FROM mailbox_messages m JOIN mailbox_attachments a ON a.message_id=m.message_id AND a.asset_kind='gold' WHERE receiver_character_id=73"));
        });
    }

    [Fact]
    public async Task Cancellation_refunds_everyone_still_there_and_skips_a_guest_whose_character_was_deleted()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var (f, peer, clock) = await Reserved(); await using var owned = f;
            var ceremony = (await f.Services.GetRequiredService<IMarriageCeremonyDomainStore>().FindLatestReceivedAsync(peer.CharacterId, default))!;
            var entries = await Pair(f, x => x.OpenWeddingAsync(ceremony, default));
            var guest = SeatGuest(f);
            var key = Assert.Single((await Flow(f, x => x.InviteAsync(f.Session, 0, 73, default))).Letters).Key;
            Assert.Equal(0, await Flow(f, x => x.GiftAsync(guest, key, 10000, default)));
            f.Sql("UPDATE characters SET delete_flag=1 WHERE character_id=73;");
            var applicant = $"SELECT COUNT(*) FROM mailbox_messages WHERE receiver_character_id={ceremony.Applicant.CharacterId.Value}";
            var before = f.Scalar(applicant);
            clock.Now = entries[0].Church.Room.ReadyUntil;
            Assert.Equal(MarriageWeddingPhase.Cancelled, (await Flow(f, x => x.AdvanceAsync(ceremony.Id, default)))!.Wedding.Phase);
            Assert.Equal(before + 1, f.Scalar(applicant));
            Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM mailbox_messages WHERE receiver_character_id=73"));
        });
    }

    /// <summary>A third account's character, online in town with 50000 gold to give.</summary>
    private static SelectedSessionIdentity SeatGuest(LearningFixture f)
    {
        f.Sql("""
            INSERT INTO accounts(account_id,m_id) VALUES(3,'guest');
            INSERT INTO characters(character_id,account_id,name,job,grow_type,level,exp,bonus_sp,bonus_tp)
            VALUES(73,3,'guest',0,0,85,0,0,0);
            INSERT INTO character_stats(character_id,carried_gold) VALUES(73,50000);
            """);
        var registry = f.Services.GetRequiredService<OnlineStateRegistry>();
        var connection = new NetworkConnectionContext(new("wedding-guest"), NetworkEndpointKind.Game, 10011);
        var user = registry.RegisterConnection(connection).OnlineUserId; registry.Authenticate(user, new(3));
        var guest = SelectedSessionIdentity.Create(new(3), new(73), connection.ConnectionId, registry.GetRequiredUser(user).Generation, 10011);
        registry.Select(guest);
        registry.EnterWorld(guest);
        var position = new CharacterPositionState(2, 1, 100, 200, 0);
        f.Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>().Begin(guest, position, 100);
        f.Services.GetRequiredService<ITownAreaDomainService>().Enter(new(guest, new(1, 2, 1), position, true));
        return guest;
    }

    [SkippableTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Current_PVF_wedding_completion_delivers_one_real_box_per_spouse_once(bool failSecond)
    {
        await TestGameplay.RunAsync(async () =>
        {
            Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
            var (f, peer, logs, clock) = await Fixture(services =>
            {
                services.Remove(services.Last(x => x.ServiceType == typeof(IMarriageCeremonyContentSource)));
                if (failSecond) services.AddSingleton<IItemDeliveryDomainService>(provider =>
                    new FailSecondDelivery(ActivatorUtilities.CreateInstance<ItemDeliveryDomainService>(provider)));
            });
            await using var owned = f;
            await MarriageCeremonyTests.MakeEligibleAsync(f, peer, logs);
            await MarriageCeremonyTests.RunAsync(f, x => x.RequestReservationAsync(f.Session, peer, 0, 0, default));
            var reserved = await MarriageCeremonyTests.RunAsync(f, x => x.RespondReservationAsync(peer, f.Session, 0, default));
            var entries = await Pair(f, x => x.OpenWeddingAsync(reserved.Ceremony!, default));
            foreach (var entry in entries) Arrive(f, entry);
            clock.Now = entries[0].Church.Room.ReadyUntil;
            if (failSecond)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => Flow(f, x => x.AdvanceAsync(reserved.Ceremony!.Id, default)));
                Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM mailbox_attachments WHERE item_template_id=10002629"));
                Assert.Equal(0, f.Scalar("SELECT phase FROM marriage_weddings"));
                Assert.False(await f.Services.GetRequiredService<IMarriageQueryDomainService>().IsMarriedAsync(reserved.Ceremony!.RelationId, default));
            }
            Assert.Equal(MarriageWeddingPhase.Completed, (await Flow(f, x => x.AdvanceAsync(reserved.Ceremony!.Id, default)))!.Wedding.Phase);
            Assert.Equal(2, f.Scalar("SELECT COUNT(*) FROM mailbox_attachments WHERE item_template_id=10002629 AND item_count=1"));
            Assert.Null(await Flow(f, x => x.AdvanceAsync(reserved.Ceremony!.Id, default)));
            Assert.Equal(2, f.Scalar("SELECT COUNT(*) FROM mailbox_attachments WHERE item_template_id=10002629 AND item_count=1"));
        });
    }

    [Fact]
    public async Task Cancellation_moves_offline_admitted_character_out_of_persisted_church()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var (f, peer, clock) = await Reserved(); await using var owned = f;
            var ceremony = (await f.Services.GetRequiredService<IMarriageCeremonyDomainStore>().FindLatestReceivedAsync(peer.CharacterId, default))!;
            var entries = await Pair(f, x => x.OpenWeddingAsync(ceremony, default));
            foreach (var entry in entries) Arrive(f, entry);
            Assert.Equal(entries[0].Church.Room.AreaId, f.Scalar("SELECT area_id FROM characters WHERE character_id=72"));
            f.Services.GetRequiredService<ISelectedSessionDomainService>().ReleaseExact(peer);
            f.Services.GetRequiredService<ITownAreaDomainService>().LeaveExact(peer, TownAreaDepartureReason.SessionEnded);
            clock.Now = entries[0].Church.Room.ReadyUntil;
            Assert.Equal(MarriageWeddingPhase.Cancelled, (await Flow(f, x => x.AdvanceAsync(ceremony.Id, default)))!.Wedding.Phase);
            Assert.Equal(2, f.Scalar("SELECT area_id FROM characters WHERE character_id=72"));
            Assert.Equal(2, f.Scalar("SELECT COUNT(*) FROM marriage_wedding_cleanup"));
        });
    }

    private sealed class LoveRules : IMarriageLovePointRuleService, IItemPlacementDefinitionSource
    {
        public ValueTask<MarriageLovePointItemRule?> RuleForAsync(int item, CancellationToken ct) => ValueTask.FromResult<MarriageLovePointItemRule?>(item == 10002901
            ? new(new(true, 0, 0, 1, ItemJobRestriction.Any, ItemPlaceRestriction.Anywhere, Action: "[love point coupon]"), 1, 999) : null);
        public ValueTask<ItemPlacement?> GetAsync(int item, CancellationToken ct) => ValueTask.FromResult<ItemPlacement?>(new(2, 100, new(Dfo.Server.Data.Inventory.BusinessInventorySpace.Consumable, 0, 55)));
    }
    [Fact]
    public async Task Love_coupon_requires_marriage_consumes_once_updates_personal_points_and_rolls_back()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var (f, peer, clock) = await Reserved(s => { s.AddSingleton<IMarriageLovePointRuleService, LoveRules>(); s.AddSingleton<IItemPlacementDefinitionSource, LoveRules>(); });
            await using var owned = f;
            foreach (var actor in new[] { f.Session, peer })
                f.Services.GetRequiredService<ICharacterContainerDomainService>().Register(actor.CharacterId, new PersistentItemContainer(
                    ContainerId.ForCharacter(actor.CharacterId, Dfo.Server.Data.Inventory.InventoryContainerType.Main), new(Dfo.Server.Data.Inventory.InventoryContainerType.Main, 0),
                    f.Services.GetRequiredService<IItemDomainStore>(), f.Services.GetRequiredService<Dfo.Server.Abstractions.Transactions.IDomainTransactionAccessor>(),
                    f.Services.GetRequiredService<IItemPlacementRuleService>(), f.Services.GetRequiredService<IItemSlotRuleService>()));
            await using (var tx = f.Scope.Begin())
            { await f.Services.GetRequiredService<IItemDeliveryDomainService>().DeliverAsync(f.Session.AccountId, f.Session.CharacterId, [new(10002901, 3)], "love-test", default, ItemDeliveryMode.InventoryOnly); await tx.CommitAsync(default); }
            var delivered = (await f.Services.GetRequiredService<ICharacterContainerDomainService>().GetContainer(f.Session.CharacterId, Dfo.Server.Data.Inventory.InventoryContainerType.Main).GetAllAsync(default)).Single(x => x.ItemId == 10002901);
            var source = A21InventoryLayout.ToBusinessSlot(new(Dfo.Server.Data.Inventory.InventoryContainerType.Main, delivered.StorageSlotIndex));
            var love = f.Services.GetRequiredService<IMarriageCoupleDomainService>();
            var accounts = await f.Services.GetRequiredService<IMarriageQueryDomainService>().CoupleAccountsAsync(f.Session, default);
            async Task<MarriageLovePointResult> Use(bool commit = true, IReadOnlyList<AccountId>? expected = null)
            {
                await using var tx = f.Scope.Begin(); var result = await TestEffectFootprints.Run(f.Session, cap => love.UseLovePointAsync(f.Session, source, expected ?? accounts, cap, default));
                if (commit) await tx.CommitAsync(default); return result;
            }
            Assert.NotEqual(0, (await Use()).Status);
            var ceremony = (await f.Services.GetRequiredService<IMarriageCeremonyDomainStore>().FindLatestReceivedAsync(peer.CharacterId, default))!;
            var entries = await Pair(f, x => x.OpenWeddingAsync(ceremony, default)); foreach (var entry in entries) Arrive(f, entry);
            clock.Now = entries[0].Church.Room.ReadyUntil; await Flow(f, x => x.AdvanceAsync(ceremony.Id, default));
            Assert.Equal(0, (await Use(false)).Status);
            Assert.Equal(3, f.Scalar("SELECT SUM(count) FROM item_slots WHERE owner_id=71 AND list_type=0"));
            Assert.Equal(0, (await Use()).Status);
            Assert.Equal(0, (await f.Services.GetRequiredService<IMarriageQueryDomainService>().SnapshotAsync(peer.AccountId, peer.CharacterId, default)).LovePoints);
            Assert.Equal(2, f.Scalar("SELECT SUM(count) FROM item_slots WHERE owner_id=71 AND list_type=0"));
            Assert.NotEqual(0, (await Use(expected: [f.Session.AccountId])).Status);
            f.Sql("UPDATE marriage_personal_points SET points=999 WHERE character_id=71");
            Assert.NotEqual(0, (await Use()).Status); Assert.Equal(2, f.Scalar("SELECT SUM(count) FROM item_slots WHERE owner_id=71 AND list_type=0"));
            await using var restored = new LearningFixture(existing: f.Path);
            Assert.Equal(999, await restored.Services.GetRequiredService<CoupleLedger>().PointsAsync(ceremony.RelationId, f.Session.CharacterId, default));
        });
    }

    [Fact]
    public async Task Final_minute_locks_active_wedding_but_not_completed_departure()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var (f, peer, clock) = await Reserved(); await using var owned = f;
            var ceremony = (await f.Services.GetRequiredService<IMarriageCeremonyDomainStore>().FindLatestReceivedAsync(peer.CharacterId, default))!;
            var entries = await Pair(f, x => x.OpenWeddingAsync(ceremony, default));
            foreach (var entry in entries) Arrive(f, entry);
            var wedding = f.Services.GetRequiredService<IMarriageQueryDomainService>();
            clock.Now = entries[0].Church.Room.ReadyUntil.AddSeconds(-61);
            Assert.True(await wedding.CanLeaveAsync(f.Session, 2, 1, default));
            var outside = f.Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>().CrossInto(f.Session, 2, 1, 100, 200);
            f.Services.GetRequiredService<ITownAreaDomainService>().TransferExact(f.Session, new(1, 2, 1), outside, true);
            Assert.Equal(entries[0].Church.Room.ReadyUntil, (await wedding.AppearanceAsync(f.Session, default))!.Room.ReadyUntil);
            Assert.True(await Ask(f, x => x.CanCrossAsync(f.Session, 1, 2, entries[0].Church.Room.AreaId, default)));
            Arrive(f, entries[0]);
            clock.Now = entries[0].Church.Room.ReadyUntil.AddSeconds(-60);
            Assert.True(await wedding.CanLeaveAsync(f.Session, 2, 1, default));
            clock.Now = entries[0].Church.Room.ReadyUntil.AddSeconds(-59);
            Assert.False(await wedding.CanLeaveAsync(f.Session, 2, 1, default));
            Assert.True(await wedding.CanLeaveAsync(f.Session, 2, entries[0].Church.Room.AreaId, default));
            clock.Now = entries[0].Church.Room.ReadyUntil;
            await Flow(f, x => x.AdvanceAsync(ceremony.Id, default));
            Assert.True(await wedding.CanLeaveAsync(f.Session, 2, 1, default));
        });
    }

    private sealed class FailSecondDelivery(IItemDeliveryDomainService inner) : IItemDeliveryDomainService
    {
        private int calls;
        public ValueTask<long> PendingQuantityAsync(CharacterId character, int itemId, CancellationToken ct) => inner.PendingQuantityAsync(character, itemId, ct);
        public async ValueTask<ItemDeliveryReceipt> DeliverAsync(AccountId account, CharacterId character, IReadOnlyList<ItemDeliveryGrant> grants,
            string source, CancellationToken ct, ItemDeliveryMode mode, ItemDeliveryOptions? options = null)
        {
            var receipt = await inner.DeliverAsync(account, character, grants, source, ct, mode, options);
            if (++calls == 2) throw new InvalidOperationException("Injected failure after second spouse reward.");
            return receipt;
        }
    }

}
