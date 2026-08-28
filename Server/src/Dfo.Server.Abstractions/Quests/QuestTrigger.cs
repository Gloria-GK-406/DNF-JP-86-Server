namespace Dfo.Server.Abstractions.Quests;

/// <summary>
/// How far along a quest is: three counters packed into one word.
/// </summary>
/// <remarks>
/// Nine bits each, low channel first, and the word is what both this server and the client call
/// the quest's trigger. Zero is not "no progress" -- it is the objective met, which is why
/// <see cref="IsComplete"/> reads the whole word rather than any one channel. A quest that asks
/// for one thing opens at 1 and finishes at 0; one that asks for three of something opens at 3.
///
/// Three channels because that is how many the reference server's packing gives, not because any
/// observed quest uses more than one. The tutorial's two both use the low channel alone.
/// </remarks>
public readonly record struct QuestTrigger(uint PackedValue)
{
    /// <summary>How many counters the word holds.</summary>
    public const int ChannelCount = 3;

    private const int ChannelWidth = 9;
    private const uint ChannelMask = 0x1FFu;

    /// <summary>The objective is met.</summary>
    public bool IsComplete => PackedValue == 0;

    /// <summary>
    /// Whether a report carrying this selector is one a quest can be advanced by at all.
    /// </summary>
    /// <remarks>
    /// The reference server's rule, and it is a rule about the selector alone: 0 and 1 move the
    /// whole word, and anything else has to be some non-empty combination of the three channel
    /// bits and nothing besides. A selector outside that set is a report this server cannot read,
    /// which is a refusal rather than a no-op -- reading it as zero would advance the wrong
    /// counter.
    /// </remarks>
    public static bool IsReportable(byte selector) =>
        selector is 0 or 1 || ((selector & ~0x70) == 0 && (selector & 0x70) != 0);

    /// <summary>The counter at this index, or zero when the index is not one of the three.</summary>
    public int GetChannel(int index)
    {
        var shift = index * ChannelWidth;
        return shift is < 0 or > 18 ? 0 : (int)((PackedValue >> shift) & ChannelMask);
    }

    /// <summary>This word with one counter set, saturating at the channel's width.</summary>
    public QuestTrigger ReplaceChannel(int index, long value)
    {
        var shift = index * ChannelWidth;
        if (shift is < 0 or > 18)
            return this;

        var channel = value <= 0 ? 0u : value >= ChannelMask ? ChannelMask : (uint)value;
        return new QuestTrigger((PackedValue & ~(ChannelMask << shift)) | (channel << shift));
    }

    /// <summary>
    /// This word after one report from the client, as the reference server applies it.
    /// </summary>
    /// <param name="selector">
    /// Which counters the report is about. <c>1</c> and <c>0</c> both address the word as a whole
    /// -- 1 only ever counts up, 0 goes whichever way <paramref name="increment"/> says. Any other
    /// value names channels by bit: 0x10 the low one, 0x20 the middle, 0x40 the high, and a
    /// selector may name several at once, which moves each of them.
    /// </param>
    /// <param name="increment">Whether the report is one more or one fewer.</param>
    /// <remarks>
    /// Saturating in both directions and at both scales: the word never wraps past its maximum and
    /// never falls below zero, and neither does any single channel. A quest at 0 that reports one
    /// fewer stays at 0, which is what makes a repeated report harmless -- and the client does
    /// repeat, every time it is not answered.
    /// </remarks>
    public QuestTrigger Advance(byte selector, bool increment)
    {
        if (selector == 1)
            return new QuestTrigger(AddSaturating(PackedValue, 1));
        if (selector == 0)
        {
            return new QuestTrigger(
                increment ? AddSaturating(PackedValue, 1) : PackedValue > 0 ? PackedValue - 1 : 0);
        }

        var result = this;
        var delta = increment ? 1 : -1;
        if ((selector & 0x10) != 0)
            result = result.AdjustChannel(0, delta);
        if ((selector & 0x20) != 0)
            result = result.AdjustChannel(1, delta);
        if ((selector & 0x40) != 0)
            result = result.AdjustChannel(2, delta);
        return result;
    }

    public override string ToString() => PackedValue.ToString();

    private QuestTrigger AdjustChannel(int index, int delta) =>
        this.ReplaceChannel(index, Math.Clamp(this.GetChannel(index) + delta, 0, (int)ChannelMask));

    private static uint AddSaturating(uint value, uint delta) =>
        uint.MaxValue - value < delta ? uint.MaxValue : value + delta;
}
