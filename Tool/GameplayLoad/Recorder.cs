using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace GameplayLoad;

/// <summary>Which population a sample belongs to; only <see cref="Background"/> enters the load percentiles.</summary>
internal static class Groups
{
    public const string Setup = "setup";
    public const string Background = "background";
    public const string Burst = "burst";
    public const string Flood = "flood";
    public const string Idle = "idle";
}

/// <summary>One request as the client saw it.</summary>
internal sealed record Sample(
    string Kind,
    string Group,
    string Connection,
    long Send,
    long? FirstReply,
    long End,
    long Bytes,
    bool TimedOut,
    bool Closed = false,
    long? Scheduled = null)
{
    public bool Replied => FirstReply is not null;

    /// <summary>
    /// When the request should have gone out by the agent's schedule (the coordinated-omission
    /// correction). Only the first request of a paced action has a slot; requests that must wait for
    /// an earlier reply of the same action, and unpaced requests, are intended when they are sent.
    /// </summary>
    public long Intended => Scheduled is { } slot && slot < Send ? slot : Send;

    /// <summary>The request timed out or its connection closed before its first reply: a right-censored sample.</summary>
    public bool Censored => !Replied && (TimedOut || Closed);

    /// <summary>No reply by protocol (the step fence answered first): not a latency sample at all.</summary>
    public bool ProtocolNoReply => !Replied && !TimedOut && !Closed;

    /// <summary>Counts in the judged distributions: replied, or censored at the time it gave up.</summary>
    public bool Counted => Replied || Censored;

    /// <summary>
    /// The judged value: intended send → first reply; for a censored sample intended send → timeout or
    /// close, which is a lower bound. NaN for a protocol no-reply.
    /// </summary>
    public double LatencyMs => FirstReply is { } first ? Stopwatch.GetElapsedTime(Intended, first).TotalMilliseconds
        : Censored ? Stopwatch.GetElapsedTime(Intended, End).TotalMilliseconds : double.NaN;

    /// <summary>A schedule slot that was never sent: it has no actual send, so no reference value.</summary>
    public bool Unsent => Kind == UnsentKind;

    public const string UnsentKind = "（计划但未发出）";

    /// <summary>The reference value: actual send → first reply (censored: actual send → timeout/close).</summary>
    public double ActualLatencyMs => Unsent ? double.NaN
        : FirstReply is { } first ? Stopwatch.GetElapsedTime(Send, first).TotalMilliseconds
        : Censored ? Stopwatch.GetElapsedTime(Send, End).TotalMilliseconds : double.NaN;
}

/// <summary>Bytes a connection received while one request (or one pair step) was open.</summary>
internal sealed record ByteWindow(string Kind, string Role, string Scenario, string Connection, long Send, long Bytes);

internal sealed record ErrorRecord(long Timestamp, string Connection, string Message);

/// <summary>A marked interval, e.g. one burst of concurrent logins or the flood.</summary>
internal sealed record Interval(string Name, long Start, long End);

internal sealed class Recorder
{
    private readonly ConcurrentQueue<Sample> samples = new();
    private readonly ConcurrentQueue<ByteWindow> windows = new();
    private readonly ConcurrentQueue<ErrorRecord> errors = new();
    private readonly ConcurrentQueue<Interval> intervals = new();
    private readonly ConcurrentDictionary<string, long> packets = new();

    public IReadOnlyCollection<Sample> Samples => samples;

    public IReadOnlyCollection<ByteWindow> Windows => windows;

    public IReadOnlyCollection<ErrorRecord> Errors => errors;

    public IReadOnlyCollection<Interval> Intervals => intervals;

    /// <summary>Inbound packets sent, by kind and group, including step fences (kind "STEP_FENCE").</summary>
    public IReadOnlyDictionary<string, long> Packets => packets;

    public void Add(Sample sample) => samples.Enqueue(sample);

    public void Add(ByteWindow window) => windows.Enqueue(window);

    public void Error(string connection, string message)
    {
        errors.Enqueue(new ErrorRecord(Stopwatch.GetTimestamp(), connection, message));
        Console.Error.WriteLine($"[error] {connection}: {message}");
    }

    public void Mark(Interval interval) => intervals.Enqueue(interval);

    public void CountPacket(string kind, string group, int count = 1) =>
        packets.AddOrUpdate(group + "|" + kind, count, (_, value) => value + count);

