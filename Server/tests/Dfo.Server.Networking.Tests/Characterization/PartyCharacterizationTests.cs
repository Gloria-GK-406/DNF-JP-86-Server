using Dfo.Server.Testing;
using static Dfo.Server.Networking.Tests.Characterization.CharacterizationSteps;

namespace Dfo.Server.Networking.Tests.Characterization;

/// <summary>RQ-080 (c) to (g): two players in one channel and town.</summary>
[Collection(CharacterizationCollection.Name)]
public sealed class PartyCharacterizationTests
{
    /// <summary>(g) Two players see each other in 1:1; one closes its socket.</summary>
    [PvfFact]
    public async Task A_disconnect_in_town_announces_the_departure_to_the_other_player()
    {
        using var timeout = new CancellationTokenSource(CharacterizationCollection.Budget);
        await using var server = await CharacterizationHost.StartAsync(null, timeout.Token);
        var (leader, member) = await TwoPlayersInTownAsync(server, timeout.Token);
        using (member)
        {
            using (leader)
            {
                leader.Step("leader closes its socket");
                member.Step("leader closes its socket");
            }

            await member.ReadUntilAsync(frame => frame.Is(0x00, 0x0006), timeout.Token);
            await member.FenceAsync(timeout.Token);

            await CharacterizationTranscript.AssertMatchesAsync("g-disconnect-cleanup", member, timeout.Token);
            await CharacterizationTranscript.AssertMatchesAsync("g-disconnect-cleanup", leader, timeout.Token);
        }
    }

    /// <summary>(f) A party of two; the member leaves it.</summary>
    [PvfFact]
    public async Task Leaving_a_party_projects_to_both_players()
    {
        using var timeout = new CancellationTokenSource(CharacterizationCollection.Budget);
        await using var server = await CharacterizationHost.StartAsync(null, timeout.Token);
        var (leader, member) = await TwoPlayersInTownAsync(server, timeout.Token);
        using var _ = leader;
        using var __ = member;
        await FormPartyAsync(leader, member, timeout.Token);

        StepAll("member leaves the party", leader, member);
        await member.SendAsync(0x000D, [], timeout.Token);
        await FenceAllAsync(timeout.Token, member, leader);

        await CharacterizationTranscript.AssertMatchesAsync("f-leave-party", leader, timeout.Token);
        await CharacterizationTranscript.AssertMatchesAsync("f-leave-party", member, timeout.Token);
    }

    /// <summary>(c) The leader of a party of two enters dungeon 144.</summary>
    /// <remarks>
    /// XO-1 (the member's entry before the leader's) is not asserted here. At baseline each
    /// connection's TouchSocket send loop writes independently, so the two arrivals are microseconds
    /// apart in either order; an arrival-stamp comparison went the wrong way or tied in 6 of 20 loaded runs.
    /// AC-012 tests in T-003/T-006 carry the requirement (TASK-01 Result).
    /// </remarks>
    [PvfFact]
    public async Task A_party_entering_a_dungeon_produces_the_recorded_transcripts()
    {
        using var timeout = new CancellationTokenSource(CharacterizationCollection.Budget);
        await using var server = await CharacterizationHost.StartAsync(null, timeout.Token);
        var (leader, member) = await TwoPlayersInTownAsync(server, timeout.Token);
        using var _ = leader;
        using var __ = member;
        await FormPartyAsync(leader, member, timeout.Token);
        await OpenDungeonListAsync(leader, member, timeout.Token);
        await EnterDungeonAsync(leader, member, PartyDungeonId, StartMap, timeout.Token);

        await CharacterizationTranscript.AssertMatchesAsync("c-party-dungeon-entry", leader, timeout.Token, DungeonMasks);
        await CharacterizationTranscript.AssertMatchesAsync("c-party-dungeon-entry", member, timeout.Token, DungeonMasks);
    }

    /// <summary>(d) The same party, both loaded, leaves the dungeon on the leader's request.</summary>
    /// <remarks>
    /// XO-2 (the member's decision before the leader's ACK) is not asserted, for the same reason as
    /// XO-1: the baseline arrival order is not deterministic (wrong way or tied in 4 of 10 idle runs).
    /// </remarks>
    [PvfFact]
    public async Task Leaving_a_dungeon_produces_the_recorded_transcripts()
    {
        using var timeout = new CancellationTokenSource(CharacterizationCollection.Budget);
        await using var server = await CharacterizationHost.StartAsync(null, timeout.Token);
        var (leader, member) = await TwoPlayersInTownAsync(server, timeout.Token);
        using var _ = leader;
        using var __ = member;
        await FormPartyAsync(leader, member, timeout.Token);
        await OpenDungeonListAsync(leader, member, timeout.Token);
        await EnterDungeonAsync(leader, member, PartyDungeonId, StartMap, timeout.Token);

        foreach (var client in new[] { leader, member })
        {
            StepAll(client.Label + " finishes loading", leader, member);
            await client.SendAsync(0x0025, new byte[8], timeout.Token);
            await FenceAllAsync(timeout.Token, client, client == leader ? member : leader);
        }

        StepAll("leader leaves the dungeon", leader, member);
        await leader.SendAsync(0x0048, [0x01, 0x01], timeout.Token);
        await FenceAllAsync(timeout.Token, leader, member);

        await CharacterizationTranscript.AssertMatchesAsync("d-leave-dungeon", leader, timeout.Token, DungeonMasks);
        await CharacterizationTranscript.AssertMatchesAsync("d-leave-dungeon", member, timeout.Token, DungeonMasks);
    }

    /// <summary>
    /// 144, a story-mode dungeon open from level 1 with one ordinary maze; the A21 client has
    /// entered it on the wire (protocol/game/client-to-server/0x0010.md).
    /// </summary>
    private const int PartyDungeonId = 144;

    private static bool StartMap(WireFrame frame) => frame.Is(0x00, 0x001D);

    /// <summary>
    /// None: the layout seed is pinned by <see cref="DeterministicLayouts"/>, and with it the start
    /// cell, room map, actors and room seed START_MAP carries.
    /// </summary>
    internal static readonly IReadOnlyList<FrameMask> DungeonMasks = [];
}
