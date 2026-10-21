using System.Buffers.Binary;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.QuestAcceptance;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;
using static Dfo.Server.Application.Tests.Marriage.MarriageHarness;

namespace Dfo.Server.Application.Tests.Marriage;

/// <summary>Characterization of B-04 (engagement snapshot and 03D0 state) and B-16 (wedding quests).</summary>
public sealed class MarriageEngagementCharacterizationTests
{
    private static byte State(byte[] info) => info[4];

    [Fact]
    public async Task Wedding_info_state_byte_follows_each_relationship_stage()
    {
        await using var w = await ChurchTownAsync(); var f = w.F; var peer = w.Peer;
        async Task<byte[]> Bride() => await WeddingInfoAsync(f, f.Session.AccountId, f.Session.CharacterId);
        async Task<byte[]> Groom() => await WeddingInfoAsync(f, peer.AccountId, peer.CharacterId);
        Assert.Equal(new byte[15], await Bride());
        Assert.True((await InTx(f, () => Couple(f).ProposeAsync(f.Session, peer, default))).Succeeded);
        Assert.Equal(new byte[15], await Bride());
        await InTx(f, () => Couple(f).RespondProposalAsync(peer, f.Session, 1, default));
        Assert.Equal(new byte[15], await Groom());
        await EligibleAsync(w, f.Session, peer);
        var engaged = await Bride();
        Assert.Equal(1, State(engaged));
        Assert.Equal(72u, BinaryPrimitives.ReadUInt32LittleEndian(engaged));
        Assert.Equal(71u, BinaryPrimitives.ReadUInt32LittleEndian(await Groom()));
        Assert.Equal(new byte[10], engaged[5..]);
        Assert.Equal(new byte[15], await WeddingInfoAsync(f, peer.AccountId, f.Session.CharacterId));
        Assert.True((await InTx(f, () => Couple(f).RequestReservationAsync(f.Session, peer, 0, 0, default))).Succeeded);
        Assert.Equal(1, State(await Bride()));
        var ceremony = (await InTx(f, () => Couple(f).RespondReservationAsync(peer, f.Session, 0, default))).Ceremony!;
        Assert.Equal(2, State(await Bride())); Assert.Equal(2, State(await Groom()));
        var entries = await InTx(f, () => Couple(f).OpenWeddingAsync(ceremony, default));
        Assert.Equal(2, State(await Bride()));
        w.Clock.Now = entries[0].Church.Room.ReadyUntil;
        Assert.Equal(MarriageWeddingPhase.Cancelled, (await InTx(f, () => Venue(f).AdvanceAsync(ceremony.Id, default)))!.Wedding.Phase);
        Assert.Equal(1, State(await Bride())); Assert.Equal(1, State(await Groom()));

        f.Sql("UPDATE character_stats SET carried_gold=500000 WHERE character_id=71");
        Assert.True((await InTx(f, () => Couple(f).RequestReservationAsync(f.Session, peer, 0, 0, default))).Succeeded);
        var second = (await InTx(f, () => Couple(f).RespondReservationAsync(peer, f.Session, 0, default))).Ceremony!;
        var room = (await InTx(f, () => Couple(f).OpenWeddingAsync(second, default)))[0].Church.Room;
        Arrive(f, f.Session, room); Arrive(f, peer, room);
        w.Clock.Now = room.ReadyUntil;
        Assert.Equal(MarriageWeddingPhase.Completed, (await InTx(f, () => Venue(f).AdvanceAsync(second.Id, default)))!.Wedding.Phase);
        var married = await Bride();
        Assert.Equal(4, State(married)); Assert.Equal(0, married[5]);
        Assert.Equal(checked((uint)room.ReadyUntil.ToUnixTimeSeconds()), BinaryPrimitives.ReadUInt32LittleEndian(married.AsSpan(6)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(married.AsSpan(10)));
        f.Sql("INSERT INTO marriage_personal_points(relation_id,character_id,points) SELECT id,72,7 FROM marriage_relations WHERE stage=4");
        Assert.Equal(7u, BinaryPrimitives.ReadUInt32LittleEndian((await Groom()).AsSpan(10)));
    }

    [Fact]
    public async Task Engagement_snapshot_profiles_the_partner_and_keeps_reserved_false_once_married()
    {
        await using var w = await ChurchTownAsync(); var f = w.F; var peer = w.Peer;
        var (ceremony, room) = await PreparedAsync(w);
        var snapshot = await Query(f).SnapshotAsync(f.Session.AccountId, f.Session.CharacterId, default);
        Assert.Equal(ceremony.RelationId, snapshot.RelationId);
        Assert.Equal("other"u8.ToArray(), snapshot.Partner!.Name);
        Assert.Equal((byte)0, snapshot.Partner.Job); Assert.Equal(85, snapshot.Partner.Level); Assert.Equal((byte)0, snapshot.Partner.GrowType);
        Assert.Equal(new int[13], snapshot.Partner.Equipment);
        Assert.True(snapshot.IsCeremonyReserved); Assert.False(snapshot.IsMarried); Assert.Null(snapshot.MarriedAt);
        Arrive(f, f.Session, room); Arrive(f, peer, room);
        w.Clock.Now = room.ReadyUntil;
        await InTx(f, () => Venue(f).AdvanceAsync(ceremony.Id, default));
        var married = await Query(f).SnapshotAsync(peer.AccountId, peer.CharacterId, default);
        Assert.True(married.IsMarried); Assert.False(married.IsCeremonyReserved);
        Assert.Equal(1, f.Scalar("SELECT state FROM marriage_ceremonies"));
        Assert.Equal(room.ReadyUntil.ToUnixTimeMilliseconds(), married.MarriedAt!.Value.ToUnixTimeMilliseconds());
        Assert.Equal(MarriageCeremonyFailure.Ineligible, (await InTx(f, () => Couple(f).RequestReservationAsync(f.Session, peer, 0, 0, default))).Failure);
    }

    private sealed class Parameters : IQuestParameterSource
    {
        public QuestParameters Load() => new(Enumerable.Repeat(0, 100).ToArray(), new Dictionary<char, int>(), 100, 100, 100, 100);
    }
    private sealed class Definitions : IQuestDefinitionSource
    {
        private static QuestDefinition Quest(int id, string type, IReadOnlyList<IReadOnlyList<int>> previous) =>
            new(id, "wedding", QuestGrade.Wedding, null, new(30, 99), null, 20013, 20013, [], type, "",
                true, false, null, null, null, null, null, previous, [], [], []);
        private static QuestDefinition Trial => Quest(20737, "condition under clear", [])
            with { ObjectiveSubType = 22, ObjectiveArguments = [-1, -1, 5] };
        private static QuestDefinition Material => Quest(20738, "seeking", [new[] { 20737 }])
            with { SeekingItems = [new(10002610, 1)] };
        public ValueTask<QuestDefinition?> GetAsync(int id, CancellationToken ct) =>
            ValueTask.FromResult<QuestDefinition?>(id == 20737 ? Trial : id == 20738 ? Material : id == 20999 ? Quest(id, "seeking", []) : null);
        public ValueTask<IReadOnlyList<QuestDefinition>> GetAllAsync(CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<QuestDefinition>>([Trial, Material, Quest(20999, "seeking", [])]);
        public ValueTask<IReadOnlyList<TrainingQuestAvailability>> GetTrainingAvailabilityAsync(CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<TrainingQuestAvailability>>([]);
    }
    private sealed class Dungeons : IMarriageDungeonContentSource
    {
        public ValueTask<bool> IsOrdinaryMapAsync(int dungeon, int map, CancellationToken ct) => ValueTask.FromResult(dungeon == 2 && map == 100);
    }

    internal static async Task<(LearningFixture F, SelectedSessionIdentity Peer)> QuestWorldAsync()
    {
        var (f, peer) = await MarriageProposalTests.Fixture(configure: s =>
        {
            s.AddSingleton<IQuestDefinitionSource, Definitions>();
            s.AddSingleton<IQuestParameterSource, Parameters>();
            s.AddSingleton<IMarriageDungeonContentSource, Dungeons>();
            s.AddCommandHandler<AcceptQuestCommand, QuestAcceptOutcome, AcceptQuestCommandHandler>(CommandExecutionKind.Transactional);
        });
        return (f, peer);
    }

    internal static async Task AcceptTrialAsync(LearningFixture f, int account, int character) =>
        Assert.True((await f.Services.GetRequiredService<ICommandBus>().SendAsync(new AcceptQuestCommand(new(account), new(character), 20737), default)).Accepted);

    [Fact]
    public async Task Quest_eligibility_follows_the_engaged_owner_only()
    {
        var (f, peer) = await QuestWorldAsync(); await using var owned = f;
        Assert.Empty(await WeddingQuests(f).EligibleQuestIdsAsync(new(1), new(71), default));
        Assert.False(await WeddingQuests(f).CanAcceptAsync(new(1), new(71), 20737, default));
        await EngageAsync(f, f.Session, peer);
        Assert.Equal(new[] { 20737, 20738 }, await WeddingQuests(f).EligibleQuestIdsAsync(new(1), new(71), default));
        Assert.Empty(await WeddingQuests(f).EligibleQuestIdsAsync(new(2), new(71), default));
        Assert.True(await WeddingQuests(f).CanAcceptAsync(new(1), new(71), 20737, default));
        Assert.False(await WeddingQuests(f).CanAcceptAsync(new(1), new(71), 20738, default));
        Assert.False(await WeddingQuests(f).CanAcceptAsync(new(1), new(71), 20999, default));
        Assert.False(await WeddingQuests(f).CanAcceptAsync(new(2), new(71), 20737, default));
        Assert.False(await WeddingQuests(f).CanCompleteAsync(new(1), new(71), 20737, default));
    }

    [Fact]
    public async Task Trial_clears_from_an_empty_run_repeated_or_foreign_participants_or_odd_maps_are_not_counted()
    {
        var (f, peer) = await QuestWorldAsync(); await using var owned = f;
        await EngageAsync(f, f.Session, peer);
        await AcceptTrialAsync(f, 1, 71); await AcceptTrialAsync(f, 2, 72);
        var valid = new DungeonMapCleared(DungeonRunId.New(), 2, DungeonDifficulty.Normal, new(0, 0), 100, 1, DateTimeOffset.UtcNow.AddSeconds(1),
            [new(new(1), new(71), 0, true), new(new(2), new(72), 1, false)]) { IsBossRoom = true };
        var subscriber = TrialClears(f);
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await using (var tx = f.Scope.Begin())
            {
                foreach (var rejected in new[]
                {
                    valid with { RunId = new DungeonRunId(Guid.Empty) },
                    valid with { MapId = 101 },
                    valid with { Participants = [new(new(1), new(71), 0, true), new(new(1), new(71), 1, false)] },
                    valid with { Participants = [new(new(1), new(71), 0, true), new(new(3), new(72), 1, false)] },
                    valid with { Participants = [new(new(3), new(71), 0, true), new(new(2), new(72), 1, false)] },
                })
                    await subscriber.HandleAsync(new(Guid.NewGuid(), Guid.NewGuid(), rejected.ClearedAt, Guid.NewGuid(), rejected), default);
                await tx.CommitAsync(default);
            }
        });
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM marriage_trial_clears"));
        Assert.Equal(10, f.Scalar("SELECT SUM(trigger_value) FROM character_active_quests WHERE quest_id=20737"));
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await using (var tx = f.Scope.Begin())
            {
                await subscriber.HandleAsync(new(Guid.NewGuid(), Guid.NewGuid(), valid.ClearedAt, Guid.NewGuid(), valid), default);
                await tx.CommitAsync(default);
            }
        });
        Assert.Equal(2, f.Scalar("SELECT COUNT(*) FROM marriage_trial_clears"));
        Assert.Equal(8, f.Scalar("SELECT SUM(trigger_value) FROM character_active_quests WHERE quest_id=20737"));
    }
}