    public void WriteSamplesCsv(string path, long origin)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine("t_send_ms,t_intended_ms,kind,group,connection,latency_ms,actual_latency_ms,replied,bytes,timed_out,closed");
        foreach (var sample in samples.OrderBy(sample => sample.Send))
        {
            writer.WriteLine(string.Join(',',
                Stopwatch.GetElapsedTime(origin, sample.Send).TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture),
                Stopwatch.GetElapsedTime(origin, sample.Intended).TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture),
                sample.Kind,
                sample.Group,
                sample.Connection,
                sample.Counted ? sample.LatencyMs.ToString("F3", CultureInfo.InvariantCulture) : "",
                sample.Counted ? sample.ActualLatencyMs.ToString("F3", CultureInfo.InvariantCulture) : "",
                sample.Replied ? "1" : "0",
                sample.Bytes.ToString(CultureInfo.InvariantCulture),
                sample.TimedOut ? "1" : "0",
                sample.Closed ? "1" : "0"));
        }
    }
}

internal sealed record Distribution(int Count, double P50, double P95, double P99, double P999, double Max)
{
    public static readonly Distribution Empty = new(0, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN);

    /// <summary>Censored samples in the set; when the largest value is one of them, max is a lower bound.</summary>
    public int Censored { get; init; }

    public bool MaxIsLowerBound { get; init; }

    /// <summary>The judged distribution (intended send → first reply, censored samples included).</summary>
    public static Distribution OfSamples(IEnumerable<Sample> samples) => Build(samples, sample => sample.LatencyMs);

    /// <summary>The reference distribution (actual send → first reply).</summary>
    public static Distribution OfActual(IEnumerable<Sample> samples) => Build(samples, sample => sample.ActualLatencyMs);

    private static Distribution Build(IEnumerable<Sample> samples, Func<Sample, double> value)
    {
        var counted = samples.Where(sample => sample.Counted && !double.IsNaN(value(sample))).ToList();
        var result = Of(counted.Select(value));
        if (result.Count == 0) return result;
        var worst = counted.MaxBy(value)!;
        return result with { Censored = counted.Count(sample => sample.Censored), MaxIsLowerBound = worst.Censored };
    }

    /// <summary>Nearest-rank percentiles: the value at rank ceil(q·n).</summary>
    public static Distribution Of(IEnumerable<double> values)
    {
        var sorted = values.Where(value => !double.IsNaN(value)).Order().ToArray();
        if (sorted.Length == 0) return Empty;
        double Rank(double q) => sorted[Math.Clamp((int)Math.Ceiling(q * sorted.Length) - 1, 0, sorted.Length - 1)];
        return new Distribution(sorted.Length, Rank(0.50), Rank(0.95), Rank(0.99), Rank(0.999), sorted[^1]);
    }

    /// <summary>
    /// Count is starred below 1000 (p99.9 is then the max) and double-starred below 100 (p99 too);
    /// censored samples are shown in the count, and a censored max is prefixed with ≥.
    /// </summary>
    public string Row() => Count == 0
        ? "0 | – | – | – | – | –"
        : string.Join(" | ",
            Count.ToString(CultureInfo.InvariantCulture) + (Count < 100 ? "**" : Count < 1000 ? "*" : "")
                + (Censored > 0 ? $"（截尾 {Censored}）" : ""),
            F(P50), F(P95), F(P99), F(P999), (MaxIsLowerBound ? "≥" : "") + F(Max));

    public const string Footnote =
        "样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。" +
        "「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。";

    public static string F(double value) => double.IsNaN(value) ? "–" : value.ToString(value >= 100 ? "F1" : "F2", CultureInfo.InvariantCulture);
}

internal static class Markdown
{
    public static void Table(StringBuilder text, string[] header, IEnumerable<string[]> rows)
    {
        // A header cell may stand for several columns ("样本 | p50 | ..."), as a Distribution.Row() does.
        header = [.. header.SelectMany(cell => cell.Split(" | "))];
        text.Append("| ").Append(string.Join(" | ", header)).AppendLine(" |");
        text.Append('|').Append(string.Join("|", header.Select((_, index) => index == 0 ? "---" : "---:"))).AppendLine("|");
        foreach (var row in rows) text.Append("| ").Append(string.Join(" | ", row)).AppendLine(" |");
        text.AppendLine();
    }
}
