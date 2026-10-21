using Dfo.Server.Testing;

namespace Dfo.Server.Networking.Tests.Characterization;

/// <summary>RQ-080 (a): one player logs in, creates a character and selects it.</summary>
[Collection(CharacterizationCollection.Name)]
public sealed class SelectCharacterSuccessCharacterizationTests
{
    /// <summary>
    /// SelectCharacterAcceptedTranscriptTests.AcceptedStaticIdentities (62 frames) without its two
    /// conditional rows a new job-0 character does not produce: 0x0245 (job 12 only) and 0x0077
    /// (pet welcome, absent without a pet).
    /// </summary>
    private static readonly (byte Selector, ushort Type)[] ReviewedEntryIdentities =
    [
        (1, 0x0004), (0, 0x0002), (0, 0x0166), (0, 0x0166),
        (0, 0x0166), (0, 0x0166), (0, 0x0166), (0, 0x0167),
        (0, 0x0015), (0, 0x0164), (0, 0x0013),
        (0, 0x0069), (0, 0x0002), (0, 0x0003), (0, 0x00CA),
        (0, 0x0005), (0, 0x000D), (0, 0x000D), (0, 0x000D),
        (0, 0x000D), (0, 0x000D), (0, 0x000D), (0, 0x0465),
        (0, 0x021E), (0, 0x0286), (0, 0x00AD), (0, 0x01C7),
        (0, 0x006C), (0, 0x0187), (0, 0x01B9), (0, 0x015F),
        (0, 0x00AC), (0, 0x00AE), (0, 0x017B), (0, 0x0381),
        (0, 0x0381), (0, 0x021F), (0, 0x03C1), (0, 0x00FB),
        (0, 0x00CD), (0, 0x00B1), (0, 0x036A), (0, 0x03D8),
        (0, 0x025B), (0, 0x0331), (0, 0x01EB), (0, 0x0061),
        (0, 0x0158), (0, 0x02D5), (0, 0x02D5), (0, 0x02D5),
        (0, 0x01A8), (0, 0x0009), (0, 0x0344), (0, 0x007C),
        (0, 0x0002), (1, 0x01BA), (0, 0x0035), (0, 0x0111),
        (0, 0x0016),
    ];

    /// <summary>
    /// Frames the real server adds to that list for the content of this archive and date, and the
    /// codec fixture leaves out (SelectedCharacterStateMessageFactory): the event block that
    /// follows 0x006C when result.Events is present (attendance, scheduled dungeons and quests,
    /// joust initialization: 0x042D, 0x033F, 0x04B8, 0x04BA, 0x04B9) and the collection-box and
    /// capsule state after 0x03D8 (0x03D0, 0x03EB, 0x03DF).
    /// </summary>
    private static readonly ushort[] ContentConditionalTypes = [0x042D, 0x033F, 0x04B8, 0x04BA, 0x04B9, 0x03D0, 0x03EB, 0x03DF];

    [PvfFact]
    public async Task Login_create_and_select_produce_the_recorded_transcript()
    {
        using var timeout = new CancellationTokenSource(CharacterizationCollection.Budget);
        await using var server = await CharacterizationHost.StartAsync(null, timeout.Token);
        using var player = await server.EnterWorldAsync("player", "solo-player", "Probe", timeout.Token);

        var batch = player.Transcript
            .SkipWhile(entry => entry is not TranscriptStep { Name: "select character" })
            .OfType<TranscriptFrame>()
            .Select(entry => entry.Frame)
            .TakeWhile(frame => !frame.Is(0x00, 0x0016))
            .Append(player.Frames.SkipWhile(frame => !frame.Is(0x01, 0x0004)).First(frame => frame.Is(0x00, 0x0016)))
            .Where(frame => !(frame.IsNoti && ContentConditionalTypes.Contains(frame.Type)))
            .Select(frame => (frame.Selector, frame.Type))
            .ToArray();
        Assert.Equal(ReviewedEntryIdentities, batch);
        await CharacterizationTranscript.AssertMatchesAsync("a-select-character", player, timeout.Token);
    }
}
