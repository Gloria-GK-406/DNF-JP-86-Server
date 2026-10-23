using Dfo.Server.Networking.Tests.Characterization;
using Dfo.Server.Testing;
using Xunit.Abstractions;
using static Dfo.Server.Networking.Tests.Characterization.CharacterizationSteps;

namespace Dfo.Server.Networking.Tests.Integration;

/// <summary>
/// Spec appendix G VC-9 from the receiving side: a party title of exactly the limit still reaches a
/// player who walks into the area, inside the per-person walk-in bound the T-006 closure derives its
/// head counts from.
/// </summary>
[Collection(CharacterizationCollection.Name)]
public sealed class PartyTitleFanOutWireTests(ITestOutputHelper output)
{
    /// <summary>VC-9's limit, written as a number so these cases do not follow the production constant.</summary>
    private const int TitleLimit = 256;

    /// <summary>
    /// The walk-in CMD with no one in the area registers 52 B; each person already there adds at most
    /// 670 B plus their party's title (TASK-26 crowd measurement, section C).
    /// </summary>
    private const int WalkInFixedBytes = 52;
    private const int PerPersonBytes = 670;

    [PvfFact]
    public async Task A_maximum_length_party_title_keeps_a_walk_in_within_the_per_person_bound()
    {
        using var timeout = new CancellationTokenSource(CharacterizationCollection.Budget);
        await using var server = await CharacterizationHost.StartAsync(null, timeout.Token);
        var (leader, newcomer, walkIn) = await WalkInPastATitledPartyAsync(server, timeout.Token);
        using var _ = leader;
        using var __ = newcomer;

        var roster = Assert.Single(walkIn, frame => frame.Is(0x00, 0x0009));
        Assert.Equal(80 + TitleLimit, roster.Bytes.Length);
        var registered = walkIn.Sum(frame => frame.Bytes.Length);
        output.WriteLine($"walk-in CMD: {walkIn.Length} frames, {registered} B; roster frame {roster.Bytes.Length} B");
        Assert.InRange(registered, 0, WalkInFixedBytes + PerPersonBytes + TitleLimit);
    }

    [PvfFact]
    public async Task A_party_title_at_the_limit_reaches_a_player_walking_in()
    {
        using var timeout = new CancellationTokenSource(CharacterizationCollection.Budget);
        await using var server = await CharacterizationHost.StartAsync(null, timeout.Token);
        var (leader, newcomer, walkIn) = await WalkInPastATitledPartyAsync(server, timeout.Token);
        using var _ = leader;
        using var __ = newcomer;

        var roster = Assert.Single(walkIn, frame => frame.Is(0x00, 0x0009));
        Assert.Equal((byte)0, roster.Body[4]);
        Assert.Equal(TitleLimit, BitConverter.ToInt32(roster.Body[6..10]));
        Assert.Equal(Title(), roster.Body.Slice(10, TitleLimit).ToArray());

        await newcomer.FenceAsync(timeout.Token);
        await leader.FenceAsync(timeout.Token);
        Assert.False(newcomer.IsClosed);
        Assert.False(leader.IsClosed);
    }

    /// <summary>
    /// The leader stands in 1:1 and creates a party with a title of the limit; the newcomer then walks in
    /// from the town gate. Returns the frames of the newcomer's walk-in CMD, without the fence answer.
    /// </summary>
    private static async Task<(WireClient Leader, WireClient Newcomer, WireFrame[] WalkIn)> WalkInPastATitledPartyAsync(
        CharacterizationHost server,
        CancellationToken cancellationToken)
    {
        var leader = await server.EnterWorldAsync("leader", "title-leader", "TitleLeader", cancellationToken);
        var newcomer = await server.EnterWorldAsync("newcomer", "title-newcomer", "TitleNewcomer", cancellationToken);
        await leader.MoveToSharedAreaAsync(cancellationToken);
        await FenceAllAsync(cancellationToken, leader, newcomer);

        StepAll("leader creates a titled party", leader, newcomer);
        var title = Title();
        await leader.SendAsync(0x000C,
            [0x01, 0x00, .. BitConverter.GetBytes(title.Length), .. title, .. Convert.FromHexString("04000000000500020100")],
            cancellationToken);
        await FenceAllAsync(cancellationToken, leader, newcomer);
        Assert.Contains(leader.Frames, frame => frame.Is(0x00, 0x0009) && frame.Bytes.Length == 80 + TitleLimit);

        await newcomer.MoveToSharedAreaAsync(cancellationToken);
        var walkIn = newcomer.Transcript
            .Skip(LastStep(newcomer, "set user area 1:1") + 1)
            .OfType<TranscriptFrame>()
            .Select(entry => entry.Frame)
            .Where(frame => !frame.Is(0x01, WireClient.FenceType))
            .ToArray();
        return (leader, newcomer, walkIn);
    }

    private static int LastStep(WireClient client, string name) =>
        client.Transcript.Select((entry, index) => (entry, index))
            .Last(pair => pair.entry is TranscriptStep step && step.Name == name).index;

    private static byte[] Title() => [.. Enumerable.Range(0, TitleLimit).Select(index => (byte)('A' + (index % 26)))];
}
