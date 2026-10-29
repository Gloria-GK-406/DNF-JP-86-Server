using System.Buffers.Binary;
using Dfo.Server.Testing;
using static Dfo.Server.Networking.Tests.Characterization.CharacterizationSteps;

namespace Dfo.Server.Networking.Tests.Characterization;

/// <summary>
/// A party of two through each tower: the projections both towers share (kill rewards, the clear
/// settlement, the party departure and the host hand-over), pinned before they are merged.
/// </summary>
/// <remarks>
/// Rooms, drops and clear rewards are pinned by <see cref="DeterministicTowers"/>. The run is
/// ended by both members dying: a defeat settles the run at once, where a completed climb would
/// need every floor cleared on the wire.
/// </remarks>
[Collection(CharacterizationCollection.Name)]
public sealed class PartyTowerRunCharacterizationTests
{
    public static TheoryData<string> Towers => [nameof(Tower.Boss), nameof(Tower.Death)];

    /// <summary>Enter, load, one kill, both members die, the member leaves the party.</summary>
    [PvfTheory]
    [MemberData(nameof(Towers))]
    public async Task A_defeated_party_run_settles_and_departs_with_the_recorded_transcripts(string name)
    {
        var tower = Tower.Named(name);
        using var timeout = new CancellationTokenSource(CharacterizationCollection.Budget);
        await using var server = await CharacterizationHost.StartAsync(null, timeout.Token);
        var (leader, member) = await EnterTogetherAsync(server, tower, timeout.Token);
        using var _ = leader;
        using var __ = member;
        await LoadBothAsync(leader, member, timeout.Token);

        StepAll("leader kills actor " + FirstActor(leader, tower), leader, member);
        await leader.SendAsync(0x0027, MonsterDeath(FirstActor(leader, tower), leader.WireUserId), timeout.Token);
        await FenceAllAsync(timeout.Token, leader, member);

        foreach (var client in new[] { member, leader })
        {
            StepAll(client.Label + " dies", leader, member);
            await client.SendAsync(0x0028, [0, 0, 0, 0], timeout.Token);
            await FenceAllAsync(timeout.Token, client, client == leader ? member : leader);
        }

        StepAll("member leaves the party", leader, member);
        await member.SendAsync(0x000D, [], timeout.Token);
        await FenceAllAsync(timeout.Token, member, leader);

        await CharacterizationTranscript.AssertMatchesAsync(tower.Scenario + "-defeat", leader, timeout.Token);
        await CharacterizationTranscript.AssertMatchesAsync(tower.Scenario + "-defeat", member, timeout.Token);
    }

    /// <summary>Enter, load, the leader leaves the party mid-fight: the member is the new host.</summary>
    [PvfTheory]
    [MemberData(nameof(Towers))]
    public async Task A_leader_leaving_mid_run_hands_the_host_over_with_the_recorded_transcripts(string name)
    {
        var tower = Tower.Named(name);
        using var timeout = new CancellationTokenSource(CharacterizationCollection.Budget);
        await using var server = await CharacterizationHost.StartAsync(null, timeout.Token);
        var (leader, member) = await EnterTogetherAsync(server, tower, timeout.Token);
        using var _ = leader;
        using var __ = member;
        await LoadBothAsync(leader, member, timeout.Token);

        StepAll("leader leaves the party", leader, member);
        await leader.SendAsync(0x000D, [], timeout.Token);
        await FenceAllAsync(timeout.Token, leader, member);

        await CharacterizationTranscript.AssertMatchesAsync(tower.Scenario + "-host", leader, timeout.Token);
        await CharacterizationTranscript.AssertMatchesAsync(tower.Scenario + "-host", member, timeout.Token);
    }

    private static async Task<(WireClient Leader, WireClient Member)> EnterTogetherAsync(
        CharacterizationHost server, Tower tower, CancellationToken cancellationToken)
    {
        var (leader, member) = await TwoPlayersInTownAsync(server, cancellationToken,
            () => PartyTowerCharacterizationTests.PrepareEveryCharacterAsync(server, tower.Level, tower.Ticket, cancellationToken));
        await FormPartyAsync(leader, member, cancellationToken);
        await OpenDungeonListAsync(leader, member, cancellationToken);
        await EnterDungeonAsync(leader, member, tower.DungeonId, frame => frame.Is(0x00, tower.MapType), cancellationToken);
        return (leader, member);
    }

    private static async Task LoadBothAsync(WireClient leader, WireClient member, CancellationToken cancellationToken)
    {
        foreach (var client in new[] { leader, member })
        {
            StepAll(client.Label + " finishes loading", leader, member);
            await client.SendAsync(0x0025, new byte[8], cancellationToken);
            await FenceAllAsync(cancellationToken, client, client == leader ? member : leader);
        }
    }

    /// <summary>The uid of the first actor the tower map announced (both maps share one layout).</summary>
    private static ushort FirstActor(WireClient client, Tower tower)
    {
        var map = client.Frames.Last(frame => frame.Is(0x00, tower.MapType));
        // u16 level, u32 seed, u32 map, u8 count, then per actor u32 list index and the u16 uid.
        Assert.True(map.Body[10] > 0, "The tower map announced no actor.");
        return BinaryPrimitives.ReadUInt16LittleEndian(map.Body[15..]);
    }

    /// <summary>DIE_MONSTER: u32 actor, u16 killer, four u32, no attack records, then the 39-byte suffix.</summary>
    private static byte[] MonsterDeath(ushort actor, ushort killer)
    {
        var body = new byte[62];
        BinaryPrimitives.WriteUInt16LittleEndian(body, actor);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(4), killer);
        return body;
    }

    private sealed record Tower(string Scenario, int DungeonId, byte Level, int Ticket, ushort MapType)
    {
        /// <summary>The Boss tower: level 60, one 领主的邀请函 (2749110, a quest-space pile like 4183).</summary>
        public static readonly Tower Boss = new("h-party-boss-tower", 3505, 60, 2749110, 0x0334);

        /// <summary>The Tower of Death, the illusion tower runtime's tower with its own inventory.</summary>
        public static readonly Tower Death = new("i-party-death-tower", 11000, 50, 4183, 0x008F);

        public static Tower Named(string name) => name == nameof(Boss) ? Boss : Death;
    }
}
