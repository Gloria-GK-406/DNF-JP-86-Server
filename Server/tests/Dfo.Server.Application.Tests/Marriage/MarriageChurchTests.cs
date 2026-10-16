using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.MarriageProposal;
using Microsoft.Extensions.DependencyInjection;

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
        var positions = setup.Fixture.Services.GetRequiredService<ICharacterPositionDomainService>();
        foreach (var actor in new[] { setup.Fixture.Session, setup.Peer })
        {
            var moved = positions.CrossInto(actor, 2, 1, 100, 200);
            areas.TransferExact(actor.CharacterId, actor.TownAreaLease(), new(1, 2, 1), moved, true);
        }
        return setup;
    }
    private static async Task<(LearningFixture F, SelectedSessionIdentity Peer, MarriageProposalTests.Clock Clock)> Reserved()
    {
        var (f, peer, logs, clock) = await Fixture(s =>
        {
            s.AddSingleton<ITownDefinitionSource, ChurchTestTowns>();
            s.AddCommandHandler<PrepareMarriageChurchCommand, MarriageChurchPreparationResult, PrepareMarriageChurchCommandHandler>(
                CommandExecutionKind.Transactional, accountAccessMode: UserAccessMode.Exclusive);
        });
        await MarriageCeremonyTests.MakeEligibleAsync(f, peer, logs);
        Assert.True((await MarriageCeremonyTests.RunAsync(f, x => x.RequestAsync(f.Session, peer, 0, 0, default))).Succeeded);
        Assert.True((await MarriageCeremonyTests.RunAsync(f, x => x.RespondAsync(peer, f.Session, 0, default))).Succeeded);
        return (f, peer, clock);
    }

    private static async Task<T> Run<T>(LearningFixture f, Func<IMarriageChurchDomainService, ValueTask<T>> action, bool commit = true)
    {
        await using var tx = f.Scope.Begin();
        var result = await action(f.Services.GetRequiredService<IMarriageChurchDomainService>());
        if (commit) await tx.CommitAsync(default);
        return result;
    }

    [Fact]
    public async Task Unreserved_characters_cannot_prepare_or_cross_into_any_church_area()
    {
        var (f, peer, _, _) = await Fixture();
        await using var owned = f;
        Assert.Null(await Run(f, x => x.PrepareAsync(f.Session, default)));
        Assert.False(await Run(f, x => x.CanCrossAsync(peer, 1, 2, 12, default)));
        Assert.False(await Run(f, x => x.CanCrossAsync(peer, 1, 2, 63, default)));
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM marriage_church_rooms"));
    }

    [Fact]
    public async Task Two_reserved_couples_get_distinct_rooms_and_cannot_cross_into_each_others_room()
    {
        var (f, peer, logs, _) = await Fixture();
        await using var owned = f;
        await MarriageCeremonyTests.MakeEligibleAsync(f, peer, logs);
        await MarriageCeremonyTests.RunAsync(f, x => x.RequestAsync(f.Session, peer, 0, 0, default));
        await MarriageCeremonyTests.RunAsync(f, x => x.RespondAsync(peer, f.Session, 0, default));
        var firstRoom = (await Run(f, x => x.PrepareAsync(f.Session, default)))!;
        f.Sql("""
            INSERT INTO accounts(account_id,m_id) VALUES(3,'groom2'),(4,'bride2');
            INSERT INTO characters(character_id,account_id,name,job,grow_type,level,exp,bonus_sp,bonus_tp,skill_tree_index)
            VALUES(73,3,'groom2',0,0,85,0,0,0,-1),(74,4,'bride2',12,0,85,0,0,0,-1);
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
            registry.Publish(session); f.Services.GetRequiredService<ISelectedSessionDomainService>().Select(session);
            var position = new CharacterPositionState(2, 1, 100, 200, 0);
            f.Services.GetRequiredService<ICharacterPositionDomainService>().Place(session, position);
            f.Services.GetRequiredService<ITownAreaDomainService>().Enter(new(session.CharacterId, session.TownAreaLease(), new(1, 2, 1), position, true));
            return session;
        }
        var groom = Publish(3, 73); var bride = Publish(4, 74);
        Assert.Equal(MarriageProposalFailure.None, (await MarriageProposalTests.Run(f, x => x.ProposeAsync(groom, bride, default))).Failure);
        var relation = (await MarriageProposalTests.Run(f, x => x.RespondAsync(bride, groom, 0, default))).Proposal!;
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
        Assert.True((await MarriageCeremonyTests.RunAsync(f, x => x.RequestAsync(groom, bride, 0, 0, default))).Succeeded);
        Assert.True((await MarriageCeremonyTests.RunAsync(f, x => x.RespondAsync(bride, groom, 0, default))).Succeeded);
        var secondRoom = (await Run(f, x => x.PrepareAsync(groom, default)))!;
        Assert.NotEqual(firstRoom.Room.Token, secondRoom.Room.Token);
        Assert.NotEqual(firstRoom.Room.AreaId, secondRoom.Room.AreaId);
        Assert.True((await Run(f, x => x.EnterAsync(f.Session, (uint)peer.CharacterId.Value, firstRoom.Room.AreaId, 0, default))).Succeeded);
        Assert.True((await Run(f, x => x.EnterAsync(groom, (uint)bride.CharacterId.Value, secondRoom.Room.AreaId, 0, default))).Succeeded);
        Assert.False(await Run(f, x => x.CanCrossAsync(groom, 1, 2, firstRoom.Room.AreaId, default)));
        Assert.False(await Run(f, x => x.CanCrossAsync(f.Session, 1, 2, secondRoom.Room.AreaId, default)));
        Assert.Equal(2, f.Scalar("SELECT COUNT(*) FROM marriage_church_rooms"));
    }

    [Fact]
    public async Task Both_partners_share_one_persistent_room_and_preparation_does_not_charge_or_marry()
    {
        var (f, peer, _) = await Reserved(); await using var owned = f;
        var first = (await Run(f, x => x.PrepareAsync(f.Session, default)))!;
        var second = (await Run(f, x => x.PrepareAsync(peer, default)))!;
        Assert.Equal(first.Room, second.Room);
        Assert.Equal(peer.CharacterId, first.Peer); Assert.Equal(f.Session.CharacterId, second.Peer);
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM marriage_church_rooms"));
        Assert.Equal(500000, f.Scalar("SELECT SUM(carried_gold) FROM character_stats"));
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM marriage_proposals WHERE state=1"));
        await using var reopened = new LearningFixture(existing: f.Path);
        var restored = await reopened.Services.GetRequiredService<IMarriageChurchDomainStore>().ReadAsync(first.Room.CeremonyId, default);
        Assert.Equal(first.Room, restored);
    }

    [Fact]
    public async Task Entry_requires_exact_partner_church_area_and_flag_and_admission_is_idempotent()
    {
        var (f, peer, _) = await Reserved(); await using var owned = f;
        var church = (await Run(f, x => x.PrepareAsync(f.Session, default)))!;
        foreach (var (target, area, flag) in new[] {
            ((uint)peer.CharacterId.Value + 100u, (uint)church.Room.AreaId, (byte)0),
            ((uint)peer.CharacterId.Value, 0u, (byte)0),
            ((uint)peer.CharacterId.Value, (uint)church.Room.AreaId + 1, (byte)0),
            ((uint)peer.CharacterId.Value, (uint)church.Room.AreaId, (byte)1) })
            Assert.False((await Run(f, x => x.EnterAsync(f.Session, target, area, flag, default))).Succeeded);
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM marriage_church_admissions"));
        Assert.False(await Run(f, x => x.CanCrossAsync(f.Session, 1, 2, church.Room.AreaId, default)));
        for (var replay = 0; replay < 2; replay++)
            Assert.True((await Run(f, x => x.EnterAsync(f.Session, (uint)peer.CharacterId.Value, church.Room.AreaId, 0, default))).Succeeded);
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM marriage_church_admissions"));
        Assert.Equal(2, f.Scalar("SELECT town_id FROM characters WHERE character_id=71"));
        Assert.Equal(church.Room.AreaId, f.Scalar("SELECT area_id FROM characters WHERE character_id=71"));
        Assert.True(await Run(f, x => x.CanCrossAsync(f.Session, 1, 2, church.Room.AreaId, default)));
        Assert.False(await Run(f, x => x.CanCrossAsync(peer, 1, 2, church.Room.AreaId, default)));
        Assert.False(await Run(f, x => x.CanCrossAsync(f.Session, 2, 2, church.Room.AreaId, default)));
        Assert.False(await Run(f, x => x.CanCrossAsync(f.Session, 1, 2, 13, default)));
        Assert.True(await Run(f, x => x.CanCrossAsync(f.Session, 1, 2, 1, default)));
        var peerView = (await Run(f, x => x.PrepareAsync(peer, default)))!;
        Assert.True((await Run(f, x => x.EnterAsync(peer, (uint)f.Session.CharacterId.Value, church.Room.AreaId, 0, default))).Succeeded);
        Assert.Equal(2, f.Scalar("SELECT COUNT(*) FROM marriage_church_admissions"));
        Assert.Equal(500000, f.Scalar("SELECT SUM(carried_gold) FROM character_stats"));
    }

    [Fact]
    public async Task Rolled_back_preparation_and_entry_leave_no_room_or_access_grant()
    {
        var (f, peer, _) = await Reserved(); await using var owned = f;
        Assert.NotNull(await Run(f, x => x.PrepareAsync(f.Session, default), false));
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM marriage_church_rooms"));
        var church = (await Run(f, x => x.PrepareAsync(f.Session, default)))!;
        var priorTown = f.Scalar("SELECT town_id FROM characters WHERE character_id=71");
        var priorArea = f.Scalar("SELECT area_id FROM characters WHERE character_id=71");
        Assert.True((await Run(f, x => x.EnterAsync(f.Session, (uint)peer.CharacterId.Value, church.Room.AreaId, 0, default), false)).Succeeded);
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM marriage_church_admissions"));
        Assert.Equal(priorTown, f.Scalar("SELECT town_id FROM characters WHERE character_id=71"));
        Assert.Equal(priorArea, f.Scalar("SELECT area_id FROM characters WHERE character_id=71"));
        Assert.False(await Run(f, x => x.CanCrossAsync(f.Session, 1, 2, church.Room.AreaId, default)));
    }

    [Fact]
    public async Task Expired_preparation_can_be_reopened_without_replacing_the_reservation_or_room()
    {
        var (f, peer, clock) = await Reserved(); await using var owned = f;
        var before = (await Run(f, x => x.PrepareAsync(f.Session, default)))!;
        clock.Now = before.Room.ReadyUntil + TimeSpan.FromSeconds(1);
        Assert.False((await Run(f, x => x.EnterAsync(f.Session, (uint)peer.CharacterId.Value, before.Room.AreaId, 0, default))).Succeeded);
        var after = (await Run(f, x => x.PrepareAsync(peer, default)))!;
        Assert.Equal(before.Room.Token, after.Room.Token);
        Assert.Equal(before.Room.CeremonyId, after.Room.CeremonyId);
        Assert.Equal(before.Room.AreaId, after.Room.AreaId);
        Assert.True(after.Room.ReadyUntil > before.Room.ReadyUntil);
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM marriage_ceremonies WHERE state=1"));
    }

    [Fact]
    public async Task A_reserved_payload_bound_to_another_pair_cannot_allocate_or_reuse_a_church()
    {
        var (f, peer, _) = await Reserved(); await using var owned = f;
        var before = (await Run(f, x => x.PrepareAsync(f.Session, default)))!;
        f.Sql("UPDATE marriage_ceremonies SET payload_json=json_set(payload_json,'$.Partner.CharacterId.Value',999) WHERE state=1");
        Assert.Null(await Run(f, x => x.PrepareAsync(f.Session, default)));
        Assert.False((await Run(f, x => x.EnterAsync(f.Session, (uint)peer.CharacterId.Value, before.Room.AreaId, 0, default))).Succeeded);
        Assert.False(await Run(f, x => x.CanCrossAsync(f.Session, 1, 2, before.Room.AreaId, default)));
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM marriage_church_admissions"));
    }

    [Fact]
    public async Task Stale_generation_cannot_use_the_room()
    {
        var (f, peer, _) = await Reserved(); await using var owned = f;
        var church = (await Run(f, x => x.PrepareAsync(f.Session, default)))!;
        var stale = SelectedSessionIdentity.Create(f.Session.AccountId, f.Session.CharacterId,
            f.Session.ConnectionId, f.Session.ConnectionGeneration + 1, f.Session.ListenerPort);
        Assert.Null(await Run(f, x => x.PrepareAsync(stale, default)));
        Assert.False((await Run(f, x => x.EnterAsync(stale, (uint)peer.CharacterId.Value, church.Room.AreaId, 0, default))).Succeeded);
        Assert.False(await Run(f, x => x.CanCrossAsync(stale, 1, 2, church.Room.AreaId, default)));
    }
}
