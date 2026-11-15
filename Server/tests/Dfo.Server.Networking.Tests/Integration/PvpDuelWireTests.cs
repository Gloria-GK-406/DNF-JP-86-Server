using System.Buffers.Binary;
using System.Text;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
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

        var waiting = Mark(a, b, c);
        StepAll("both close the result screen", a, b, c);
        await a.SendAsync(0x0039, [], ct);
        await b.SendAsync(0x0039, [], ct);
        await FenceAllAsync(ct, a, b, c);
        foreach (var client in new[] { a, b, c })
        {
            var state = After(client, waiting).Last(frame => frame.Is(0, 0x002A)).Body;
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

    private static Task<CharacterizationHost> StartAsync(bool ranked, CancellationToken ct) =>
        CharacterizationHost.StartAsync(new CharacterizationOptions
        {
            Decorate = services => services.Decorate<IChannelEntryDomainService>((_, inner) => new PvpChannel(inner, ranked ? (byte)24 : (byte)8)),
        }, ct);

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

    private sealed class PvpChannel(IChannelEntryDomainService actual, byte environment) : IChannelEntryDomainService
    {
        public async ValueTask<ChannelEntryIdentity> ResolveAsync(int port, CancellationToken token) =>
            (await actual.ResolveAsync(port, token)) with { Kind = ChannelEntryKind.Pvp, LoginEnvironment = environment };
        public ChannelAdmissionRefusal? Refuse(ChannelEntryIdentity channel, int level) => null;
        public ValueTask<ChannelLoginEntry> ResolveLoginAsync(int port, CancellationToken token) => actual.ResolveLoginAsync(port, token);
    }
}
