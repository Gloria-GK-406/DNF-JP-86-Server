using System.Buffers.Binary;
using System.Text;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Networking.Tests.Characterization;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using static Dfo.Server.Networking.Tests.Characterization.CharacterizationSteps;

namespace Dfo.Server.Networking.Tests.Integration;

/// <summary>
/// Duel rooms and battles over real TCP on a whole server: the lobby kept equal to a fresh snapshot, a battle from
/// start to settlement, a fighter's disconnect, room invitations and the room refusals.
/// </summary>
/// <remarks>
/// The clock is frozen (<see cref="CharacterizationHost.Now"/>), so no phase deadline ever passes here; the deadline
/// paths are covered by the Application and Protocol tests. The duel deadline scheduler still runs: a departure wakes it
/// through its signal, not through the clock, so departure projections arrive here as in production.
/// </remarks>
[Collection(CharacterizationCollection.Name)]
public sealed class PvpDuelWireTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(300);

    [PvfFact]
    public async Task Colosseum_mixes_with_ordinary_duels_and_credits_only_the_paid_challenge()
    {
        using var timeout = new CancellationTokenSource(Budget);
        var ct = timeout.Token;
        var clock = new RelayTestClock(dayOffset: 10);
        await using var server = await StartAsync(ranked: false, ct, clock);
        using var first = await server.EnterWorldAsync("colo-a", "colosseum1", "ColosseumOne", ct);
        using var second = await server.EnterWorldAsync("colo-b", "colosseum2", "ColosseumTwo", ct);
        var id = new CharacterId(first.WireUserId);
        var owner = new PvpColosseumOwner(PvpColosseumKind.Individual, id.Value);
        var runs = server.Services.GetRequiredService<IPvpColosseumStore>();
        var normal = Convert.FromHexString("0100000000FFFFFFFFFFFFFFFF00");
        var challenge = normal.ToArray();
        challenge[^1] = 1;

        await first.SendAsync(0x020F, [0, 0], ct);
        Assert.Equal(new byte[] { 0, 7 }, (await first.ReadUntilAsync(f => f.Is(1, 0x020F), ct)).Body.ToArray());
        await first.SendAsync(0x0209, challenge, ct);
        Assert.Equal(new byte[] { 0, 0x13 }, (await first.ReadUntilAsync(f => f.Is(1, 0x0209), ct)).Body.ToArray());

        await using (var connection = new SqliteConnection($"Data Source={server.DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO pvp_matches(match_id,room_id,listener_port,channel_env,battle_mode,outcome_kind,winner,
                    records_result,game_day,completed_at)
                VALUES('seed-colosseum-rating',1,44060,8,0,0,0,0,'2026-11-25',1795564800000);
                INSERT INTO pvp_match_results(match_id,character_id,seat,team,outcome,kills,deaths,fled,
                    counts_toward_daily,previous_grade,grade,experience_change,experience,win_streak)
                VALUES('seed-colosseum-rating',@character,0,0,0,0,0,0,0,0,0,0,0,0);
                INSERT INTO pvp_match_rating_results
                    (match_id,character_id,season_id,placement_games,rp_before,rp_after,peak_rp)
                VALUES('seed-colosseum-rating',@character,1,20,1000,1000,1000);
                """;
            command.Parameters.AddWithValue("@character", id.Value);
            await command.ExecuteNonQueryAsync(ct);
        }

        await InWriteAsync(async token =>
        {
            var scope = server.Services.GetRequiredService<DomainTransactionScope>();
            var inventory = server.Services.GetRequiredService<IItemDomainStore>().SessionIn(scope.Current!);
            var item = await inventory.CreateAsync(new ItemSnapshot
            {
                ItemId = 2, LegacyKind = 0, Type = ItemTraits.None, IsPile = true, Count = 200,
            }, token);
            await inventory.PutAsync(ContainerId.ForCharacter(id, InventoryContainerType.Main), 2, item, token);
        });

        for (var attempt = 0; attempt < 2; attempt++)
        {
            await first.SendAsync(0x020F, [0, 0], ct);
            var buy = await first.ReadUntilAsync(f => f.Is(1, 0x020F), ct);
            Assert.Equal(new byte[] { 1, 2, 0, 0, 0, 200, 0, 0, 0 }, buy.Body.ToArray());
        }
        var active = (await runs.ReadAsync(owner, ct))!;
        Assert.Equal(1, await runs.EntriesAsync(owner, active.EntryDay, ct));
        Assert.Equal(0L, await EmblemsAsync());
        var activeNotice = first.Frames.Last(frame => frame.Is(0, 0x0262));
        Assert.Equal(1, activeNotice.Body[4]);

        await InWriteAsync(token => runs.UpdateAsync(active with { Wins = 9 }, token));
        await PlayMatchAsync(normal, null, expectedPower: 1000);
        await ReturnFromResultAsync();
        var afterOrdinary = (await runs.ReadAsync(owner, ct))!;
        Assert.Equal((active.Id, 9, 0, false), (afterOrdinary.Id, afterOrdinary.Wins, afterOrdinary.Losses, afterOrdinary.Complete));
        Assert.Equal(0L, await EmblemsAsync());
        var missions = server.Services.GetRequiredService<IPvpDailyMissionStore>();
        Assert.Equal(0, await missions.ProgressAsync(id, active.EntryDay, 134, ct));

        var challengeStart = Mark(first, second);
        await PlayMatchAsync(challenge, active.Id);
        var resultFrames = After(first, challengeStart).ToList();
        var ownResult = Assert.Single(resultFrames, frame => frame.Is(0, 0x002F));
        var ownContext = resultFrames.Last(frame => frame.Is(0, 0x0262));
        Assert.Equal(10, ownContext.Body[2]);
        Assert.Equal(0, ownContext.Body[3]);
        Assert.True(resultFrames.IndexOf(ownContext) < resultFrames.IndexOf(ownResult));
        var experience = BinaryPrimitives.ReadInt32LittleEndian(ownResult.Body[6..]);
        Assert.True(experience > 0);
        Assert.Equal((long)experience, await ScalarAsync(server,
            $"SELECT experience_change FROM pvp_match_results WHERE character_id={id.Value} ORDER BY rowid DESC LIMIT 1", ct));
        var otherResult = Assert.Single(After(second, challengeStart), frame => frame.Is(0, 0x002F));
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(otherResult.Body[6..]));
        var completed = (await runs.ReadAsync(owner, ct))!;
        Assert.Equal((10, 0, true), (completed.Wins, completed.Losses, completed.Complete));
        Assert.Equal(3, completed.Prizes.Length);
        Assert.Equal(1, await missions.ProgressAsync(id, active.EntryDay, 134, ct));
        Assert.Equal(0, await missions.ProgressAsync(new CharacterId(second.WireUserId), active.EntryDay, 134, ct));

        await ReturnFromResultAsync();
        var rewards = first.Frames.Last(frame => frame.Is(0, 0x0263));
        Assert.Equal(3, BinaryPrimitives.ReadInt32LittleEndian(rewards.Body));
        Assert.DoesNotContain(second.Frames, frame => frame.Is(0, 0x0263));
        await first.SendAsync(0x0210, [], ct);
        await first.SendAsync(0x0210, [], ct);
        await first.FenceAsync(ct);
        Assert.Null(await runs.ReadAsync(owner, ct));
        Assert.Equal((long)completed.Prizes.Single(prize => prize.ItemId == 2).Count, await EmblemsAsync());

        async Task PlayMatchAsync(byte[] request, Guid? challengeId, int? expectedPower = null)
        {
            await second.SendAsync(0x0209, normal, ct);
            await second.FenceAsync(ct);
            await first.SendAsync(0x0209, request, ct);
            var accepted = await first.ReadUntilAsync(f => f.Is(1, 0x0209), ct);
            Assert.Equal(new byte[] { 1, 1, 0, 0, request[^1] }, accepted.Body.ToArray());
            await FenceAllAsync(ct, first, second);
            var room = Room(server, first);
            var a = room.Members.Single(x => x.Member.CharacterId == id).Seat;
            var b = room.Members.Single(x => x.Member.CharacterId != id).Seat;
            if (challengeId is { } runId)
                Assert.Equal(runId, Assert.Single(room.ColosseumRuns).Value);
            else
                Assert.Empty(room.ColosseumRuns);

            var versus = first.Frames.Last(frame => frame.Is(0, 0x01F9));
            Assert.Equal(second.WireUserId, BinaryPrimitives.ReadUInt16LittleEndian(versus.Body[6..]));
            Assert.Equal(first.WireUserId, BinaryPrimitives.ReadUInt16LittleEndian(versus.Body[19..]));
            if (expectedPower is { } power)
            {
                Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(versus.Body[9..]));
                Assert.Equal(power, BinaryPrimitives.ReadInt32LittleEndian(versus.Body[22..]));
            }
            await first.SendAsync(0x023B, versus.Body[1..5].ToArray(), ct);
            await second.SendAsync(0x023B, versus.Body[1..5].ToArray(), ct);
            await FenceAllAsync(ct, first, second);
            await first.SendAsync(0x012A, [], ct);
            await second.SendAsync(0x012A, [], ct);
            await FenceAllAsync(ct, first, second);
            await first.SendAsync(0x012B, LinkReport(b), ct);
            await second.SendAsync(0x012B, LinkReport(a), ct);
            await FenceAllAsync(ct, first, second);
            await second.SendAsync(0x0037, [.. U16(second.WireUserId), .. U16(first.WireUserId), 0], ct);
            await FenceAllAsync(ct, second, first);
            await first.SendAsync(0x003A, [], ct);
            await second.SendAsync(0x003A, [], ct);
            await FenceAllAsync(ct, first, second);
        }

        async Task ReturnFromResultAsync()
        {
            clock.Advance(16, server);
            await FenceAllAsync(ct, first, second);
            await first.SendAsync(0x0039, [], ct);
            await second.SendAsync(0x0039, [], ct);
            await FenceAllAsync(ct, first, second);
        }

        Task<long> EmblemsAsync() => ScalarAsync(server,
            "SELECT COALESCE(SUM(count),0) FROM item_slots WHERE owner_scope='character' AND list_type=0 AND slot_index=2"
            + $" AND owner_id={id.Value}", ct);

        Task InWriteAsync(Func<CancellationToken, ValueTask> action) => TestGameplay.RunAsync(server.Services, async () =>
        {
            var scope = server.Services.GetRequiredService<DomainTransactionScope>();
            await using var transaction = scope.Begin();
            await action(ct);
            await transaction.CommitAsync(ct);
        });

        static byte[] LinkReport(int opponent) =>
            [8, .. Enumerable.Range(0, 8).SelectMany(seat => new byte[] { (byte)seat, seat == opponent ? (byte)1 : (byte)0 })];
    }

    [PvfFact]
    public async Task Ordinary_duel_can_cancel_its_queue_and_then_pair_two_waiting_players()
    {
        using var timeout = new CancellationTokenSource(Budget);
        var ct = timeout.Token;
        // Exercise the real PVF draw on a day that offers both HP objectives and the poses.
        var clock = new RelayTestClock(dayOffset: 10);
        await using var server = await StartAsync(ranked: false, ct, clock);
        using var first = await server.EnterWorldAsync("match-a", "match1", "MatchOne", ct);
        using var second = await server.EnterWorldAsync("match-b", "match2", "MatchTwo", ct);
        var casual = second.Frames.Last(frame => frame.Is(0, 0x0143));
        Assert.Equal(41, casual.Body.Length);
        Assert.Equal(4, casual.Body[0]);
        Assert.Equal(134, BinaryPrimitives.ReadUInt16LittleEndian(casual.Body[31..]));
        Assert.All(Enumerable.Range(0, 4), index =>
            Assert.InRange(BinaryPrimitives.ReadUInt16LittleEndian(casual.Body[(1 + 10 * index)..]), 113, 134));
        var join = Convert.FromHexString("0100000000FFFFFFFFFFFFFFFF00");

        await first.SendAsync(0x0209, join, ct);
        await FenceAllAsync(ct, first, second);
        Assert.Contains(first.Frames, frame => frame.Is(1, 0x0209)
            && frame.Body.SequenceEqual(new byte[] { 1, 1, 0, 0, 0 }));
        Assert.DoesNotContain(first.Frames, frame => frame.Is(0, 0x01FA));
        await first.SendAsync(0x023D, [], ct);
        var count = await first.ReadUntilAsync(frame => frame.Is(0, 0x01FE), ct);
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(count.Body[9..]));

        await first.SendAsync(0x020A, [1, 0, 0], ct);
        await FenceAllAsync(ct, first, second);
        Assert.Contains(first.Frames, frame => frame.Is(1, 0x020A)
            && frame.Body.SequenceEqual(new byte[] { 1 }));

        await second.SendAsync(0x0209, join, ct);
        await FenceAllAsync(ct, second, first);
        Assert.DoesNotContain(second.Frames, frame => frame.Is(0, 0x01FA));

        await first.SendAsync(0x0209, join, ct);
        await FenceAllAsync(ct, first, second);
        var versus = Assert.Single(first.Frames, frame => frame.Is(0, 0x01F9));
        Assert.Equal(32, versus.Body.Length);
        Assert.Equal(2, versus.Body[5]);
        Assert.Equal(second.WireUserId, BinaryPrimitives.ReadUInt16LittleEndian(versus.Body[6..]));
        Assert.Equal(first.WireUserId, BinaryPrimitives.ReadUInt16LittleEndian(versus.Body[19..]));
        Assert.Equal(PvpRoomPhase.Waiting, Room(server, first).Phase);
        Assert.DoesNotContain(first.Frames, frame => frame.Is(0, 0x01FA));
        var roomNumber = versus.Body[1..5].ToArray();
        await second.SendAsync(0x023B, [255, 0, 0, 0], ct);
        await first.SendAsync(0x023B, roomNumber, ct);
        await FenceAllAsync(ct, first, second);
        Assert.DoesNotContain(first.Frames, frame => frame.Is(0, 0x01FB));
        await second.SendAsync(0x023B, roomNumber, ct);
        await FenceAllAsync(ct, second, first);
        Assert.Contains(first.Frames, frame => frame.Is(0, 0x01FB) && frame.Body.SequenceEqual(new byte[] { 2 }));
        await first.SendAsync(0x012A, [], ct);
        await second.SendAsync(0x012A, [], ct);
        await FenceAllAsync(ct, first, second);
        Assert.Equal(2, first.Frames.Count(frame => frame.Is(0, 0x0119)));

        static byte[] LinkReport(byte opponent) =>
            [8, .. Enumerable.Range(0, 8).SelectMany(seat => new byte[] { (byte)seat, seat == opponent ? (byte)1 : (byte)0 })];

        await first.SendAsync(0x012B, LinkReport(1), ct);
        await second.SendAsync(0x012B, LinkReport(0), ct);
        await FenceAllAsync(ct, first, second);
        Assert.DoesNotContain(first.Frames, frame => frame.Is(0, 0x01FA));
        await first.SendAsync(0x012B, LinkReport(0), ct);
        await FenceAllAsync(ct, first, second);
        Assert.DoesNotContain(second.Frames, frame => frame.Is(0, 0x01FA));
        await second.SendAsync(0x012B, LinkReport(1), ct);
        await FenceAllAsync(ct, second, first);
        Assert.Contains(first.Frames, frame => frame.Is(0, 0x01FA));
        Assert.Contains(second.Frames, frame => frame.Is(0, 0x01FA));
        Assert.Contains(first.Frames, frame => frame.Is(0, 0x011A));
        Assert.Contains(second.Frames, frame => frame.Is(0, 0x002D));
        Assert.Equal(PvpRoomPhase.Fighting, Room(server, first).Phase);
        Assert.Equal(Room(server, first).Id, Room(server, second).Id);
        var rooms = server.Services.GetRequiredService<IPvpRoomQueryDomainService>();
        Assert.Empty(rooms.RoomsOn(Room(server, first).ListenerPort));

        await first.SendAsync(0x0178, [2, 116, 0, 117, 0], ct);
        await first.SendAsync(0x0178, [1, 116, 0], ct);
        await first.SendAsync(0x0179, [133, 0, 1], ct);
        await first.SendAsync(0x0179, [133, 0, 1], ct);
        await first.FenceAsync(ct);
        await second.SendAsync(0x0037, [.. U16(second.WireUserId), .. U16(first.WireUserId), 0], ct);
        await FenceAllAsync(ct, second, first);
        var firstSettlement = Mark(first);
        var secondSettlement = Mark(second);
        await second.SendAsync(0x003A, [], ct);
        await first.SendAsync(0x003A, [], ct);
        await FenceAllAsync(ct, first, second);
        foreach (var (client, settlement) in new[] { (first, firstSettlement), (second, secondSettlement) })
        {
            var frames = After(client, settlement).Where(frame => frame.IsNoti && frame.Type is 0x0030 or 0x002F).ToArray();
            Assert.Equal(new[] { 0x0030, 0x002F }, frames.Select(frame => (int)frame.Type));
            Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(frames[0].Body[26..]));
            Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(frames[0].Body[34..]));
        }
        await first.SendAsync(0x003A, [], ct);
        // A21 starts the rating animation near ten seconds and acknowledges after the result sequence.
        Assert.Equal(clock.GetUtcNow().AddSeconds(20), Room(server, first).Match!.PhaseDeadline);
        clock.Advance(16, server);
        await FenceAllAsync(ct, first, second);
        Assert.Equal(PvpRoomPhase.AwaitResult, Room(server, first).Phase);
        var resultExit = Mark(first, second);
        await first.SendAsync(0x0039, [], ct);
        await second.SendAsync(0x0039, [], ct);
        await FenceAllAsync(ct, second, first);
        AssertMatchedRoomRetiredBeforeTown(first, resultExit, seatCount: 2);
        AssertMatchedRoomRetiredBeforeTown(second, resultExit, seatCount: 2);
        var refreshedMissions = first.Frames.Last(frame => frame.Is(0, 0x0143));
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(refreshedMissions.Body[3..]));
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(refreshedMissions.Body[13..]));
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(refreshedMissions.Body[23..]));
        Assert.Equal(1L, await ScalarAsync(server,
            "SELECT value FROM pvp_daily_mission_metrics WHERE mission_id=133 AND metric=1", ct));
        Assert.False(rooms.IsSeated(new CharacterId(first.WireUserId)));
        Assert.False(rooms.IsSeated(new CharacterId(second.WireUserId)));

        await first.SendAsync(0x0209, join, ct);
        await FenceAllAsync(ct, first, second);
        await second.SendAsync(0x0209, join, ct);
        await FenceAllAsync(ct, second, first);
        Assert.NotNull(Room(server, second).Admission);

        clock.Advance(91, server);
        using var admissionTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        admissionTimeout.CancelAfter(TimeSpan.FromSeconds(12));
        await first.ReadUntilAsync(frame => frame.Is(0, 0x01FB) && frame.Body.SequenceEqual(new byte[] { 1 }),
            admissionTimeout.Token);
        Assert.False(rooms.IsSeated(new CharacterId(first.WireUserId)));
        Assert.False(rooms.IsSeated(new CharacterId(second.WireUserId)));
        Assert.Equal(1L, await ScalarAsync(server, "SELECT COUNT(*) FROM pvp_matches", ct));
        Assert.Equal(2L, await ScalarAsync(server, "SELECT COUNT(*) FROM pvp_match_rating_results", ct));
    }

    [PvfFact]
    public async Task Ordinary_duel_honors_both_queued_and_arriving_players_career_filters()
    {
        using var timeout = new CancellationTokenSource(Budget);
        var ct = timeout.Token;
        await using var server = await StartAsync(ranked: false, ct);
        using var first = await server.EnterWorldAsync("filter-a", "filter1", "FilterOne", ct);
        using var swordMaster = await server.EnterWorldAsync("filter-b", "filter2", "FilterTwo", ct,
            () => server.ExecuteSqlAsync("UPDATE characters SET grow_type=33 WHERE name='FilterTwo'", ct));
        var plain = Convert.FromHexString("0100000000FFFFFFFFFFFFFFFF00");
        var filters = Convert.FromHexString("0100000001000101010203FFFF00");

        await first.SendAsync(0x0209, filters, ct);
        await FenceAllAsync(ct, first, swordMaster);
        Assert.Contains(first.Frames, frame => frame.Is(1, 0x0209)
            && frame.Body.SequenceEqual(new byte[] { 1, 1, 0, 0, 0 }));
        await swordMaster.SendAsync(0x0209, plain, ct);
        await FenceAllAsync(ct, swordMaster, first);
        Assert.DoesNotContain(first.Frames, frame => frame.Is(0, 0x01F9));
        Assert.DoesNotContain(swordMaster.Frames, frame => frame.Is(0, 0x01F9));

        await first.SendAsync(0x020A, [1, 0, 0], ct);
        await FenceAllAsync(ct, first, swordMaster);
        await first.SendAsync(0x0209, filters, ct);
        await FenceAllAsync(ct, first, swordMaster);
        Assert.DoesNotContain(first.Frames, frame => frame.Is(0, 0x01F9));
        Assert.DoesNotContain(swordMaster.Frames, frame => frame.Is(0, 0x01F9));
        await first.SendAsync(0x023D, [], ct);
        var count = await first.ReadUntilAsync(frame => frame.Is(0, 0x01FE), ct);
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(count.Body[9..]));

        await first.SendAsync(0x020A, [1, 0, 0], ct);
        await FenceAllAsync(ct, first, swordMaster);
        await first.SendAsync(0x0209, plain, ct);
        await FenceAllAsync(ct, first, swordMaster);
        Assert.Contains(first.Frames, frame => frame.Is(0, 0x01F9));
        Assert.Contains(swordMaster.Frames, frame => frame.Is(0, 0x01F9));
        Assert.Equal(Room(server, first).Id, Room(server, swordMaster).Id);
    }

    [PvfFact]
    public async Task Ordinary_duel_relay_pairs_six_distinct_players_and_rates_the_whole_match_once()
    {
        using var timeout = new CancellationTokenSource(Budget);
        var ct = timeout.Token;
        var clock = new RelayTestClock();
        await using var server = await StartAsync(ranked: false, ct, clock);
        using var first = await server.EnterWorldAsync("relay-a", "relay1", "RelayOne", ct);
        using var second = await server.EnterWorldAsync("relay-b", "relay2", "RelayTwo", ct);
        using var third = await server.EnterWorldAsync("relay-c", "relay3", "RelayThree", ct);
        using var fourth = await server.EnterWorldAsync("relay-d", "relay4", "RelayFour", ct);
        using var fifth = await server.EnterWorldAsync("relay-e", "relay5", "RelayFive", ct);
        using var last = await server.EnterWorldAsync("relay-f", "relay6", "RelaySix", ct);
        WireClient[] players = [first, second, third, fourth, fifth, last];
        var solo = Convert.FromHexString("0100000000FFFFFFFFFFFFFFFF00");
        var relay = Convert.FromHexString("0101000000FFFFFFFFFFFFFFFF00");

        await last.SendAsync(0x0209, solo, ct);
        await last.FenceAsync(ct);
        foreach (var player in players[..5])
        {
            await player.SendAsync(0x0209, relay, ct);
            await player.FenceAsync(ct);
            Assert.Contains(player.Frames, frame => frame.Is(1, 0x0209)
                && frame.Body.SequenceEqual(new byte[] { 1, 1, 1, 0, 0 }));
            Assert.DoesNotContain(player.Frames, frame => frame.Is(0, 0x01F9));
        }
        await last.SendAsync(0x023D, [], ct);
        var count = await last.ReadUntilAsync(frame => frame.Is(0, 0x01FE), ct);
        Assert.Equal(2, count.Body[4]);
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(count.Body[9..]));
        Assert.Equal(5, BinaryPrimitives.ReadInt32LittleEndian(count.Body[17..]));
        await last.SendAsync(0x020A, [1, 0, 0], ct);
        await last.FenceAsync(ct);
        await last.SendAsync(0x0209, relay, ct);
        await last.FenceAsync(ct);

        byte[]? roomNumber = null;
        foreach (var player in players)
        {
            var versus = player == last
                ? Assert.Single(player.Frames, frame => frame.Is(0, 0x01F9))
                : await player.ReadUntilAsync(frame => frame.Is(0, 0x01F9), ct);
            Assert.Equal(6, versus.Body[5]);
            Assert.Equal(84, versus.Body.Length);
            roomNumber ??= versus.Body[1..5].ToArray();
            for (var seat = 0; seat < players.Length; seat++)
            {
                Assert.Equal(players[seat].WireUserId, BinaryPrimitives.ReadUInt16LittleEndian(versus.Body[(6 + 13 * seat)..]));
                Assert.Equal(1 + seat % 2, versus.Body[8 + 13 * seat]);
            }
        }
        foreach (var player in players[..5]) await player.SendAsync(0x023B, roomNumber!, ct);
        await FenceAllAsync(ct, players);
        Assert.DoesNotContain(first.Frames, frame => frame.Is(0, 0x01FB));
        await last.SendAsync(0x023B, roomNumber!, ct);
        await FenceAllAsync(ct, players);
        Assert.Contains(last.Frames, frame => frame.Is(0, 0x01FB) && frame.Body.SequenceEqual(new byte[] { 2 }));
        foreach (var player in players) await player.SendAsync(0x012A, [], ct);
        await FenceAllAsync(ct, players);
        Assert.Equal(6, first.Frames.Count(frame => frame.Is(0, 0x0119)));

        static byte[] Links(int mask) => [8, .. Enumerable.Range(0, 8)
            .SelectMany(seat => new[] { (byte)seat, (byte)((mask >> seat) & 1) })];
        for (var seat = 0; seat < 5; seat++)
            await players[seat].SendAsync(0x012B, Links(0b111111 & ~(1 << seat)), ct);
        await last.SendAsync(0x012B, Links(0b011110), ct);
        await FenceAllAsync(ct, players);
        Assert.NotNull(Room(server, last).Admission);
        Assert.DoesNotContain(first.Frames, frame => frame.Is(0, 0x01FA));
        await last.SendAsync(0x012B, Links(1), ct);
        await FenceAllAsync(ct, players);
        Assert.Contains(last.Frames, frame => frame.Is(0, 0x002D) && frame.Body[1] == 3);
        Assert.Equal(PvpRoomPhase.Fighting, Room(server, last).Phase);

        clock.Advance(7, server);
        foreach (var victimSeat in new[] { 1, 3, 5 })
        {
            foreach (var player in players)
                await player.ReadUntilAsync(frame => frame.Is(0, 0x0070)
                    && frame.Body.SequenceEqual(new byte[] { 2, 0, 0, (byte)victimSeat, 0 }), ct);
            var victim = players[victimSeat];
            await victim.SendAsync(0x0037, [.. U16(victim.WireUserId), .. U16(first.WireUserId), 1], ct);
            await FenceAllAsync(ct, victim, first);
            if (victimSeat != 5)
            {
                Assert.Equal(PvpRoomPhase.Fighting, Room(server, first).Phase);
                Assert.DoesNotContain(first.Frames, frame => frame.Is(0, 0x0031));
                clock.Advance(3, server);
            }
        }
        Assert.Equal(PvpRoomPhase.AwaitRank, Room(server, first).Phase);
        foreach (var player in players) await player.SendAsync(0x003A, [], ct);
        foreach (var player in players)
        {
            var result = await player.ReadUntilAsync(frame => frame.Is(0, 0x002F), ct);
            var record = player.Frames.Last(frame => frame.Is(0, 0x0030));
            Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(record.Body[34..]));
            Assert.Equal(0, result.Body[0]);
            Assert.Equal(6, result.Body[10]);
        }
        await first.SendAsync(0x003A, [], ct);
        var resultExit = Mark(players);
        foreach (var player in players) await player.SendAsync(0x0039, [], ct);
        foreach (var player in players) await player.FenceAsync(ct);
        var rooms = server.Services.GetRequiredService<IPvpRoomQueryDomainService>();
        Assert.All(players, player =>
        {
            AssertMatchedRoomRetiredBeforeTown(player, resultExit, seatCount: 6);
            Assert.False(rooms.IsSeated(new CharacterId(player.WireUserId)));
        });

        var practice = MakeRoom("practice", 0, null);
        practice[^1] = 1;
        await first.SendAsync(0x0032, practice, ct);
        await first.FenceAsync(ct);
        Assert.Equal(PvpBattleMode.Practice, Room(server, first).Mode);
        await first.SendAsync(0x0034, [0, 0xFE], ct);
        await first.FenceAsync(ct);
        Assert.False(rooms.IsSeated(new CharacterId(first.WireUserId)));

        Assert.Equal(1L, await ScalarAsync(server, "SELECT COUNT(*) FROM pvp_matches", ct));
        Assert.Equal(6L, await ScalarAsync(server, "SELECT COUNT(*) FROM pvp_match_rating_results WHERE placement_games=1", ct));
        Assert.Equal(3L, await ScalarAsync(server, "SELECT COUNT(*) FROM pvp_match_rating_results WHERE rp_after=1020", ct));
        Assert.Equal(3L, await ScalarAsync(server, "SELECT COUNT(*) FROM pvp_match_rating_results WHERE rp_after=980", ct));
    }

    [PvfTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(0, true)]
    public async Task Ranked_total_match_cancels_pairs_rotates_and_settles_by_account(int priorMissionProgress, bool colosseum = false)
    {
        using var timeout = new CancellationTokenSource(Budget);
        var ct = timeout.Token;
        // Exercise the real PVF draw while keeping the participation card for both test representatives.
        var clock = new RelayTestClock(dayOffset: 591);
        await using var server = await StartAsync(ranked: false, ct, clock, totalMatchmaking: true);
        using var first = await EnterTeamAsync("RankedA");
        using var second = await EnterTeamAsync("RankedB");
        AssertMission(first, 0);
        AssertMission(second, 0);
        var gameDay = server.Services.GetRequiredService<IPvpSettlementRuleService>().GameDayOf(clock.GetUtcNow());
        if (priorMissionProgress > 0)
            await SeedMissionProgressAsync();
        var join = Convert.FromHexString("0107000000FFFFFFFFFFFFFFFF00");

        var mark = Mark(first);
        await first.SendAsync(0x0209, join, ct);
        await first.FenceAsync(ct);
        Assert.Equal(new byte[] { 0, 0x13 }, Assert.Single(After(first, mark), f => f.Is(1, 0x0209)).Body.ToArray());
        await EquipTeamAsync(first);
        await EquipTeamAsync(second);

        if (colosseum)
        {
            await SeedColosseumAdmissionAsync();
            await first.SendAsync(0x020F, [0, 7], ct);
            var receipt = await first.ReadUntilAsync(f => f.Is(1, 0x020F), ct);
            Assert.Equal(1, receipt.Body[0]);
            await first.SendAsync(0x0209, join, ct);
            Assert.Equal(new byte[] { 1, 1, 7, 0, 0 }, (await first.ReadUntilAsync(f => f.Is(1, 0x0209), ct)).Body.ToArray());
            await first.SendAsync(0x020A, [1, 7, 0], ct);
            Assert.Equal(new byte[] { 1 }, (await first.ReadUntilAsync(f => f.Is(1, 0x020A), ct)).Body.ToArray());
        }

        await JoinAsync(first);
        await first.SendAsync(0x023D, [], ct);
        var counts = await first.ReadUntilAsync(frame => frame.Is(0, 0x01FE), ct);
        Assert.Equal(3, counts.Body[4]);
        Assert.Equal(7, BinaryPrimitives.ReadInt32LittleEndian(counts.Body[21..]));
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(counts.Body[25..]));
        await first.SendAsync(0x020A, [1, 7, 0], ct);
        Assert.Equal(new byte[] { 1 }, (await first.ReadUntilAsync(f => f.Is(1, 0x020A), ct)).Body.ToArray());
        await JoinAsync(second);
        Assert.DoesNotContain(second.Frames, f => f.Is(0, 0x01F9));

        await JoinAsync(first);
        await FenceAllAsync(ct, first, second);
        var versus = Assert.Single(first.Frames, f => f.Is(0, 0x01F9));
        Assert.Equal(2, versus.Body[5]);
        Assert.Equal(second.WireUserId, BinaryPrimitives.ReadUInt16LittleEndian(versus.Body[6..]));
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(versus.Body[9..]));
        Assert.Equal(first.WireUserId, BinaryPrimitives.ReadUInt16LittleEndian(versus.Body[19..]));
        Assert.Equal(colosseum ? 1000 : 0, BinaryPrimitives.ReadInt32LittleEndian(versus.Body[22..]));
        foreach (var player in new[] { first, second })
        {
            var groups = Assert.Single(player.Frames, f => f.Is(0, 0x031A) && f.Body[0] == 2);
            Assert.True(groups.Body.IndexOf(WireClient.DStrings("RankedA2")) >= 0);
            Assert.True(groups.Body.IndexOf(WireClient.DStrings("RankedB2")) >= 0);
            Assert.Contains(player.Frames, f => f.Is(0, 0x031C));
            await player.SendAsync(0x023B, versus.Body[1..5].ToArray(), ct);
        }
        await FenceAllAsync(ct, second, first);
        using (var preload = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            preload.CancelAfter(TimeSpan.FromSeconds(12));
            if (!first.Frames.Any(f => f.Is(0, 0x01FB) && f.Body[0] == 2))
                await first.ReadUntilAsync(f => f.Is(0, 0x01FB) && f.Body[0] == 2, preload.Token);
        }
        var room = Room(server, first);
        var firstSeat = room.Members.Single(m => m.Member.Session.WireUserId == first.WireUserId).Seat;
        var secondSeat = room.Members.Single(m => m.Member.Session.WireUserId == second.WireUserId).Seat;
        await first.SendAsync(0x012A, [], ct);
        await second.SendAsync(0x012A, [], ct);
        await FenceAllAsync(ct, first, second);
        await first.SendAsync(0x012B, [8, .. Links(secondSeat)], ct);
        await second.SendAsync(0x012B, [8, .. Links(firstSeat)], ct);
        await FenceAllAsync(ct, second, first);
        using var battleStart = CancellationTokenSource.CreateLinkedTokenSource(ct);
        battleStart.CancelAfter(TimeSpan.FromSeconds(12));
        if (!first.Frames.Any(f => f.Is(0, 0x002D)))
            await first.ReadUntilAsync(f => f.Is(0, 0x002D), battleStart.Token);
        var start = Assert.Single(first.Frames, f => f.Is(0, 0x002D));
        Assert.Equal(8, start.Body[1]);
        Assert.Contains(first.Frames, f => f.Is(0, 0x01FA));

        clock.Advance(2, server);
        await FenceAllAsync(ct, first, second);
        Assert.DoesNotContain(first.Frames, f => f.Is(0, 0x031E));
        await first.SendAsync(0x006E, [], ct);
        await first.FenceAsync(ct);
        clock.Advance(3, server);
        await FenceAllAsync(ct, first, second);
        Assert.DoesNotContain(first.Frames, f => f.Is(0, 0x031E));
        await second.SendAsync(0x006E, [], ct);
        await second.FenceAsync(ct);
        clock.Advance(1, server);
        await FenceAllAsync(ct, first, second);
        Assert.DoesNotContain(second.Frames, f => f.Is(0, 0x031E));
        clock.Advance(1, server);
        for (byte round = 0; round < 3; round++)
        {
            await first.ReadUntilAsync(f => f.Is(0, 0x031E), ct);
            await second.ReadUntilAsync(f => f.Is(0, 0x031E), ct);
            if (round == 0) await first.SendAsync(0x0361, [2], ct);
            await second.SendAsync(0x0361, [round], ct);
            await first.ReadUntilAsync(f => f.Is(0, 0x031D), ct);
            await second.ReadUntilAsync(f => f.Is(0, 0x031D), ct);
            await first.SendAsync(0x006E, [], ct);
            await second.SendAsync(0x006E, [], ct);
            await FenceAllAsync(ct, first, second);
            clock.Advance(8, server);
            await second.SendAsync(0x0037, [.. U16(second.WireUserId), .. U16(first.WireUserId), 1], ct);
            await FenceAllAsync(ct, second, first);
            Assert.Equal(round + 1, Room(server, first).Match!.Fighters.Single(f => f.Seat == secondSeat).Deaths);
            clock.Advance(3, server);
        }

        foreach (var player in new[] { first, second })
        {
            await player.ReadUntilAsync(f => f.Is(0, 0x0031), ct);
            await player.SendAsync(0x003A, [], ct);
        }
        await FenceAllAsync(ct, first, second);
        foreach (var player in new[] { first, second })
        {
            if (!player.Frames.Any(f => f.Is(0, 0x002F)))
                await player.ReadUntilAsync(f => f.Is(0, 0x002F), ct);
            var frames = player.Frames.ToList();
            var record = frames.Last(f => f.Is(0, 0x0030));
            Assert.Equal(129, record.Body.Length);
            Assert.Equal(colosseum && player == first ? 20 : 1, BinaryPrimitives.ReadInt32LittleEndian(record.Body[47..]));
            var end = Assert.Single(frames, f => f.Is(0, 0x002F));
            Assert.Equal(firstSeat, end.Body[0]);
            Assert.True(frames.IndexOf(record) < frames.IndexOf(end));
            for (var ordinal = 0; ordinal < 3; ordinal++)
            {
                var bar = record.Body[(51 + ordinal * 26)..];
                var before = BinaryPrimitives.ReadInt32LittleEndian(bar);
                var after = BinaryPrimitives.ReadInt32LittleEndian(bar[4..]);
                Assert.True(BinaryPrimitives.ReadInt32LittleEndian(bar[14..]) > BinaryPrimitives.ReadInt32LittleEndian(bar[10..]));
                Assert.True(BinaryPrimitives.ReadInt32LittleEndian(bar[22..]) > BinaryPrimitives.ReadInt32LittleEndian(bar[18..]));
                if (player == first && ordinal == 2)
                {
                    Assert.True(after > before);
                    Assert.Equal(0, bar[8]);
                    Assert.Equal(1, bar[9]);
                    Assert.Equal(after - before, BinaryPrimitives.ReadInt32LittleEndian(end.Body[6..]));
                }
                else
                {
                    Assert.Equal(before, after);
                }
            }
            await player.SendAsync(0x0039, [], ct);
            await player.FenceAsync(ct);
        }
        await first.FenceAsync(ct);
        var progress = priorMissionProgress + 1;
        AssertMission(first, progress);
        AssertMission(second, progress);
        // Repeat the result-screen acknowledgements after the temporary room is already gone.
        await first.SendAsync(0x0039, [], ct);
        await second.SendAsync(0x0039, [], ct);
        await FenceAllAsync(ct, first, second);
        Assert.Equal(2L, await ScalarAsync(server, $"SELECT COUNT(*) FROM pvp_daily_missions WHERE mission_id=136 AND progress={progress}", ct));
        Assert.Equal(progress == 3 ? 2L : 0L, await ScalarAsync(server,
            "SELECT COUNT(*) FROM pvp_daily_missions WHERE mission_id=136 AND reward_match_id IN (SELECT match_id FROM pvp_matches)", ct));
        foreach (var player in new[] { first, second })
        {
            var emblems = await ScalarAsync(server, $"SELECT COALESCE(SUM(count),0) FROM item_slots WHERE owner_scope='character' "
                + $"AND owner_id={player.WireUserId} AND list_type=0 AND slot_index=2", ct);
            Assert.Equal((progress == 3 ? 80L : 0L) + (colosseum && player == first ? 200L : 0L), emblems);
            var rewards = player.Frames.Count(frame => frame.Is(0, 0x000E) && frame.Body.Length >= 13
                && frame.Body[0] == (byte)InventoryContainerType.Main
                && BinaryPrimitives.ReadUInt16LittleEndian(frame.Body[1..]) == 1
                && BinaryPrimitives.ReadInt16LittleEndian(frame.Body[3..]) == 2
                && BinaryPrimitives.ReadInt32LittleEndian(frame.Body[5..]) == 2
                && BinaryPrimitives.ReadInt32LittleEndian(frame.Body[9..]) == 80);
            Assert.Equal(progress == 3 ? 1 : 0, rewards);
        }
        var missionStore = server.Services.GetRequiredService<IPvpDailyMissionStore>();
        Assert.Equal(0, await missionStore.ProgressAsync(new CharacterId(first.WireUserId), gameDay.AddDays(1), 136, ct));
        var rooms = server.Services.GetRequiredService<IPvpRoomQueryDomainService>();
        Assert.False(rooms.IsSeated(new CharacterId(first.WireUserId)));
        Assert.False(rooms.IsSeated(new CharacterId(second.WireUserId)));
        Assert.Equal(colosseum ? 2L : 1L, await ScalarAsync(server, "SELECT COUNT(*) FROM pvp_matches WHERE battle_mode=8", ct));
        if (colosseum)
        {
            Assert.Equal(1L, await ScalarAsync(server, "SELECT COUNT(*) FROM pvp_colosseum_runs WHERE kind=7 AND wins=1 AND losses=0", ct));
            var frames = first.Frames.ToList();
            var end = frames.FindIndex(frame => frame.Is(0, 0x002F));
            var context = frames.FindIndex(frame => frame.Is(0, 0x0262) && frame.Body[10] == 1);
            Assert.InRange(context, 0, end - 1);
            Assert.Equal(1, await missionStore.ProgressAsync(new CharacterId(first.WireUserId), gameDay, 143, ct));
            Assert.Equal(0, await missionStore.ProgressAsync(new CharacterId(second.WireUserId), gameDay, 143, ct));
        }
        Assert.Equal(0L, await ScalarAsync(server, "SELECT COUNT(*) FROM pvp_match_rating_results", ct));
        Assert.Equal(colosseum ? 1L : 2L, await ScalarAsync(server, "SELECT COUNT(*) FROM account_pvp_total_match_rating_results WHERE placement_games=1 AND gp_after IN (980,1020)", ct));
        Assert.Equal(1L, await ScalarAsync(server, "SELECT COUNT(*) FROM pvp_match_results WHERE counts_toward_daily=1 AND outcome=0", ct));
        Assert.Equal(3L, await ScalarAsync(server, "SELECT COUNT(*) FROM pvp_match_results WHERE counts_toward_daily=1 AND outcome=1", ct));

        static void AssertMission(WireClient player, int progress)
        {
            var mission = player.Frames.Last(frame => frame.Is(0, 0x0143));
            Assert.Equal(4, mission.Body[0]);
            Assert.Equal(41, mission.Body.Length);
            Assert.Equal(136, BinaryPrimitives.ReadUInt16LittleEndian(mission.Body[1..]));
            Assert.InRange(BinaryPrimitives.ReadUInt16LittleEndian(mission.Body[11..]), 139, 140);
            Assert.Equal(142, BinaryPrimitives.ReadUInt16LittleEndian(mission.Body[21..]));
            Assert.Equal(143, BinaryPrimitives.ReadUInt16LittleEndian(mission.Body[31..]));
            Assert.Equal(progress, BinaryPrimitives.ReadInt32LittleEndian(mission.Body[3..]));
            Assert.Equal(3, BinaryPrimitives.ReadInt32LittleEndian(mission.Body[7..]));
        }

        var login = await server.IssueTicketAsync("RankedA", ct);
        first.Dispose();
        using var returning = await server.ConnectAsync("reserve relog", ct);
        await returning.LoginAsync(login.MId, login.Ticket, ct);
        await returning.SelectCharacterAsync(ct, rosterIndex: 1);
        await returning.FenceAsync(ct);
        AssertMission(returning, 0);
        var persisted = Assert.Single(returning.Frames, f => f.Is(0, 0x0030));
        Assert.Equal(colosseum ? 20 : 1, BinaryPrimitives.ReadInt32LittleEndian(persisted.Body[47..]));

        async Task SeedColosseumAdmissionAsync()
        {
            var selected = server.Services.GetRequiredService<ISelectedSessionDomainService>();
            Assert.True(selected.TryGetInWorld(first.WireUserId, out var actor));
            await using (var connection = new SqliteConnection($"Data Source={server.DatabasePath};Pooling=False"))
            {
                await connection.OpenAsync(ct);
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO pvp_matches(match_id,room_id,listener_port,channel_env,battle_mode,outcome_kind,winner,
                        records_result,game_day,completed_at)
                    VALUES('seed-peak-colosseum',1,44081,8,8,0,0,1,'2026-11-25',1795564800000);
                    INSERT INTO account_pvp_total_match_rating_results
                        (match_id,account_id,season_id,placement_games,gp_before,gp_after,peak_gp)
                    VALUES('seed-peak-colosseum',@account,1,20,1000,1000,1000);
                    """;
                command.Parameters.AddWithValue("@account", actor.AccountId.Value);
                await command.ExecuteNonQueryAsync(ct);
            }
            await TestGameplay.RunAsync(server.Services, async () =>
            {
                var scope = server.Services.GetRequiredService<DomainTransactionScope>();
                await using var transaction = scope.Begin();
                var items = server.Services.GetRequiredService<IItemDomainStore>().SessionIn(scope.Current!);
                var item = await items.CreateAsync(new ItemSnapshot
                {
                    ItemId = 2, LegacyKind = 0, Type = ItemTraits.None, IsPile = true, Count = 400,
                }, ct);
                await items.PutAsync(ContainerId.ForCharacter(actor.CharacterId, InventoryContainerType.Main), 2, item, ct);
                await transaction.CommitAsync(ct);
            });
        }

        Task SeedMissionProgressAsync() => TestGameplay.RunAsync(server.Services, async () =>
        {
            var services = server.Services;
            var scope = services.GetRequiredService<DomainTransactionScope>();
            await using var transaction = scope.Begin();
            var missions = services.GetRequiredService<IPvpDailyMissionStore>();
            foreach (var player in new[] { first, second })
                for (var index = 0; index < priorMissionProgress; index++)
                    Assert.False(await missions.AdvanceAsync(new CharacterId(player.WireUserId), gameDay, 136, 3,
                        PvpMatchId.New(), ct));
            await transaction.CommitAsync(ct);
        });

        async Task<WireClient> EnterTeamAsync(string name)
        {
            var credentials = await server.RegisterAndIssueTicketAsync(name, ct);
            var client = await server.ConnectAsync(name, ct);
            await client.LoginAsync(credentials.MId, credentials.Ticket, ct);
            await client.CreateCharacterAsync(name + "0", ct);
            await client.CreateCharacterAsync(name + "1", ct, job: 1);
            await client.CreateCharacterAsync(name + "2", ct, job: 2);
            await client.SelectCharacterAsync(ct);
            await client.SendAsync(0x035E, [.. WireClient.DStrings(name), 0, 1, 2], ct);
            await client.FenceAsync(ct);
            Assert.Contains(client.Frames, frame => frame.Is(1, 0x035E) && frame.Body[0] == 1);
            return client;
        }

        Task EquipTeamAsync(WireClient client) => TestGameplay.RunAsync(server.Services, async () =>
        {
            var services = server.Services;
            Assert.True(services.GetRequiredService<ISelectedSessionDomainService>()
                .TryGetInWorld(client.WireUserId, out var selected));
            var roster = services.GetRequiredService<ICharacterRosterDomainService>();
            var scope = services.GetRequiredService<DomainTransactionScope>();
            await using var transaction = scope.Begin();
            var store = services.GetRequiredService<IItemDomainStore>().SessionIn(scope.Current!);
            int[] equipment = [27600, 400330013, 10400, 100180014, 12400, 100280013, 100230014, 20151, 22143, 24157];
            for (ushort index = 0; index < 3; index++)
            {
                var character = await roster.LoadByRosterPositionAsync(selected.AccountId, index, ct);
                var container = ContainerId.ForCharacter(character.Identity.CharacterId, InventoryContainerType.Equipment);
                for (var slot = 12; slot < 22; slot++)
                {
                    if (await store.GetAsync(container, slot, ct) is not null) continue;
                    var template = slot == 12 && index != 0 ? (index == 1 ? 29900 : 31300) : equipment[slot - 12];
                    var item = await store.CreateAsync(ItemSnapshot.FromCore(StackableItemCore.Create(1, template, 1), false), ct);
                    await store.PutAsync(container, slot, item, ct);
                }
            }
            await transaction.CommitAsync(ct);
        });

        async Task JoinAsync(WireClient player)
        {
            var last = Mark(player);
            var flag = colosseum && player == first ? (byte)1 : (byte)0;
            await player.SendAsync(0x0209, [.. join[..^1], flag], ct);
            await player.FenceAsync(ct);
            var result = Assert.Single(After(player, last), f => f.Is(1, 0x0209));
            Assert.Equal(new byte[] { 1, 1, 7, 0, flag }, result.Body.ToArray());
        }

        static IEnumerable<byte> Links(int target) => Enumerable.Range(0, 8)
            .SelectMany(seat => new byte[] { (byte)seat, seat == target ? (byte)1 : (byte)0 });
    }

    [PvfFact]
    public async Task Total_match_lobby_loads_the_representatives_candidates_on_save_and_entry()
    {
        using var timeout = new CancellationTokenSource(Budget);
        var ct = timeout.Token;
        await using var server = await StartAsync(ranked: false, ct, totalMatchmaking: true);
        var (mId, ticket) = await server.RegisterAndIssueTicketAsync("total-match-lobby", ct);
        using (var player = await server.ConnectAsync("representative", ct))
        {
            await player.LoginAsync(mId, ticket, ct);
            await player.CreateCharacterAsync("LobbyLeader", ct);
            await player.CreateCharacterAsync("LobbySecond", ct, job: 1);
            await player.CreateCharacterAsync("LobbyThird", ct, job: 2);
            await player.SelectCharacterAsync(ct);

            var mark = Mark(player);
            await player.SendAsync(0x035E, [.. CountedAscii("LobbyTeam"), 0, 2, 1], ct);
            await player.FenceAsync(ct);
            var frames = After(player, mark).Where(frame => frame.Is(0, 0x031B) || frame.Is(1, 0x035E)
                || frame.Is(0, 0x031A)).ToArray();
            Assert.Collection(frames,
                frame => Assert.True(frame.Is(0, 0x031B)),
                frame =>
                {
                    Assert.True(frame.Is(1, 0x035E));
                    Assert.Equal(new byte[] { 1, 0, 2, 1 }, frame.Body.ToArray());
                },
                frame =>
                {
                    Assert.True(frame.Is(0, 0x031A));
                    byte[] expected = [1, .. U16(player.WireUserId), 3];
                    Assert.Equal(expected, frame.Body[..4].ToArray());
                });
        }

        (mId, ticket) = await server.IssueTicketAsync("total-match-lobby", ct);
        using var returning = await server.ConnectAsync("returning representative", ct);
        await returning.LoginAsync(mId, ticket, ct);
        await returning.SelectCharacterAsync(ct);
        await returning.FenceAsync(ct);
        var entry = returning.Frames.ToArray();
        var roster = Array.FindIndex(entry, frame => frame.Is(0, 0x031B));
        var lobby = Array.FindIndex(entry, frame => frame.Is(0, 0x0029));
        var candidates = Array.IndexOf(entry, Assert.Single(entry, frame => frame.Is(0, 0x031A)));
        Assert.True(roster >= 0 && lobby > roster && candidates > lobby);
        byte[] expectedCandidates = [1, .. U16(returning.WireUserId), 3];
        Assert.Equal(expectedCandidates, entry[candidates].Body[..4].ToArray());
    }

    [PvfFact]
    public async Task Total_match_team_uses_account_roster_positions_and_reads_back_after_reordering()
    {
        using var timeout = new CancellationTokenSource(Budget);
        var ct = timeout.Token;
        await using var server = await StartAsync(ranked: true, ct);
        var name = "MatchTeam"u8.ToArray();
        byte[] encodedName = [9, 0, 0, 0, .. name];

        var login = await server.RegisterAndIssueTicketAsync("duel-roster", ct);
        using (var player = await server.ConnectAsync("roster", ct))
        {
            await player.LoginAsync(login.MId, login.Ticket, ct);
            await player.CreateCharacterAsync("RosterMain", ct);
            await player.CreateCharacterAsync("RosterSecond", ct, job: 1);
            await player.CreateCharacterAsync("RosterThird", ct, job: 2);
            await server.ExecuteSqlAsync("""
                UPDATE characters SET slot_index=4 WHERE name='RosterThird';
                UPDATE characters SET slot_index=2 WHERE name='RosterSecond';
                """, ct);
            await player.SelectCharacterAsync(ct);

            var mark = Mark(player);
            await player.SendAsync(0x035F, encodedName, ct);
            await player.FenceAsync(ct);
            Assert.Equal(new byte[] { 1 }, Assert.Single(After(player, mark), frame => frame.Is(1, 0x035F)).Body.ToArray());

            mark = Mark(player);
            await player.SendAsync(0x035E, [.. encodedName, 0, 0, 2], ct);
            await player.FenceAsync(ct);
            Assert.Equal(new byte[] { 0, 2 }, Assert.Single(After(player, mark), frame => frame.Is(1, 0x035E)).Body.ToArray());

            mark = Mark(player);
            await player.SendAsync(0x035E, [.. encodedName, 2, 0, 1], ct);
            await player.FenceAsync(ct);
            var frames = After(player, mark).Where(frame => frame.Is(0, 0x031B) || frame.Is(1, 0x035E)).ToArray();
            Assert.Collection(frames,
                frame =>
                {
                    Assert.True(frame.Is(0, 0x031B));
                    Assert.Equal(new byte[] { 2, 0, 1 }, frame.Body.ToArray()[^3..]);
                },
                frame =>
                {
                    Assert.True(frame.Is(1, 0x035E));
                    Assert.Equal(new byte[] { 1, 2, 0, 1 }, frame.Body.ToArray());
                });
        }

        await server.ExecuteSqlAsync("UPDATE characters SET slot_index=1 WHERE name='RosterThird';", ct);
        var (mId, ticket) = await server.IssueTicketAsync("duel-roster", ct);
        using var returning = await server.ConnectAsync("returning", ct);
        await returning.LoginAsync(mId, ticket, ct);
        await returning.SelectCharacterAsync(ct);
        var restored = Assert.Single(returning.Frames, frame => frame.Is(0, 0x031B));
        Assert.Equal(new byte[] { 1, 0, 2 }, restored.Body.ToArray()[^3..]);
    }

    [PvfFact]
    public async Task Changing_the_total_match_representative_uses_the_roster_from_return_to_selection()
    {
        using var timeout = new CancellationTokenSource(Budget);
        var ct = timeout.Token;
        await using var server = await CharacterizationHost.StartAsync(new CharacterizationOptions
        {
            Decorate = services => services.Decorate<IChannelEntryDomainService>(
                (_, inner) => new PvpChannel(inner, 13, ChannelEntryKind.FreeDuel)),
        }, ct);
        var login = await server.RegisterAndIssueTicketAsync("duel-representative", ct);
        using var player = await server.ConnectAsync("representative", ct);
        await player.LoginAsync(login.MId, login.Ticket, ct);
        await player.CreateCharacterAsync("RosterOriginal", ct);
        await player.CreateCharacterAsync("RosterNew", ct, job: 1);
        await player.CreateCharacterAsync("RosterReserve", ct, job: 2);
        var mark = Mark(player);
        await player.SendAsync(0x0008, [0xFF, 0xFF, 2], ct);
        await player.FenceAsync(ct);
        Assert.Single(After(player, mark), frame => frame.Is(0, 0x0002) && frame.Body[0] == 2);
        await player.SelectCharacterAsync(ct);
        var original = player.WireUserId;
        await player.SendAsync(0x0032, MakeRoom("total", 1, null), ct);
        await player.SendAsync(0x0036, [8], ct);
        await player.FenceAsync(ct);
        Assert.Equal(PvpBattleMode.TotalMatch, TestGameplay.Run(server.Services, () => Room(server, player).Mode));

        mark = Mark(player);
        await player.SendAsync(0x035E, [.. WireClient.DStrings("RosterTeam"), 1, 2, 0], ct);
        await player.FenceAsync(ct);
        Assert.Equal(new byte[] { 1, 1, 2, 0 },
            Assert.Single(After(player, mark), frame => frame.Is(1, 0x035E)).Body.ToArray());

        mark = Mark(player);
        await player.SendAsync(0x0007, [], ct);
        await player.FenceAsync(ct);
        Assert.Contains(After(player, mark), frame => frame.Is(1, 0x0007));
        Assert.Single(After(player, mark), frame => frame.Is(0, 0x0002) && frame.Body[0] == 2);

        // A21 queries the roster again before processing the automatic selection.
        mark = Mark(player);
        await player.SendAsync(0x0008, [0xFF, 0xFF, 2], ct);
        await player.FenceAsync(ct);
        Assert.DoesNotContain(After(player, mark), frame => frame.Is(0, 0x0002) && frame.Body[0] == 2);
        Assert.Contains(After(player, mark), frame => frame.Is(1, 0x0286));

        mark = Mark(player);
        await player.SelectCharacterAsync(ct, rosterIndex: 1);
        Assert.NotEqual(original, player.WireUserId);
        Assert.DoesNotContain(After(player, mark), frame => frame.Is(0, 0x0002) && frame.Body[0] == 2);
        Assert.Contains(After(player, mark), frame => frame.Is(0, 0x007C));
        Assert.Contains(After(player, mark), frame => frame.Is(0, 0x0029));
        Assert.Contains(After(player, mark), frame => frame.Is(0, 0x031B));

        await player.SendAsync(0x0007, [], ct);
        await player.FenceAsync(ct);
        mark = Mark(player);
        await player.SelectCharacterAsync(ct);
        Assert.Equal(original, player.WireUserId);
        Assert.Single(After(player, mark), frame => frame.Is(0, 0x0002) && frame.Body[0] == 2);
    }

    [PvfFact]
    public async Task Duel_build_switch_is_kept_when_creating_a_room_and_does_not_change_the_ordinary_build()
    {
        using var timeout = new CancellationTokenSource(Budget);
        var ct = timeout.Token;
        await using var server = await StartAsync(ranked: true, ct);
        using var player = await server.EnterWorldAsync("skill", "duel-skill-build", "DuelSkill", ct,
            () => server.ExecuteSqlAsync("""
                UPDATE characters SET active_skill_build=0 WHERE name='DuelSkill';
                INSERT INTO character_skill_builds(character_id,build_id,unlocked)
                    SELECT character_id,0,1 FROM characters WHERE name='DuelSkill'
                    ON CONFLICT(character_id,build_id) DO UPDATE SET unlocked=1;
                INSERT INTO character_skill_builds(character_id,build_id,unlocked)
                    SELECT character_id,1,1 FROM characters WHERE name='DuelSkill'
                    ON CONFLICT(character_id,build_id) DO UPDATE SET unlocked=1;
                DELETE FROM item_slots WHERE owner_scope='character' AND owner_id IN
                    (SELECT character_id FROM characters WHERE name='DuelSkill');
                """, ct));

        var mark = Mark(player);
        await player.SendAsync(0x0104, [0, 0x3E, 0x74, 0x5E, 0x7A], ct);
        await player.FenceAsync(ct);
        Assert.Equal(new byte[] { 1, 1 },
            Assert.Single(After(player, mark), frame => frame.Is(1, 0x0104)).Body.ToArray());

        mark = Mark(player);
        await player.SendAsync(0x0032, MakeRoom("skill", 1, password: null), ct);
        await player.FenceAsync(ct);
        var userInfo = Assert.Single(After(player, mark), frame => frame.Is(0, 0x0002) && frame.Body[0] == 1);
        const int equipmentCountOffset = 28 + 88 + 1;
        Assert.Equal(0, userInfo.Body[equipmentCountOffset]);
        Assert.Equal(1, userInfo.Body[equipmentCountOffset + 1 + 3 * sizeof(uint)]);
        Assert.Equal(0L, await ScalarAsync(server, "SELECT active_skill_build FROM characters WHERE name='DuelSkill'", ct));
        Assert.Equal(1L, await ScalarAsync(server, "SELECT active_skill_build FROM character_pvp_skill_state", ct));
    }

    [PvfFact]
    public async Task A_room_observer_sees_the_battle_without_starting_or_settling_as_a_fighter()
    {
        using var timeout = new CancellationTokenSource(Budget);
        var ct = timeout.Token;
        await using var server = await StartAsync(ranked: true, ct);
        using var owner = await server.EnterWorldAsync("owner", "observer-owner", "WatchOwner", ct);
        using var fighter = await server.EnterWorldAsync("fighter", "observer-fighter", "WatchFighter", ct);
        using var viewer = await server.EnterWorldAsync("viewer", "observer-viewer", "WatchViewer", ct);
        await FenceAllAsync(ct, owner, fighter, viewer);

        await owner.SendAsync(0x0032, MakeRoom("watch", 1, null), ct);
        await FenceAllAsync(ct, owner, viewer);
        var number = Assert.Single(PvpLobbyWireView.Of(viewer.Frames).Numbers);
        await fighter.SendAsync(0x0033, EnterRoom(number, null), ct);
        await fighter.FenceAsync(ct);
        await viewer.SendAsync(0x0033, EnterRoom(number, null), ct);
        await viewer.FenceAsync(ct);
        var lobby = PvpLobbyWireView.Of(viewer.Frames);
        var viewerSeat = (byte)lobby.SeatOf(number, viewer.WireUserId);
        var fighterSeat = (byte)lobby.SeatOf(number, fighter.WireUserId);
        var before = Assert.Single(await RecordsAsync(server, ct, viewer));

        var watch = Mark(owner, viewer);
        await viewer.SendAsync(0x0034, [viewerSeat, 3], ct);
        await viewer.FenceAsync(ct);
        var watchSeat = Assert.Single(After(viewer, watch), frame => frame.Is(0, 0x002B) && frame.Body[4] == viewerSeat
            && frame.Body[5] == 3);
        Assert.Equal(viewer.WireUserId, BinaryPrimitives.ReadUInt16LittleEndian(watchSeat.Body[6..]));
        await viewer.SendAsync(0x0034, [viewerSeat, 4], ct);
        await FenceAllAsync(ct, viewer, owner);
        Assert.Contains(After(owner, watch), frame => frame.Is(0, 0x002B) && frame.Body[4] == viewerSeat && frame.Body[5] == 4);

        var battle = Mark(owner, fighter, viewer);
        await fighter.SendAsync(0x0035, [1], ct);
        await fighter.FenceAsync(ct);
        await owner.SendAsync(0x0035, [1], ct);
        await viewer.ReadUntilAsync(frame => frame.Is(0, 0x002D), ct);
        var playing = TestGameplay.Run(server.Services, () => Room(server, owner));
        Assert.Equal(2, playing.Match!.Fighters.Length);
        Assert.DoesNotContain(playing.Match.Fighters, entry => entry.Seat == viewerSeat);
        await viewer.SendAsync(0x0037, [.. U16(viewer.WireUserId), .. U16(owner.WireUserId), 0], ct);
        await viewer.FenceAsync(ct);
        Assert.Equal(playing.Version, TestGameplay.Run(server.Services, () => Room(server, owner)).Version);

        await fighter.SendAsync(0x0037, [.. U16(fighter.WireUserId), .. U16(owner.WireUserId), 0], ct);
        await viewer.ReadUntilAsync(frame => frame.Is(0, 0x0031), ct);
        Assert.Contains(After(viewer, battle), frame => frame.Is(0, 0x002E) && frame.Body[0] == fighterSeat);
        await owner.SendAsync(0x003A, [], ct);
        await fighter.SendAsync(0x003A, [], ct);
        var result = await viewer.ReadUntilAsync(frame => frame.Is(0, 0x002F), ct);
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(result.Body[6..]));
        Assert.Equal(2, result.Body[10]);
        await viewer.ReadUntilAsync(frame => frame.Is(0, 0x0030), ct);
        Assert.Equal(before, Assert.Single(await RecordsAsync(server, ct, viewer)));
        Assert.Equal(2L, await ScalarAsync(server, "SELECT COUNT(*) FROM pvp_match_results", ct));

        await owner.SendAsync(0x0039, [], ct);
        await fighter.SendAsync(0x0039, [], ct);
        await viewer.ReadUntilAsync(frame => frame.Is(0, 0x002A) && frame.Body[2] == 1, ct);
        await viewer.SendAsync(0x0034, [viewerSeat, 1], ct);
        await viewer.ReadUntilAsync(frame => frame.Is(0, 0x002B) && frame.Body[4] == viewerSeat && frame.Body[5] == 1, ct);
        Assert.Equal(PvpRoomPhase.Waiting, TestGameplay.Run(server.Services, () => Room(server, viewer)).Phase);
    }

    [PvfFact]
    public async Task A_whole_battle_settles_once_and_the_lobby_matches_a_fresh_snapshot_after_every_step()
    {
        using var timeout = new CancellationTokenSource(Budget);
        var ct = timeout.Token;
        await using var server = await StartAsync(ranked: true, ct);
        var probes = 0;
        using var a = await server.EnterWorldAsync("a", "duel-a", "DuelA", ct);
        using var b = await server.EnterWorldAsync("b", "duel-b", "DuelB", ct);
        using var c = await server.EnterWorldAsync("c", "duel-c", "DuelC", ct);
        await FenceAllAsync(ct, a, b, c);
        await AssertLobbyAsync("entered", server, c, ++probes, ct);

        var rules = server.Services.GetRequiredService<IPvpRoomRuleService>();
        Assert.True(rules.AcceptsMap(1) && rules.AcceptsMap(2), "The archive's duel map order has fewer than three entries.");

        StepAll("a makes a room", a, b, c);
        await a.SendAsync(0x0032, MakeRoom("duel", 1, "pw"), ct);
        await FenceAllAsync(ct, a, b, c);
        var number = Assert.Single(PvpLobbyWireView.Of(c.Frames).Numbers);
        await AssertLobbyAsync("made", server, c, ++probes, ct);

        StepAll("b enters with the password", a, b, c);
        await b.SendAsync(0x0033, EnterRoom(number, "pw"), ct);
        await FenceAllAsync(ct, b, a, c);
        var entered = Assert.Single(b.Frames, frame => frame.Is(1, 0x0033));
        Assert.Equal(1, entered.Body[0]);
        await AssertLobbyAsync("entered room", server, c, ++probes, ct);
        var lobby = PvpLobbyWireView.Of(c.Frames);
        Assert.Equal(0, lobby.SeatOf(number, a.WireUserId));
        Assert.InRange(lobby.SeatOf(number, b.WireUserId), 1, 7);
        var seatA = (byte)lobby.SeatOf(number, a.WireUserId);
        var seatB = (byte)lobby.SeatOf(number, b.WireUserId);

        StepAll("b switches to the first team", a, b, c);
        await b.SendAsync(0x0034, [seatB, 1], ct);
        await FenceAllAsync(ct, b, a, c);
        await AssertLobbyAsync("b on team 1", server, c, ++probes, ct);

        StepAll("b switches back to the second team", a, b, c);
        await b.SendAsync(0x0034, [seatB, 2], ct);
        await FenceAllAsync(ct, b, a, c);
        await AssertLobbyAsync("b on team 2", server, c, ++probes, ct);

        StepAll("a changes the map", a, b, c);
        await a.SendAsync(0x003B, [2, 0], ct);
        await FenceAllAsync(ct, a, b, c);
        await AssertLobbyAsync("map 2", server, c, ++probes, ct);

        var before = await RecordsAsync(server, ct, a, b);
        var battle = Mark(a, b, c);
        StepAll("b ready, a starts", a, b, c);
        await b.SendAsync(0x0035, [1], ct);
        await FenceAllAsync(ct, b, a, c);
        await a.SendAsync(0x0035, [1], ct);
        await FenceAllAsync(ct, a, b, c);
        // b's own ready flag first, then the owner's flag, the start and the room's battle state.
        foreach (var fighter in new[] { a, b })
        {
            var started = After(fighter, battle).Where(frame => frame.IsNoti && frame.Type is 0x002C or 0x002D or 0x002A).ToArray();
            Assert.Equal(new[] { 0x002C, 0x002C, 0x002D, 0x002A }, started.Select(frame => (int)frame.Type));
            Assert.Equal(new byte[] { seatB, 1 }, started[0].Body.ToArray());
            Assert.Equal(new byte[] { seatA, 1 }, started[1].Body.ToArray());
            Assert.Equal(new byte[] { 2, 2 }, started[2].Body.ToArray());
            Assert.Equal(2, started[3].Body[2]);
        }
        Assert.Equal(2, PvpLobbyWireView.Of(c.Frames).StateOf(number));
        await AssertLobbyAsync("battle", server, c, ++probes, ct);

        // Spec SA-1: fighting from the start; loading and linking reports are only logged and change nothing.
        var fighting = TestGameplay.Run(server.Services, () => Room(server, a));
        Assert.Equal(PvpRoomPhase.Fighting, fighting.Phase);
        var reported = Mark(a, b, c);
        StepAll("both report loading and linking", a, b, c);
        await a.SendAsync(0x012A, [], ct);
        await b.SendAsync(0x012A, [], ct);
        await a.SendAsync(0x012B, [1, seatB, 1], ct);
        await b.SendAsync(0x012B, [1, seatA, 1], ct);
        await FenceAllAsync(ct, a, b, c);
        TestGameplay.Run(server.Services, () =>
        {
            var room = Room(server, a);
            Assert.Equal(PvpRoomPhase.Fighting, room.Phase);
            Assert.Equal(fighting.Version, room.Version);
        });
        foreach (var client in new[] { a, b, c })
            Assert.DoesNotContain(After(client, reported), frame => frame.IsNoti && frame.Type is >= 0x0029 and <= 0x0031);

        StepAll("b dies to a", a, b, c);
        await b.SendAsync(0x0037, [.. U16(b.WireUserId), .. U16(a.WireUserId), 0], ct);
        await FenceAllAsync(ct, b, a);
        StepAll("both close the ranking", a, b, c);
        await a.SendAsync(0x003A, [], ct);
        await b.SendAsync(0x003A, [], ct);
        await FenceAllAsync(ct, a, b, c);
        foreach (var fighter in new[] { a, b })
        {
            var frames = After(fighter, battle).Where(frame => frame.IsNoti && frame.Type is 0x002E or 0x0031 or 0x002F or 0x0030).ToArray();
            Assert.Equal(new[] { 0x002E, 0x0031, 0x002F, 0x0030 }, frames.Select(frame => (int)frame.Type));
            Assert.Equal(new byte[] { seatB, seatA }, frames[0].Body.ToArray());
            Assert.Equal(26 + 19 * 2, frames[2].Body.Length);
            Assert.Equal(51, frames[3].Body.Length);
            Assert.Equal(seatA, frames[2].Body[0]);
        }

        // The records written: once, by the archive's grade rules for a ranked channel.
        var settlement = server.Services.GetRequiredService<IPvpSettlementRuleService>();
        var channel = await server.Services.GetRequiredService<IChannelEntryDomainService>().ResolveAsync(server.GamePort, ct);
        var entitlements = settlement.EntitlementsOf(channel);
        Assert.True(entitlements.RecordsResult);
        var winner = settlement.Decide(new PvpSettlementFacts(before[0].Grade, before[0].Experience, PvpOutcome.Win, false, 0, 0, entitlements));
        var loser = settlement.Decide(new PvpSettlementFacts(before[1].Grade, before[1].Experience, PvpOutcome.Loss, false, 0, 0, entitlements));
        var after = await RecordsAsync(server, ct, a, b);
        Assert.Equal(new StoredRecord(winner.Grade, winner.Experience, 1, 0, 0), after[0]);
        Assert.Equal(new StoredRecord(loser.Grade, loser.Experience, 0, 1, 0), after[1]);
        Assert.Equal(1L, await ScalarAsync(server, "SELECT COUNT(*) FROM pvp_matches", ct));
        foreach (var (fighter, record, decision) in new[] { (a, after[0], winner), (b, after[1], loser) })
        {
            var end = After(fighter, battle).Last(frame => frame.Is(0, 0x002F)).Body;
            Assert.Equal(decision.Grade, end[5]);
            Assert.Equal(decision.ExperienceChange, (long)BinaryPrimitives.ReadInt32LittleEndian(end[6..]));
            var shown = After(fighter, battle).Last(frame => frame.Is(0, 0x0030)).Body;
            Assert.Equal(record.Wins, BinaryPrimitives.ReadInt32LittleEndian(shown));
            Assert.Equal(record.Losses, BinaryPrimitives.ReadInt32LittleEndian(shown[4..]));
            Assert.Equal(record.Experience ?? -1, (long)BinaryPrimitives.ReadInt32LittleEndian(shown[8..]));
            Assert.Equal(record.Grade, shown[20]);
        }

        StepAll("both close the result screen", a, b, c);
        await a.SendAsync(0x0039, [], ct);
        await b.SendAsync(0x0039, [], ct);
        foreach (var client in new[] { a, b, c })
        {
            var state = (await client.ReadUntilAsync(frame => frame.Is(0, 0x002A) && frame.Body[2] == 1, ct)).Body;
            Assert.Equal(number, BinaryPrimitives.ReadUInt16LittleEndian(state));
            Assert.Equal(1, state[2]);
        }
        TestGameplay.Run(server.Services, () =>
        {
            var room = Room(server, a);
            Assert.Equal(PvpRoomPhase.Waiting, room.Phase);
            Assert.Null(room.Match);
        });
        Assert.Equal(1L, await ScalarAsync(server, "SELECT COUNT(*) FROM pvp_matches", ct));
        await AssertLobbyAsync("waiting again", server, c, ++probes, ct);

        // A member leaving returns to town first and only then hears of the room, with no USER_STATE of its own.
        var leaving = Mark(a, b, c);
        StepAll("b leaves", a, b, c);
        await b.SendAsync(0x0034, [seatB, 0xFE], ct);
        await FenceAllAsync(ct, b, a, c);
        var returned = After(b, leaving).Where(frame => frame.IsNoti).ToArray();
        var area = Array.FindIndex(returned, frame => frame.Type == 0x0017);
        var seat = Array.FindIndex(returned, frame => frame.Type == 0x002B);
        Assert.True(area >= 0 && seat > area, "b: " + string.Join(",", returned.Select(frame => frame.Type.ToString("X4"))));
        Assert.DoesNotContain(returned, frame => frame.Type == 0x0003 && frame.Body.Length >= 3
            && BinaryPrimitives.ReadUInt16LittleEndian(frame.Body[1..]) == b.WireUserId);
        Assert.Contains(After(c, leaving), frame => frame.Is(0, 0x0003) && frame.Body.Length >= 3
            && BinaryPrimitives.ReadUInt16LittleEndian(frame.Body[1..]) == b.WireUserId);
        await AssertLobbyAsync("b left", server, c, ++probes, ct);

        StepAll("a leaves; the room is gone", a, b, c);
        await a.SendAsync(0x0034, [seatA, 0xFE], ct);
        await FenceAllAsync(ct, a, b, c);
        Assert.Empty(PvpLobbyWireView.Of(c.Frames).Numbers);
        await AssertLobbyAsync("room gone", server, c, ++probes, ct);
        Assert.False(a.IsClosed || b.IsClosed || c.IsClosed);
    }

    /// <summary>
    /// Spec SA-1 (observed 2026-11-15): an A21 client in an ordinary room sends neither <c>0x012A</c> nor <c>0x012B</c>;
    /// a KO right after the start must still end the battle and settle it.
    /// </summary>
    [PvfFact]
    public async Task A_ko_without_any_loading_or_linking_report_ends_and_settles_the_battle()
    {
        using var timeout = new CancellationTokenSource(Budget);
        var ct = timeout.Token;
        await using var server = await StartAsync(ranked: true, ct);
        using var a = await server.EnterWorldAsync("a", "ko-a", "KoA", ct);
        using var b = await server.EnterWorldAsync("b", "ko-b", "KoB", ct);
        using var c = await server.EnterWorldAsync("c", "ko-c", "KoC", ct);
        await FenceAllAsync(ct, a, b, c);
        var (_, seatA, seatB) = await FightAsync(a, b, c, ct);

        var battle = Mark(a, b, c);
        await b.SendAsync(0x0037, [.. U16(b.WireUserId), .. U16(a.WireUserId), 0], ct);
        await FenceAllAsync(ct, b, a);
        await a.SendAsync(0x003A, [], ct);
        await b.SendAsync(0x003A, [], ct);
        await FenceAllAsync(ct, a, b, c);

        foreach (var fighter in new[] { a, b })
        {
            var frames = After(fighter, battle).Where(frame => frame.IsNoti && frame.Type is 0x002E or 0x0031 or 0x002F or 0x0030).ToArray();
            Assert.Equal(new[] { 0x002E, 0x0031, 0x002F, 0x0030 }, frames.Select(frame => (int)frame.Type));
            Assert.Equal(new byte[] { seatB, seatA }, frames[0].Body.ToArray());
            Assert.Equal(seatA, frames[2].Body[0]);
        }
        var records = await RecordsAsync(server, ct, a, b);
        Assert.Equal((1, 0), (records[0].Wins, records[0].Losses));
        Assert.Equal((0, 1), (records[1].Wins, records[1].Losses));
        Assert.Equal(1L, await ScalarAsync(server, "SELECT COUNT(*) FROM pvp_matches", ct));
        TestGameplay.Run(server.Services, () => Assert.Equal(PvpRoomPhase.AwaitResult, Room(server, a).Phase));
        Assert.False(a.IsClosed || b.IsClosed || c.IsClosed);
    }

    [PvfFact]
    public async Task A_fighter_who_disconnects_loses_as_fled_and_the_survivor_settles_and_waits_again()
    {
        using var timeout = new CancellationTokenSource(Budget);
        var ct = timeout.Token;
        await using var server = await StartAsync(ranked: true, ct);
        using var a = await server.EnterWorldAsync("a", "flee-a", "FleeA", ct);
        var b = await server.EnterWorldAsync("b", "flee-b", "FleeB", ct);
        using var c = await server.EnterWorldAsync("c", "flee-c", "FleeC", ct);
        await FenceAllAsync(ct, a, b, c);
        var (number, seatA, seatB) = await FightAsync(a, b, c, ct);

        var fled = Mark(a, c);
        b.Dispose();
        // The departure is projected by the duel deadline scheduler once the cleanup woke it; no fence bounds it.
        await a.ReadUntilAsync(frame => frame.Is(0, 0x0031), ct);
        await c.ReadUntilAsync(frame => frame.Is(0, 0x002B), ct);
        await FenceAllAsync(ct, a, c);
        var battle = After(a, fled).Where(frame => frame.IsNoti && frame.Type is 0x002E or 0x0031 or 0x002B).ToArray();
        Assert.Equal(new[] { 0x002E, 0x0031 }, battle.Select(frame => (int)frame.Type));
        Assert.Equal(new byte[] { seatB, 0xFF }, battle[0].Body.ToArray());
        // The lobby sees the seat emptied at once; the survivor still on its result screen does not.
        var lobby = PvpLobbyWireView.Of(c.Frames);
        Assert.Equal(-1, lobby.SeatOf(number, b.WireUserId));
        await AssertLobbyAsync("b gone", server, c, 1, ct);

        var settled = Mark(a, c);
        await a.SendAsync(0x003A, [], ct);
        await a.FenceAsync(ct);
        Assert.Equal(new[] { 0x002F, 0x0030 }, After(a, settled).Where(frame => frame.IsNoti && frame.Type is 0x002F or 0x0030)
            .Select(frame => (int)frame.Type));
        Assert.Equal(26 + 19 * 2, After(a, settled).Single(frame => frame.Is(0, 0x002F)).Body.Length);
        Assert.Equal(seatA, After(a, settled).Single(frame => frame.Is(0, 0x002F)).Body[0]);
        var records = await RecordsAsync(server, ct, a, b);
        Assert.Equal((1, 0), (records[0].Wins, records[0].Losses));
        Assert.Equal((0, 1), (records[1].Wins, records[1].Losses));
        Assert.Equal(1L, await ScalarAsync(server,
            $"SELECT fled FROM pvp_match_results WHERE character_id = {b.WireUserId} AND outcome = 1", ct));
        Assert.Equal(0L, await ScalarAsync(server, $"SELECT fled FROM pvp_match_results WHERE character_id = {a.WireUserId}", ct));

        var released = Mark(a, c);
        await a.SendAsync(0x0039, [], ct);
        await FenceAllAsync(ct, a, c);
        // Its own close of the result screen first settles the seat it was owed, then the room waits again.
        var own = After(a, released).Where(frame => frame.IsNoti && frame.Type is 0x002B or 0x002A).ToArray();
        Assert.Equal(new[] { 0x002B, 0x002A, 0x002A }, own.Select(frame => (int)frame.Type));
        Assert.Equal(4 + 8 * 4, own[0].Body.Length);
        Assert.Equal(1, own[^1].Body[2]);
        Assert.Equal(1, After(c, released).Last(frame => frame.Is(0, 0x002A)).Body[2]);
        TestGameplay.Run(server.Services, () =>
        {
            var room = Room(server, a);
            Assert.Equal(PvpRoomPhase.Waiting, room.Phase);
            Assert.Equal(new[] { a.WireUserId }, room.Members.Select(member => member.Member.Session.WireUserId));
            Assert.Equal(seatA, room.OwnerSeat);
        });
        Assert.Equal(PvpLobbyWireView.Of(c.Frames).Render(), PvpLobbyWireView.Of(a.Frames).Render());
        await AssertLobbyAsync("a waits alone", server, c, 2, ct);
        Assert.False(a.IsClosed || c.IsClosed);
    }

    /// <remarks>
    /// Review F-2: a record kept from before the archive's grade table was enforced may hold a grade above its top.
    /// Selection reads the record on every channel, so it must still enter, with the top grade's band.
    /// </remarks>
    [PvfFact]
    public async Task A_character_whose_stored_grade_is_above_the_top_still_enters_with_the_top_grade_s_band()
    {
        using var timeout = new CancellationTokenSource(Budget);
        var ct = timeout.Token;
        await using var server = await StartAsync(ranked: false, ct);
        using var client = await server.EnterWorldAsync("high", "duel-high", "DuelHigh", ct,
            () => server.ExecuteSqlAsync("UPDATE character_pvp_record SET grade = 25, grade_experience = NULL", ct));

        var rules = server.Services.GetRequiredService<IPvpSettlementRuleService>();
        var lower = rules.LowerBound(rules.MaximumGrade);
        var shown = Assert.Single(client.Frames, frame => frame.Is(0, 0x0030)).Body.ToArray();
        Assert.Equal(lower, (long)BinaryPrimitives.ReadInt32LittleEndian(shown.AsSpan(8)));
        Assert.Equal(lower, (long)BinaryPrimitives.ReadInt32LittleEndian(shown.AsSpan(12)));
        Assert.Equal(Math.Min(rules.UpperBound(rules.MaximumGrade), int.MaxValue),
            (long)BinaryPrimitives.ReadInt32LittleEndian(shown.AsSpan(16)));
        Assert.Equal(25, shown[20]);
        Assert.Equal(25L, await ScalarAsync(server, "SELECT grade FROM character_pvp_record", ct));
        Assert.False(client.IsClosed);
    }

    [PvfFact]
    public async Task A_room_invitation_reaches_a_lobby_member_who_accepts_into_the_room_without_the_enter_reply()
    {
        using var timeout = new CancellationTokenSource(Budget);
        var ct = timeout.Token;
        await using var server = await StartAsync(ranked: false, ct);
        using var a = await server.EnterWorldAsync("a", "invite-a", "InviteA", ct);
        using var c = await server.EnterWorldAsync("c", "invite-c", "InviteC", ct);
        await a.SendAsync(0x0032, MakeRoom("invite", 1, null), ct);
        await FenceAllAsync(ct, a, c);
        var number = Assert.Single(PvpLobbyWireView.Of(c.Frames).Numbers);

        var invited = Mark(a, c);
        await a.SendAsync(0x000A, [.. U16(c.WireUserId), 2, 0x34, 0x12, 0, 0], ct);
        await FenceAllAsync(ct, a, c);
        var notices = After(c, invited).Where(frame => frame.IsNoti && frame.Type is 0x0002 or 0x0007).ToArray();
        Assert.Equal(new[] { 0x0002, 0x0007 }, notices.Select(frame => (int)frame.Type));
        byte[] invitation = [.. U16(a.WireUserId), 2, 0x34, 0x12, 0, 0];
        Assert.Equal(invitation, notices[1].Body.ToArray());
        Assert.DoesNotContain(After(a, invited), frame => frame.Is(1, 0x000A));

        var accepted = Mark(a, c);
        await c.SendAsync(0x000B, [.. U16(a.WireUserId), 2, 0x34, 0x12, 0, 0], ct);
        await FenceAllAsync(ct, c, a);
        Assert.DoesNotContain(After(c, accepted), frame => frame.Is(1, 0x0033));
        Assert.DoesNotContain(After(c, accepted), frame => frame.Is(1, 0x000B));
        Assert.Contains(After(c, accepted), frame => frame.Is(0, 0x002B)
            && BinaryPrimitives.ReadUInt16LittleEndian(frame.Body) == number
            && BinaryPrimitives.ReadUInt16LittleEndian(frame.Body[6..]) == c.WireUserId);
        Assert.Contains(After(a, accepted), frame => frame.Is(0, 0x0002));
        TestGameplay.Run(server.Services, () =>
        {
            var room = Room(server, a);
            Assert.Equal(room.Id, Room(server, c).Id);
            Assert.Equal(2, room.Members.Count());
        });
        await AssertLobbyAsync("c invited in", server, a, 1, ct);
    }

    [PvfFact]
    public async Task Rooms_refuse_a_wrong_password_and_a_full_room_and_a_seated_character_refuses_parties()
    {
        using var timeout = new CancellationTokenSource(Budget);
        var ct = timeout.Token;
        await using var server = await StartAsync(ranked: false, ct);
        using var a = await server.EnterWorldAsync("a", "refuse-a", "RefuseA", ct);
        using var b = await server.EnterWorldAsync("b", "refuse-b", "RefuseB", ct);
        using var c = await server.EnterWorldAsync("c", "refuse-c", "RefuseC", ct);
        await a.SendAsync(0x0032, MakeRoom("locked", 1, "secret"), ct);
        await FenceAllAsync(ct, a, b, c);
        var number = Assert.Single(PvpLobbyWireView.Of(c.Frames).Numbers);
        // Close every seat but one, so the room is full with one more member.
        for (byte seat = 2; seat < 8; seat++) await a.SendAsync(0x0034, [seat, 0xFE], ct);
        await FenceAllAsync(ct, a, b, c);
        Assert.DoesNotContain(a.Frames, frame => frame.Is(1, 0x0034));

        var wrong = Mark(b);
        await b.SendAsync(0x0033, EnterRoom(number, "guess"), ct);
        await b.FenceAsync(ct);
        Assert.Equal(new byte[] { 0x00, 0x06 }, After(b, wrong).Single(frame => frame.Is(1, 0x0033)).Body.ToArray());

        var right = Mark(b);
        await b.SendAsync(0x0033, EnterRoom(number, "secret"), ct);
        await FenceAllAsync(ct, b, a, c);
        Assert.Equal(1, After(b, right).Single(frame => frame.Is(1, 0x0033)).Body[0]);

        var full = Mark(c);
        await c.SendAsync(0x0033, EnterRoom(number, "secret"), ct);
        await c.FenceAsync(ct);
        Assert.Equal(new byte[] { 0x00, 0x04 }, After(c, full).Single(frame => frame.Is(1, 0x0033)).Body.ToArray());

        // Spec RQ-015: a party invitation to or from a seated character is refused 0x13, and nothing reaches the other.
        var party = Mark(a, c);
        await c.SendAsync(0x000A, [.. U16(a.WireUserId), 0, 0x40, 0, 0, 0, 0, 0], ct);
        await FenceAllAsync(ct, c, a);
        await a.SendAsync(0x000A, [.. U16(c.WireUserId), 0, 0x40, 0, 0, 0, 0, 0], ct);
        await FenceAllAsync(ct, a, c);
        Assert.Equal(new byte[] { 0x00, 0x13, 0x00 }, After(c, party).Single(frame => frame.Is(1, 0x000A)).Body.ToArray());
        Assert.Equal(new byte[] { 0x00, 0x13, 0x00 }, After(a, party).Single(frame => frame.Is(1, 0x000A)).Body.ToArray());
        Assert.DoesNotContain(After(a, party).Concat(After(c, party)), frame => frame.Is(0, 0x0007));
        TestGameplay.Run(server.Services, () => Assert.Equal(2, Room(server, a).Members.Count()));
        await AssertLobbyAsync("full", server, c, 1, ct);
    }

    [PvfFact]
    public async Task Quick_join_uses_the_first_available_room_and_replies_when_none_is_available()
    {
        using var timeout = new CancellationTokenSource(Budget);
        var ct = timeout.Token;
        await using var server = await StartAsync(ranked: true, ct);
        using var locked = await server.EnterWorldAsync("locked", "quick-locked", "QuickLocked", ct);
        using var entrant = await server.EnterWorldAsync("entrant", "quick-entrant", "QuickEntrant", ct);
        await locked.SendAsync(0x0032, MakeRoom("locked", 1, "secret"), ct);
        await FenceAllAsync(ct, locked, entrant);

        var denied = Mark(entrant);
        await entrant.SendAsync(0x0062, [0], ct);
        await entrant.FenceAsync(ct);
        Assert.Equal(new byte[] { 0, 0xBF },
            Assert.Single(After(entrant, denied), frame => frame.Is(1, 0x0062)).Body.ToArray());

        using var first = await server.EnterWorldAsync("first", "quick-first", "QuickFirst", ct);
        using var second = await server.EnterWorldAsync("second", "quick-second", "QuickSecond", ct);
        await first.SendAsync(0x0032, MakeRoom("first", 1, null), ct);
        await first.FenceAsync(ct);
        await second.SendAsync(0x0032, MakeRoom("second", 1, null), ct);
        await FenceAllAsync(ct, second, entrant);

        var joined = Mark(entrant);
        await entrant.SendAsync(0x0062, [0], ct);
        await FenceAllAsync(ct, entrant, first);
        Assert.Equal(new byte[] { 1 },
            Assert.Single(After(entrant, joined), frame => frame.Is(1, 0x0062)).Body.ToArray());
        Assert.Equal(1, Assert.Single(After(entrant, joined), frame => frame.Is(1, 0x0033)).Body[0]);
        var firstRoom = TestGameplay.Run(server.Services, () => Room(server, first));
        Assert.NotNull(firstRoom.SeatOf(new CharacterId(entrant.WireUserId)));

        for (byte seat = 2; seat < 8; seat++)
            await first.SendAsync(0x0034, [seat, 0xFE], ct);
        await first.FenceAsync(ct);
        await locked.SendAsync(0x0034, [0, 0xFE], ct);
        await locked.FenceAsync(ct);
        await locked.SendAsync(0x0062, [0], ct);
        await FenceAllAsync(ct, locked, second);
        var secondRoom = TestGameplay.Run(server.Services, () => Room(server, second));
        Assert.NotNull(secondRoom.SeatOf(new CharacterId(locked.WireUserId)));
    }

    [PvfFact]
    public async Task Practice_room_is_private_and_can_be_left_without_recording_a_match()
    {
        const int luckyStar = 10002998;
        using var timeout = new CancellationTokenSource(Budget);
        var ct = timeout.Token;
        await using var server = await StartAsync(ranked: true, ct);
        await server.ExecuteSqlAsync(
            "INSERT INTO initialization_settings(kind,revision,input,updated_at,updated_by) VALUES('character',1," +
            $"'{{\"Level\":1,\"PersonalCargoLevel\":8,\"Items\":[{{\"ItemId\":{luckyStar},\"Count\":2}}]}}',0,'PvpDuelWireTests');", ct);
        using var owner = await server.EnterWorldAsync("practice", "practice-owner", "Practice", ct);
        using var lobby = await server.EnterWorldAsync("lobby", "practice-lobby", "Lobby", ct);

        var created = Mark(owner, lobby);
        var request = MakeRoom("practice", 0, null);
        request[^1] = 1;
        await owner.SendAsync(0x0032, request, ct);
        await FenceAllAsync(ct, owner, lobby);
        Assert.Contains(After(owner, created), frame => frame.Is(0, 0x0029));
        Assert.DoesNotContain(After(lobby, created), frame => frame.Is(0, 0x0029));
        var practice = TestGameplay.Run(server.Services, () => Room(server, owner));
        Assert.Equal(PvpBattleMode.Practice, practice.Mode);
        Assert.Single(practice.Members);

        var itemStack = "FROM item_slots s JOIN items i ON s.core_uid=i.item_uid " +
            $"WHERE s.owner_scope='character' AND s.owner_id={owner.WireUserId} AND s.list_type=0 " +
            $"AND substr(hex(i.item_core),3,8)='{Convert.ToHexString(BitConverter.GetBytes(luckyStar))}'";
        var itemSlot = checked((short)await ScalarAsync(server, "SELECT s.slot_index " + itemStack, ct));
        var itemRequest = new byte[11];
        BinaryPrimitives.WriteInt16LittleEndian(itemRequest, itemSlot);
        BinaryPrimitives.WriteInt32LittleEndian(itemRequest.AsSpan(7), luckyStar);
        Assert.Equal(2L, await ScalarAsync(server, "SELECT s.count " + itemStack, ct));
        await owner.SendAsync(0x002C, itemRequest, ct);
        var itemAnswer = await owner.ReadUntilAsync(frame => frame.Is(1, 0x002C), ct);
        Assert.Equal(0, itemAnswer.Body[0]);
        Assert.Equal(2L, await ScalarAsync(server, "SELECT s.count " + itemStack, ct));

        using var fresh = await server.EnterWorldAsync("fresh", "practice-fresh", "Fresh", ct);
        Assert.Empty(PvpLobbyWireView.Of(fresh.Frames).Numbers);

        await lobby.SendAsync(0x0033, EnterRoom(practice.Number, null), ct);
        await lobby.SendAsync(0x0062, [0], ct);
        await lobby.FenceAsync(ct);
        Assert.Equal(new byte[] { 0, 22 }, Assert.Single(lobby.Frames, frame => frame.Is(1, 0x0033)).Body.ToArray());
        Assert.Equal(new byte[] { 0, 0xBF }, Assert.Single(lobby.Frames, frame => frame.Is(1, 0x0062)).Body.ToArray());

        var ready = Mark(owner);
        await owner.SendAsync(0x0035, [1], ct);
        await owner.FenceAsync(ct);
        Assert.DoesNotContain(After(owner, ready), frame => frame.Is(1, 0x0035) || frame.Is(0, 0x002D));

        var left = Mark(owner, lobby);
        await owner.SendAsync(0x0034, [0, 0xFE], ct);
        await FenceAllAsync(ct, owner, lobby);
        Assert.Empty(PvpLobbyWireView.Of(owner.Frames).Numbers);
        Assert.DoesNotContain(After(lobby, left), frame => frame.Is(0, 0x002A) || frame.Is(0, 0x002B));
        Assert.Equal(0L, await ScalarAsync(server, "SELECT COUNT(*) FROM pvp_matches", ct));
        Assert.Equal(0L, await ScalarAsync(server, "SELECT SUM(win + lose + draw) FROM character_pvp_record", ct));

        await lobby.SendAsync(0x0032, MakeRoom("ordinary", 1, null), ct);
        await FenceAllAsync(ct, lobby, owner);
        await owner.SendAsync(0x0062, [0], ct);
        await FenceAllAsync(ct, owner, lobby);
        Assert.Contains(owner.Frames, frame => frame.Is(1, 0x0062) && frame.Body[0] == 1);
        Assert.Equal(PvpBattleMode.Team, TestGameplay.Run(server.Services, () => Room(server, owner).Mode));
    }

    [PvfTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Free_duel_total_match_rotates_three_actors_and_settles_once(bool guestTimesOut)
    {
        using var timeout = new CancellationTokenSource(Budget);
        var ct = timeout.Token;
        var clock = new RelayTestClock();
        await using var server = await CharacterizationHost.StartAsync(new CharacterizationOptions
        {
            Decorate = services =>
            {
                services.Decorate<IChannelEntryDomainService>((_, inner) => new PvpChannel(inner, 13, ChannelEntryKind.FreeDuel));
                services.AddSingleton<TimeProvider>(clock);
            },
        }, ct);
        var ownerLogin = await server.RegisterAndIssueTicketAsync("total-room-a", ct);
        using var owner = await server.ConnectAsync("a", ct);
        await owner.LoginAsync(ownerLogin.MId, ownerLogin.Ticket, ct);
        await ConfigureTeamAsync(owner, "TotalRoomA");
        var guestLogin = await server.RegisterAndIssueTicketAsync("total-room-b", ct);
        using var guest = await server.ConnectAsync("b", ct);
        await guest.LoginAsync(guestLogin.MId, guestLogin.Ticket, ct);
        await ConfigureTeamAsync(guest, "TotalRoomB");
        await EquipCreatureAsync(owner, rosterIndex: 1);
        await EquipCreatureAsync(guest, rosterIndex: 0);
        await owner.SendAsync(0x0032, MakeRoom("total", 1, null), ct);
        await FenceAllAsync(ct, owner, guest);
        var number = Assert.Single(PvpLobbyWireView.Of(guest.Frames).Numbers);
        await guest.SendAsync(0x0033, EnterRoom(number, null), ct);
        await FenceAllAsync(ct, owner, guest);
        Assert.DoesNotContain(owner.Frames.Concat(guest.Frames), frame => frame.Is(0, 0x031A));

        var mark = Mark(owner, guest);
        await owner.SendAsync(0x0036, [8], ct);
        await FenceAllAsync(ct, owner, guest);

        var room = TestGameplay.Run(server.Services, () => Room(server, owner));
        Assert.Equal(PvpBattleMode.TotalMatch, room.Mode);
        Assert.Equal([PvpTeam.First, PvpTeam.Second], room.Members.Select(x => x.Member.Team));
        foreach (var client in new[] { owner, guest })
        {
            var modeAndCandidates = After(client, mark)
                .Where(frame => frame.Is(0, 0x002B) && frame.Body[2] == 8 || frame.Is(0, 0x031A)).ToArray();
            Assert.True(modeAndCandidates.Length >= 2);
            Assert.True(modeAndCandidates[0].Is(0, 0x002B));
            AssertCandidates(client, modeAndCandidates, client == owner ? "TotalRoomA" : "TotalRoomB", 1, 2);
        }

        // A21 clears ready after switching modes, even when the room already has the seat unready.
        mark = Mark(owner, guest);
        foreach (var (client, seat) in new[] { (owner, (byte)0), (guest, (byte)1) })
        {
            await client.SendAsync(0x0035, [0], ct);
            await client.FenceAsync(ct);
            Assert.Contains(After(client, mark), frame => frame.Is(0, 0x002C) && frame.Body.SequenceEqual(new byte[] { seat, 0 }));
        }
        await guest.SendAsync(0x0035, [1], ct);
        await guest.ReadUntilAsync(frame => frame.Is(0, 0x002C) && frame.Body.SequenceEqual(new byte[] { 1, 1 }), ct);
        await guest.SendAsync(0x0035, [0], ct);
        await guest.ReadUntilAsync(frame => frame.Is(0, 0x002C) && frame.Body.SequenceEqual(new byte[] { 1, 0 }), ct);

        mark = Mark(owner, guest);
        await guest.SendAsync(0x035E, [.. WireClient.DStrings("TotalRoomB"), 0, 2, 1], ct);
        await FenceAllAsync(ct, owner, guest);
        var savedTeamFrames = After(guest, mark)
            .Where(frame => frame.Is(0, 0x031B) || frame.Is(1, 0x035E) || frame.Is(0, 0x031A)).ToArray();
        Assert.Collection(savedTeamFrames,
            frame => Assert.True(frame.Is(0, 0x031B)),
            frame =>
            {
                Assert.True(frame.Is(1, 0x035E));
                Assert.Equal(new byte[] { 1, 0, 2, 1 }, frame.Body.ToArray());
            },
            frame => Assert.True(frame.Is(0, 0x031A)));
        AssertCandidates(guest, savedTeamFrames, "TotalRoomB", 2, 1);
        Assert.DoesNotContain(After(owner, mark), frame => frame.Is(0, 0x031A));

        var guestSeat = checked((byte)room.Members.Single(entry => entry.Member.Session.WireUserId == guest.WireUserId).Seat);
        mark = Mark(owner, guest);
        await guest.SendAsync(0x0035, [1], ct);
        await FenceAllAsync(ct, guest, owner);
        foreach (var client in new[] { owner, guest })
            Assert.Contains(After(client, mark), frame => frame.Is(0, 0x002C)
                && frame.Body.SequenceEqual(new byte[] { guestSeat, 1 }));

        await guest.SendAsync(0x0034, [guestSeat, 0xFE], ct);
        await FenceAllAsync(ct, owner, guest);
        mark = Mark(owner, guest);
        await guest.SendAsync(0x0033, EnterRoom(number, null), ct);
        await FenceAllAsync(ct, owner, guest);
        AssertCandidates(guest, After(guest, mark), "TotalRoomB", 2, 1);
        Assert.DoesNotContain(After(owner, mark), frame => frame.Is(0, 0x031A));

        await guest.SendAsync(0x0035, [1], ct);
        await FenceAllAsync(ct, owner, guest);
        room = TestGameplay.Run(server.Services, () => Room(server, owner));
        Assert.True(room.Members.Single(entry => entry.Member.Session.WireUserId == guest.WireUserId).Member.Ready);

        mark = Mark(owner, guest);
        await owner.SendAsync(0x0035, [1], ct);
        await FenceAllAsync(ct, owner, guest);
        foreach (var client in new[] { owner, guest })
        {
            var opening = After(client, mark)
                .Where(frame => frame.Is(0, 0x031A) || frame.Is(0, 0x031C) || frame.Is(0, 0x002D)).ToArray();
            Assert.Collection(opening,
                frame =>
                {
                    Assert.True(frame.Is(0, 0x031A));
                    Assert.Equal(2, frame.Body[0]);
                    foreach (var candidateName in new[] { "TotalRoomA0", "TotalRoomA1", "TotalRoomA2", "TotalRoomB0", "TotalRoomB1", "TotalRoomB2" })
                        Assert.True(frame.Body.IndexOf(WireClient.DStrings(candidateName)) >= 0);
                },
                frame =>
                {
                    Assert.True(frame.Is(0, 0x031C));
                    Assert.Equal(2, frame.Body[0]);
                    Assert.Equal((uint)owner.WireUserId, BinaryPrimitives.ReadUInt32LittleEndian(frame.Body[1..]));
                },
                frame =>
                {
                    Assert.True(frame.Is(0, 0x002D));
                    Assert.Equal(8, frame.Body[1]);
                });
            Assert.DoesNotContain(After(client, mark), frame => frame.Is(0, 0x031E));
        }
        await guest.SendAsync(0x0361, [1], ct);
        await guest.FenceAsync(ct);
        clock.Advance(2, server);
        foreach (var client in new[] { owner, guest })
        {
            var selection = await client.ReadUntilAsync(frame => frame.Is(0, 0x031E), ct);
            Assert.Equal(0, selection.Body.Length);
        }
        mark = Mark(owner, guest);
        await owner.SendAsync(0x0361, [3], ct);
        await owner.SendAsync(0x0361, [2], ct);
        await owner.SendAsync(0x0361, [1], ct);
        await FenceAllAsync(ct, owner, guest);
        Assert.DoesNotContain(After(owner, mark), frame => frame.Is(0, 0x031D));
        if (guestTimesOut)
            clock.Advance(6, server);
        else
            await guest.SendAsync(0x0361, [2], ct);
        foreach (var client in new[] { owner, guest })
        {
            var fight = await client.ReadUntilAsync(frame => frame.Is(0, 0x031D), ct);
            Assert.Equal(10, fight.Body.Length);
            Assert.Equal((uint)owner.WireUserId, BinaryPrimitives.ReadUInt32LittleEndian(fight.Body));
            Assert.Equal(2, fight.Body[4]);
            Assert.Equal((uint)guest.WireUserId, BinaryPrimitives.ReadUInt32LittleEndian(fight.Body[5..]));
            Assert.Equal(guestTimesOut ? 0 : 2, fight.Body[9]);
        }
        room = TestGameplay.Run(server.Services, () => Room(server, owner));
        Assert.Equal(PvpTotalMatchPhase.Fighting, Assert.IsType<PvpTotalMatchBattle>(room.Match?.ModeState).Phase);
        var matchId = room.Match!.Id;
        for (var round = 0; round < 3; round++)
        {
            var bothEliminated = round == 1 && guestTimesOut;
            var killer = round == 1 ? ushort.MaxValue : owner.WireUserId;
            byte[] report = [.. U16(guest.WireUserId), .. U16(killer), (byte)(round == 1 ? 0 : 1)];
            if (round == 1)
            {
                // Keep both representatives connected through the native round timer.
                for (var elapsed = 0; elapsed <= 180; elapsed += 20)
                {
                    await owner.SendAsync(0x006E, [], ct);
                    await guest.SendAsync(0x006E, [], ct);
                    await FenceAllAsync(ct, owner, guest);
                    if (elapsed < 180) clock.Advance(20, server);
                }
            }
            clock.Advance(8, server);
            var currentMatch = TestGameplay.Run(server.Services, () => Room(server, owner).Match);
            Assert.NotNull(currentMatch);
            var currentRound = Assert.IsType<PvpTotalMatchBattle>(currentMatch.ModeState);
            Assert.Equal(PvpTotalMatchPhase.Fighting, currentRound.Phase);
            Assert.True(clock.GetUtcNow() >= currentRound.RoundOpensAt && clock.GetUtcNow() < currentMatch.PhaseDeadline,
                $"Round {round + 1} started at {currentRound.RoundOpensAt:o}, deadline {currentMatch.PhaseDeadline:o}, now {clock.GetUtcNow():o}.");
            mark = Mark(owner, guest);
            await guest.SendAsync(0x0037, report, ct);
            await guest.SendAsync(0x0037, report, ct);
            await guest.FenceAsync(ct);
            if (bothEliminated)
            {
                await owner.SendAsync(0x0037, [.. U16(owner.WireUserId), 0xFF, 0xFF, 0], ct);
                await owner.FenceAsync(ct);
            }
            var afterKo = TestGameplay.Run(server.Services, () => Room(server, owner).Match);
            Assert.Equal(PvpTotalMatchPhase.BetweenRounds, Assert.IsType<PvpTotalMatchBattle>(afterKo!.ModeState).Phase);
            Assert.Equal(round + 1, afterKo.Fighters.Single(fighter => fighter.Seat == guestSeat).Deaths);
            foreach (var client in new[] { owner, guest })
            {
                if (!After(client, mark).Any(frame => frame.Is(0, 0x002E) && frame.Body[0] == guestSeat))
                    await client.ReadUntilAsync(frame => frame.Is(0, 0x002E) && frame.Body[0] == guestSeat, ct);
                if (bothEliminated && !After(client, mark).Any(frame => frame.Is(0, 0x002E) && frame.Body[0] == 0))
                    await client.ReadUntilAsync(frame => frame.Is(0, 0x002E) && frame.Body[0] == 0, ct);
            }
            await FenceAllAsync(ct, guest, owner);
            foreach (var client in new[] { owner, guest })
            {
                var notices = After(client, mark).Where(frame => frame.Is(0, 0x002E)).ToArray();
                Assert.Equal(bothEliminated ? 2 : 1, notices.Length);
                var death = Assert.Single(notices, frame => frame.Body[0] == guestSeat);
                Assert.Equal(new byte[] { guestSeat, round == 1 ? (byte)0xFF : (byte)0 }, death.Body.ToArray());
                Assert.DoesNotContain(After(client, mark), frame => frame.Is(0, 0x0031));
            }
            Assert.Equal(0L, await ScalarAsync(server, "SELECT COUNT(*) FROM pvp_matches", ct));

            clock.Advance(3, server);
            if (round == 2) break;
            foreach (var client in new[] { owner, guest })
                await client.ReadUntilAsync(frame => frame.Is(0, 0x031E), ct);

            var eliminated = (byte)(round == 0 ? (guestTimesOut ? 0 : 2) : 1);
            mark = Mark(owner, guest);
            await owner.SendAsync(0x0361, [bothEliminated ? (byte)2 : (byte)0], ct);
            await guest.SendAsync(0x0361, [eliminated], ct);
            await FenceAllAsync(ct, owner, guest);
            Assert.DoesNotContain(After(owner, mark), frame => frame.Is(0, 0x031D));
            if (round == 0)
                await guest.SendAsync(0x0361, [1], ct);
            else
            {
                if (bothEliminated)
                {
                    await owner.SendAsync(0x0361, [1], ct);
                    await owner.FenceAsync(ct);
                }
                clock.Advance(6, server);
            }

            foreach (var client in new[] { owner, guest })
            {
                var fight = await client.ReadUntilAsync(frame => frame.Is(0, 0x031D), ct);
                Assert.Equal(bothEliminated ? 1 : 2, fight.Body[4]);
                Assert.Equal(round == 0 ? 1 : guestTimesOut ? 2 : 0, fight.Body[9]);
            }
            Assert.Equal(matchId, TestGameplay.Run(server.Services, () => Room(server, owner).Match?.Id));
        }

        foreach (var client in new[] { owner, guest })
            await client.ReadUntilAsync(frame => frame.Is(0, 0x0031), ct);
        var conclusion = TestGameplay.Run(server.Services, () => server.Services.GetRequiredService<IPvpRoomDomainService>()
            .PendingConclusions().Single(item => item.MatchId == matchId));
        var ownerAppearances = conclusion.TotalMatchAppearances.Where(role => role.Seat == 0).ToArray();
        var guestAppearances = conclusion.TotalMatchAppearances.Where(role => role.Seat == guestSeat).ToArray();
        Assert.Equal(guestTimesOut ? 2 : 1, ownerAppearances.Length);
        Assert.Equal(2, ownerAppearances.Sum(role => role.Kills));
        Assert.Equal(guestTimesOut ? 1 : 0, ownerAppearances.Sum(role => role.Deaths));
        Assert.Equal(3, guestAppearances.Length);
        Assert.All(guestAppearances, role => Assert.Equal((0, 1), (role.Kills, role.Deaths)));
        var representative = conclusion.Fighters.Single(fighter => fighter.Seat == 0).CharacterId;
        Assert.DoesNotContain(representative, ownerAppearances.Select(role => role.Character));
        await owner.SendAsync(0x003A, [], ct);
        await guest.SendAsync(0x003A, [], ct);
        foreach (var client in new[] { owner, guest })
            Assert.Equal(0, (await client.ReadUntilAsync(frame => frame.Is(0, 0x002F), ct)).Body[0]);
        Assert.Equal(1L, await ScalarAsync(server, "SELECT COUNT(*) FROM pvp_matches WHERE battle_mode=8", ct));
        Assert.Equal(2L, await ScalarAsync(server, "SELECT COUNT(*) FROM pvp_match_results", ct));
        Assert.Equal(0L, await ScalarAsync(server, "SELECT COUNT(*) FROM pvp_match_results WHERE experience_change != 0", ct));

        await owner.SendAsync(0x0039, [], ct);
        await guest.SendAsync(0x0039, [], ct);
        await owner.ReadUntilAsync(frame => frame.Is(0, 0x002A) && frame.Body[2] == 1, ct);
        var waitingRoom = TestGameplay.Run(server.Services, () => Room(server, owner));
        Assert.Equal(number, waitingRoom.Number);
        Assert.Equal(PvpRoomPhase.Waiting, waitingRoom.Phase);
        Assert.Null(waitingRoom.Match);
        Assert.All(waitingRoom.Members, member => Assert.False(member.Member.Ready));

        async Task ConfigureTeamAsync(WireClient client, string name)
        {
            await client.CreateCharacterAsync(name + "0", ct);
            await client.CreateCharacterAsync(name + "1", ct, job: 1);
            await client.CreateCharacterAsync(name + "2", ct, job: 2);
            await client.SelectCharacterAsync(ct);
            await client.SendAsync(0x035E, [.. WireClient.DStrings(name), 0, 1, 2], ct);
            await client.FenceAsync(ct);
            Assert.Contains(client.Frames, frame => frame.Is(1, 0x035E) && frame.Body[0] == 1);
        }

        Task EquipCreatureAsync(WireClient client, ushort rosterIndex) => TestGameplay.RunAsync(server.Services, async () =>
        {
            var services = server.Services;
            Assert.True(services.GetRequiredService<ISelectedSessionDomainService>()
                .TryGetInWorld(client.WireUserId, out var selected));
            var character = await services.GetRequiredService<ICharacterRosterDomainService>()
                .LoadByRosterPositionAsync(selected.AccountId, rosterIndex, ct);
            var scope = services.GetRequiredService<DomainTransactionScope>();
            await using var transaction = scope.Begin();
            var store = services.GetRequiredService<IItemDomainStore>().SessionIn(scope.Current!);
            var pet = await store.CreateAsync(ItemSnapshot.FromCore(StackableItemCore.Create(5, 63000, 1), false,
                creature: new(50, 1, 0, 0, 0, 1, [], 0)), ct);
            await store.PutAsync(ContainerId.ForCharacter(character.Identity.CharacterId, InventoryContainerType.Equipment),
                25, pet, ct);
            await transaction.CommitAsync(ct);
        });

        static void AssertCandidates(WireClient client, IEnumerable<WireFrame> frames, string name, int second, int third)
        {
            var body = Assert.Single(frames, frame => frame.Is(0, 0x031A)).Body;
            Assert.Equal(1, body[0]);
            Assert.Equal(client.WireUserId, BinaryPrimitives.ReadUInt16LittleEndian(body[1..]));
            Assert.Equal(3, body[3]);
            Assert.True(body.IndexOf(new byte[] { 25, 0x18, 0xF6, 0, 0 }) >= 0); // Worn creature slot and template 63000.
            var offset = 4;
            foreach (var (ordinal, role) in new[] { (0, 0), (1, second), (2, third) })
            {
                byte[] prefix = [(byte)ordinal, .. WireClient.DStrings(name + role), 1, (byte)role, 0];
                var next = body[offset..].IndexOf(prefix);
                Assert.True(next >= 0);
                if (ordinal == 0) Assert.Equal(0, next);
                offset += next + prefix.Length;
            }
        }
    }

    [PvfFact]
    public async Task Relay_turns_stay_in_one_match_across_a_ko_and_a_disconnect()
    {
        using var timeout = new CancellationTokenSource(Budget);
        var ct = timeout.Token;
        var clock = new RelayTestClock();
        await using var server = await StartAsync(ranked: true, ct, clock);
        using var a = await server.EnterWorldAsync("a", "relay-a", "RelayA", ct);
        using var b = await server.EnterWorldAsync("b", "relay-b", "RelayB", ct);
        using var c = await server.EnterWorldAsync("c", "relay-c", "RelayC", ct);
        using var d = await server.EnterWorldAsync("d", "relay-d", "RelayD", ct);
        WireClient[] fighters = [a, b, c, d];

        await a.SendAsync(0x0032, MakeRoom("relay", 1, null), ct);
        await FenceAllAsync(ct, fighters);
        var room = Assert.Single(PvpLobbyWireView.Of(b.Frames).Numbers);
        await a.SendAsync(0x0036, [3], ct);
        await FenceAllAsync(ct, fighters);
        foreach (var guest in fighters.Skip(1))
        {
            await guest.SendAsync(0x0033, EnterRoom(room, null), ct);
            await guest.SendAsync(0x0035, [1], ct);
            await guest.FenceAsync(ct);
        }
        await a.SendAsync(0x0035, [1], ct);
        await FenceAllAsync(ct, fighters);
        foreach (var fighter in fighters)
        {
            Assert.Equal(new byte[] { 1, 3 }, Assert.Single(fighter.Frames, frame => frame.Is(0, 0x002D)).Body.ToArray());
            Assert.DoesNotContain(fighter.Frames, frame => frame.Is(0, 0x0070));
        }

        clock.Advance(7, server);
        foreach (var fighter in fighters)
            Assert.Equal(new byte[] { 2, 0, 0, 1, 0 }, (await fighter.ReadUntilAsync(frame => frame.Is(0, 0x0070), ct)).Body.ToArray());

        await a.SendAsync(0x0070, [], ct);
        await d.SendAsync(0x0070, [1], ct);
        await FenceAllAsync(ct, a, d);
        Assert.DoesNotContain(a.Frames, frame => frame.Is(0, 0x0071));
        await d.SendAsync(0x0070, [], ct);
        foreach (var fighter in fighters)
            Assert.Equal(new byte[] { 3 }, (await fighter.ReadUntilAsync(frame => frame.Is(0, 0x0071), ct)).Body.ToArray());

        await b.SendAsync(0x0037, [.. U16(b.WireUserId), .. U16(a.WireUserId), 0], ct);
        await FenceAllAsync(ct, b, a);
        Assert.Contains(a.Frames, frame => frame.Is(0, 0x002E));
        Assert.DoesNotContain(a.Frames, frame => frame.Is(0, 0x0031));
        Assert.Equal(0L, await ScalarAsync(server, "SELECT COUNT(*) FROM pvp_matches", ct));
        clock.Advance(3, server);
        foreach (var fighter in fighters)
            Assert.Equal(new byte[] { 2, 0, 0, 3, 0 }, (await fighter.ReadUntilAsync(frame => frame.Is(0, 0x0070), ct)).Body.ToArray());

        a.Dispose();
        await c.ReadUntilAsync(frame => frame.Is(0, 0x002E) && frame.Body[0] == 0, ct);
        clock.Advance(3, server);
        Assert.Equal(new byte[] { 2, 2, 0, 3, 0 }, (await c.ReadUntilAsync(frame => frame.Is(0, 0x0070), ct)).Body.ToArray());

        await d.SendAsync(0x0037, [.. U16(d.WireUserId), .. U16(c.WireUserId), 0], ct);
        await c.ReadUntilAsync(frame => frame.Is(0, 0x0031), ct);
        foreach (var fighter in new[] { b, c, d }) await fighter.SendAsync(0x003A, [], ct);
        var result = await c.ReadUntilAsync(frame => frame.Is(0, 0x002F), ct);
        Assert.Equal(2, result.Body[0]);
        Assert.Equal(1L, await ScalarAsync(server, "SELECT COUNT(*) FROM pvp_matches WHERE battle_mode=3", ct));
        Assert.Equal(4L, await ScalarAsync(server, "SELECT COUNT(*) FROM pvp_match_results", ct));
        Assert.Equal(1L, await ScalarAsync(server, "SELECT COUNT(*) FROM pvp_match_results WHERE fled=1", ct));

        foreach (var fighter in new[] { b, c, d }) await fighter.SendAsync(0x0039, [], ct);
        await c.ReadUntilAsync(frame => frame.Is(0, 0x002A) && frame.Body[2] == 1, ct);
        Assert.Equal(PvpRoomPhase.Waiting, TestGameplay.Run(server.Services, () => Room(server, c).Phase));
    }

    /// <summary>A room made by <paramref name="a"/>, entered by <paramref name="b"/> and started: fighting at once (spec SA-1).</summary>
    private static async Task<(ushort Number, byte SeatA, byte SeatB)> FightAsync(WireClient a, WireClient b, WireClient c,
        CancellationToken ct)
    {
        await a.SendAsync(0x0032, MakeRoom("fight", 1, null), ct);
        await FenceAllAsync(ct, a, b, c);
        var number = Assert.Single(PvpLobbyWireView.Of(c.Frames).Numbers);
        await b.SendAsync(0x0033, EnterRoom(number, null), ct);
        await FenceAllAsync(ct, b, a, c);
        var lobby = PvpLobbyWireView.Of(c.Frames);
        var (seatA, seatB) = ((byte)lobby.SeatOf(number, a.WireUserId), (byte)lobby.SeatOf(number, b.WireUserId));
        await b.SendAsync(0x0035, [1], ct);
        await FenceAllAsync(ct, b, a);
        await a.SendAsync(0x0035, [1], ct);
        await FenceAllAsync(ct, a, b, c);
        Assert.Contains(b.Frames, frame => frame.Is(0, 0x002D));
        return (number, seatA, seatB);
    }

    private static Task<CharacterizationHost> StartAsync(
        bool ranked, CancellationToken ct, TimeProvider? clock = null, bool totalMatchmaking = false) =>
        CharacterizationHost.StartAsync(new CharacterizationOptions
        {
            Decorate = services =>
            {
                services.Decorate<IChannelEntryDomainService>((_, inner) => ranked
                    ? new PvpChannel(inner, 24, ChannelEntryKind.TraditionalDuel)
                    : new PvpChannel(inner, 8, totalMatchmaking ? ChannelEntryKind.TotalMatchDuel : ChannelEntryKind.MatchmakingDuel));
                if (clock is not null) services.AddSingleton(clock);
            },
        }, ct);

    private sealed class RelayTestClock(int dayOffset = 0) : TimeProvider
    {
        private int seconds;

        public override DateTimeOffset GetUtcNow() => CharacterizationHost.Now.AddDays(dayOffset).AddSeconds(Volatile.Read(ref seconds));

        public void Advance(int elapsed, CharacterizationHost server)
        {
            Interlocked.Add(ref seconds, elapsed);
            server.Services.GetRequiredService<IPvpDeadlineWake>().Wake();
        }
    }

    /// <summary>
    /// The lobby <paramref name="observer"/> accumulated from increments equals the snapshot a session entering the lobby
    /// now is given (Spec RQ-003, AC-003). The probe session leaves again at once.
    /// </summary>
    private static async Task AssertLobbyAsync(string step, CharacterizationHost server, WireClient observer, int probe,
        CancellationToken ct)
    {
        using var fresh = await server.EnterWorldAsync("probe", "p" + Guid.NewGuid().ToString("N")[..12],
            "Probe" + probe, ct);
        Assert.True(fresh.Frames.Count(frame => frame.Is(0, 0x0029)) >= 1, $"{step}: the entry carried no room snapshot.");
        Assert.Equal(PvpLobbyWireView.Of(fresh.Frames).Render(), PvpLobbyWireView.Of(observer.Frames).Render());
    }

    private static PvpRoomSnapshot Room(CharacterizationHost server, WireClient member) =>
        server.Services.GetRequiredService<IPvpRoomQueryDomainService>().FindSeated(new CharacterId(member.WireUserId))
            ?? throw new InvalidOperationException($"{member.Label} holds no duel seat.");

    private sealed record StoredRecord(byte Grade, long? Experience, int Wins, int Losses, int Draws);

    private static async Task<StoredRecord[]> RecordsAsync(CharacterizationHost server, CancellationToken ct, params WireClient[] clients)
    {
        await using var connection = new SqliteConnection($"Data Source={server.DatabasePath};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync(ct);
        var records = new List<StoredRecord>();
        foreach (var client in clients)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT grade, grade_experience, win, lose, draw FROM character_pvp_record WHERE character_id = $id";
            command.Parameters.AddWithValue("$id", (int)client.WireUserId);
            await using var reader = await command.ExecuteReaderAsync(ct);
            Assert.True(await reader.ReadAsync(ct), $"{client.Label} has no duel record row.");
            records.Add(new StoredRecord((byte)reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetInt64(1),
                reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4)));
        }
        return [.. records];
    }

    private static async Task<long> ScalarAsync(CharacterizationHost server, string sql, CancellationToken ct)
    {
        await using var connection = new SqliteConnection($"Data Source={server.DatabasePath};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct));
    }

    private static Dictionary<WireClient, int> Mark(params WireClient[] clients) =>
        clients.ToDictionary(client => client, client => client.Frames.Count());

    private static IEnumerable<WireFrame> After(WireClient client, Dictionary<WireClient, int> mark) =>
        client.Frames.Skip(mark[client]);

    private static void AssertMatchedRoomRetiredBeforeTown(WireClient client, Dictionary<WireClient, int> mark, byte seatCount)
    {
        var frames = After(client, mark).Where(frame => frame.IsNoti).ToArray();
        var seats = Array.FindIndex(frames, frame => frame.Type == 0x002B);
        var destroyed = Array.FindIndex(frames, frame => frame.Type == 0x002A && frame.Body[2] == 0);
        var town = Array.FindIndex(frames, frame => frame.Type == 0x0017);
        Assert.DoesNotContain(frames, frame => frame.Type == 0x002A && frame.Body[2] == 1);
        Assert.True(seats >= 0 && destroyed > seats && town > destroyed,
            $"{client.Label}: {string.Join(",", frames.Select(frame => frame.Type.ToString("X4")))}");
        var body = frames[seats].Body;
        Assert.True(frames[destroyed].Body[..2].SequenceEqual(body[..2]));
        Assert.Equal(seatCount, body[3]);
        for (byte index = 0; index < seatCount; ++index)
            Assert.True(body.Slice(4 + index * 4, 4).SequenceEqual(new byte[] { index, 0xFF, 0xFF, 0xFF }));
    }

    /// <summary>MAKE_PVP_ROOM: a named room (name type 0) on <paramref name="map"/>, with or without a password, not a practice room.</summary>
    private static byte[] MakeRoom(string name, short map, string? password) =>
        [0, .. CountedAscii(name), .. I16(map), .. Password(password), 0];

    private static byte[] EnterRoom(ushort number, string? password) => [.. U16(number), .. Password(password)];

    private static byte[] Password(string? password) => password is null ? [0] : [1, .. CountedAscii(password)];

    private static byte[] CountedAscii(string value) => [.. U32((uint)value.Length), .. Encoding.ASCII.GetBytes(value)];

    private static byte[] I16(short value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(bytes, value);
        return bytes;
    }

    private sealed class PvpChannel(IChannelEntryDomainService actual, byte environment, ChannelEntryKind kind)
        : IChannelEntryDomainService
    {
        public async ValueTask<ChannelEntryIdentity> ResolveAsync(int port, CancellationToken token) =>
            (await actual.ResolveAsync(port, token)) with
            {
                Kind = kind, LoginEnvironment = environment,
            };
        public ValueTask<ChannelAdmissionRefusal?> RefuseAsync(ChannelEntryIdentity channel, AccountId accountId,
            CharacterId characterId, int characterLevel, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ChannelAdmissionRefusal?>(null);
        public ValueTask<ChannelLoginEntry> ResolveLoginAsync(int port, CancellationToken token) => actual.ResolveLoginAsync(port, token);
    }
}
