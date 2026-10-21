using System.Runtime.CompilerServices;
using System.Text;

namespace Dfo.Server.Networking.Tests.Characterization;

/// <summary>
/// One byte range of one frame type that changes from run to run and cannot be pinned by the
/// fixture. Offsets count from the first byte of the 15-byte header.
/// </summary>
internal sealed record FrameMask(
    byte Selector,
    ushort Type,
    int Offset,
    int Length,
    string Reason,
    Func<WireFrame, bool>? When = null)
{
    public bool AppliesTo(WireFrame frame) =>
        frame.Selector == Selector
        && frame.Type == Type
        && frame.Bytes.Length >= Offset + Length
        && (When is null || When(frame));
}

/// <summary>
/// A frame that the baseline sends from a background flow, so its position among the other frames
/// of its connection is not defined. Its bytes and its count are still compared; only its place
/// in the sequence is not. It must arrive after <see cref="AnchorType"/>.
/// </summary>
internal sealed record FloatingFrame(
    byte Selector,
    ushort Type,
    byte AnchorSelector,
    ushort AnchorType,
    string Reason);

/// <summary>
/// Golden transcripts: one hexadecimal line per frame, in arrival order, step markers and a
/// comment naming each frame. A masked byte is written as <c>..</c> and compared as such.
/// </summary>
internal static class CharacterizationTranscript
{
    /// <summary>Set to 1 to (re)write the golden files from this run instead of comparing.</summary>
    public const string RecordVariable = "DFO_CHARACTERIZATION_RECORD";

    private const int Noti2Subtype1BodyLength = 519;

    /// <summary>
    /// Everything else is pinned: the clock by <see cref="CharacterizationHost.Now"/>, character
    /// ids and item uids by a fresh database and a fixed creation order, names and jobs by the test,
    /// dungeon layouts by <see cref="FixedLayoutSeed"/>.
    /// </summary>
    public static readonly IReadOnlyList<FrameMask> Masks =
    [
        new(0x01, 0x0004, WireFrame.HeaderLength + 5, 4,
            "SELECT_CHARACTER accept, body 5..8: character created_at in unix seconds. SQLite writes it " +
            "with unixepoch() at CREATE_CHARACTER (CharacterEntryTables.cs), the wall clock, not TimeProvider.",
            frame => frame.Body.Length > 2),
        .. new[] { 123, 183, 243 }.Select(body => new FrameMask(0x00, 0x0002, WireFrame.HeaderLength + body, 4,
            $"USERINFO subtype 1, body {body}..{body + 3}: core.Value (InstanceValue) of starting equipment piece " +
            $"{(body - 123) / 60 + 1} of 3, sixty bytes apart (WriteNoti2EquippedItem). CREATE_CHARACTER draws it " +
            "from EquipmentQualitySeeds.NewRandom(), i.e. Random.Shared, which nothing can inject.",
            frame => frame.Body.Length == Noti2Subtype1BodyLength && frame.Body[0] == 1)),
    ];

    /// <summary>
    /// The second AVAILABLE_QUEST_LIST of every successful selection. SelectCharacterHandler's
    /// Southern Dale ticket refill enqueues SouthernDaleDailyRefillCompleted after publication
    /// (CMD 0x0239 precedes publication); its LiveOnly subscriber projects the quest offers from
    /// the event loop, concurrently with the rest of the selection tail and later steps.
    /// </summary>
    public static readonly FloatingFrame SouthernDaleQuestOffers = new(0x00, 0x0015, 0x01, 0x0239,
        "SouthernDaleDailyRefillCompleted -> SouthernInventoryRefresh(RefreshQuestOffers) from the event loop");

    public static readonly IReadOnlyList<FloatingFrame> Floating = [SouthernDaleQuestOffers];

    /// <summary>
    /// The layout seed every dungeon entry is given in place of Random.Shared.Next(); see
    /// <see cref="DeterministicLayouts"/>.
    /// </summary>
    public const int FixedLayoutSeed = 0x2468ACE;

    /// <param name="optionalFloating">
    /// Floating frames that may also not arrive at all, because the connection is closed while
    /// the background flow is still running. Their bytes are listed as comments only.
    /// </param>
    /// <param name="lastStepMayEndEarly">
    /// The observed transcript may stop part-way through the golden's last step -- every step marker
    /// present, every frame read identical to the golden's, only trailing frames of the last step
    /// missing. For a connection the server closes at baseline before its last frames are
    /// guaranteed to leave the process. Record such a golden on an idle machine.
    /// </param>
    public static async Task AssertMatchesAsync(
        string scenario,
        WireClient client,
        CancellationToken cancellationToken,
        IReadOnlyList<FrameMask>? scenarioMasks = null,
        IReadOnlyList<FloatingFrame>? optionalFloating = null,
        bool lastStepMayEndEarly = false,
        [CallerFilePath] string callerFile = "")
    {
        var masks = scenarioMasks is null ? Masks : [.. Masks, .. scenarioMasks];
        optionalFloating ??= [];
        var required = Floating.Except(optionalFloating).ToArray();
        // A floating frame may still be on its way when the scenario's last fence returns.
        while (!client.IsClosed && required.Any(rule => Split(client.Transcript, rule).Missing))
            await client.ReadAsync(cancellationToken);

        var name = $"{scenario}.{client.Label}";
        var actual = Render(name, client.Transcript, masks, required, optionalFloating);
        if (Environment.GetEnvironmentVariable(RecordVariable) == "1")
        {
            // DFO_CHARACTERIZATION_RECORD_DIR records elsewhere, to compare runs before masking.
            var directory = Environment.GetEnvironmentVariable(RecordVariable + "_DIR")
                ?? Path.Combine(Path.GetDirectoryName(callerFile)!, "Golden");
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, name + ".hex"), actual, cancellationToken);
            return;
        }

        var goldenPath = Path.Combine(AppContext.BaseDirectory, "Characterization", "Golden", name + ".hex");
        Assert.True(File.Exists(goldenPath), $"Golden transcript {goldenPath} is missing.");
        var expected = Significant(await File.ReadAllTextAsync(goldenPath, cancellationToken));
        var observed = Significant(actual);
        if (expected.SequenceEqual(observed)) return;
        if (lastStepMayEndEarly
            && observed.Count < expected.Count
            && observed.Count > expected.FindLastIndex(line => line.StartsWith("## ", StringComparison.Ordinal))
            && expected.Take(observed.Count).SequenceEqual(observed))
            return;

        var actualDirectory = Path.Combine(Path.GetTempPath(), "dfo-characterization-actual");
        Directory.CreateDirectory(actualDirectory);
        var actualPath = Path.Combine(actualDirectory, name + ".hex");
        await File.WriteAllTextAsync(actualPath, actual, cancellationToken);
        var index = 0;
        while (index < expected.Count && index < observed.Count && expected[index] == observed[index]) index++;
        Assert.Fail(
            $"{name}: transcript differs at significant line {index} " +
            $"(expected {expected.Count} lines, observed {observed.Count}).{Environment.NewLine}" +
            $"expected: {(index < expected.Count ? expected[index] : "<end>")}{Environment.NewLine}" +
            $"observed: {(index < observed.Count ? observed[index] : "<end>")}{Environment.NewLine}" +
            $"Whole observed transcript: {actualPath}");
    }

    /// <summary>The first frame matching the rule after the rule's anchor, if the anchor has been read.</summary>
    private static (int Index, bool Missing) Split(IReadOnlyList<TranscriptEntry> transcript, FloatingFrame rule)
    {
        var anchored = false;
        for (var index = 0; index < transcript.Count; index++)
        {
            if (transcript[index] is not TranscriptFrame { Frame: var frame }) continue;
            if (!anchored)
            {
                anchored = frame.Is(rule.AnchorSelector, rule.AnchorType);
                continue;
            }

            if (frame.Is(rule.Selector, rule.Type)) return (index, false);
        }

        return (-1, anchored);
    }

    /// <summary>Step markers and frame lines; comments are documentation and are not compared.</summary>
    private static List<string> Significant(string text) =>
        text.Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.Length > 0 && (!line.StartsWith('#') || line.StartsWith("## ", StringComparison.Ordinal)))
            .ToList();

    private static string Render(
        string name,
        IReadOnlyList<TranscriptEntry> transcript,
        IReadOnlyList<FrameMask> masks,
        IReadOnlyList<FloatingFrame> required,
        IReadOnlyList<FloatingFrame> optional)
    {
        var floating = new Dictionary<int, (FloatingFrame Rule, bool Optional)>();
        foreach (var rule in required)
            if (Split(transcript, rule) is { Index: >= 0 } found) floating[found.Index] = (rule, false);
        foreach (var rule in optional)
            if (Split(transcript, rule) is { Index: >= 0 } found) floating[found.Index] = (rule, true);

        var text = new StringBuilder();
        text.Append("# ").Append(name).Append('\n');
        text.Append("# One server frame per line (15-byte header + payload), in arrival order. '..' is a masked byte;\n");
        text.Append("# masks and floating frames are defined, with reasons, in CharacterizationTranscript.\n");
        var index = 0;
        for (var position = 0; position < transcript.Count; position++)
        {
            switch (transcript[position])
            {
                case TranscriptStep step:
                    text.Append("## ").Append(step.Name).Append('\n');
                    break;
                case TranscriptFrame { Frame: var frame } when floating.ContainsKey(position):
                    text.Append($"# (floating {Describe(frame)}, listed at the end)\n");
                    break;
                case TranscriptFrame { Frame: var frame }:
                    text.Append($"# {index++:D3} {Describe(frame)}\n");
                    text.Append(Hex(frame, masks)).Append('\n');
                    break;
            }
        }

        foreach (var (position, (rule, isOptional)) in floating.OrderBy(entry => entry.Key))
        {
            var frame = ((TranscriptFrame)transcript[position]).Frame;
            text.Append($"# floating: {rule.Reason}; after {Describe(rule.AnchorSelector, rule.AnchorType)}\n");
            if (isOptional)
            {
                text.Append("# optional at baseline (the connection may close first), so not compared:\n");
                text.Append("# ").Append(Hex(frame, masks)).Append('\n');
            }
            else
            {
                text.Append($"## floating {Describe(frame.Selector, frame.Type)}\n");
                text.Append(Hex(frame, masks)).Append('\n');
            }
        }

        return text.ToString();
    }

    private static string Describe(WireFrame frame) =>
        $"{Describe(frame.Selector, frame.Type)} {FrameNames.Describe(frame)} ({frame.Body.Length}B)";

    private static string Describe(byte selector, ushort type) =>
        $"{(selector == 0x01 ? "CMD " : selector == 0x00 ? "NOTI" : $"S{selector:X2}")} 0x{type:X4}";

    private static string Hex(WireFrame frame, IReadOnlyList<FrameMask> masks)
    {
        var masked = new bool[frame.Bytes.Length];
        foreach (var mask in masks.Where(mask => mask.AppliesTo(frame)))
        {
            if (mask.Offset < WireFrame.HeaderLength || mask.Length >= frame.Body.Length)
                throw new InvalidOperationException($"Mask for 0x{mask.Type:X4} would cover a header or a whole payload.");
            Array.Fill(masked, true, mask.Offset, mask.Length);
        }

        var text = new StringBuilder(frame.Bytes.Length * 2);
        for (var i = 0; i < frame.Bytes.Length; i++)
            text.Append(masked[i] ? ".." : frame.Bytes[i].ToString("X2"));
        return text.ToString();
    }
}
