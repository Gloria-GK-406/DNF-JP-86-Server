using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace GameplayLoad;

internal sealed record RunOptions(
    string HostDll,
    string Pvf,
    string Output,
    string Tag,
    int Solos,
    int Pairs,
    double Rate,
    int Seed,
    TimeSpan Cold,
    TimeSpan Steady,
    int Bursts,
    int BurstSize,
    TimeSpan BurstEvery,
    TimeSpan Flood,
    int FloodWindow,
    TimeSpan Dwell,
    SoloWeights Weights,
    double[] RampRates,
    TimeSpan RampStep,
    TimeSpan RampWarm,
    int EntryConcurrency);

/// <summary>The three entry points: full baseline run, throughput ramp, exact outbound bytes.</summary>
internal static class Scenarios
{
    // ------------------------------------------------------------------ run

    public static async Task<int> RunAsync(RunOptions options)
    {
        Directory.CreateDirectory(options.Output);
        var recorder = new Recorder();
        await using var host = await HostProcess.StartAsync(options.HostDll, options.Pvf, Path.Combine(options.Output, "host.log"), CancellationToken.None);
        var origin = host.ReadyTimestamp;
        var startup = Stopwatch.GetElapsedTime(host.StartedTimestamp, host.ReadyTimestamp);
        Console.WriteLine($"host ready after {startup.TotalMilliseconds:F0} ms (pid {host.ProcessId})");
        var context = new LoadContext(host, recorder, options.Tag)
        {
            RatePerConnection = options.Rate,
            EntryGate = options.EntryConcurrency > 0 ? new SemaphoreSlim(options.EntryConcurrency) : null,
        };
        using var stop = new CancellationTokenSource();

        // (f): the load starts the moment the Host is ready; nothing is warmed up.
        var agents = new List<Agent>();
        for (var index = 0; index < options.Solos; index++) agents.Add(new SoloAgent(context, index, options.Seed * 1000 + index, options.Weights));
        for (var index = 0; index < options.Pairs; index++) agents.Add(new PairAgent(context, index, options.Seed * 1000 + 100 + index, options.Dwell));
        var flood = new FloodAgent(context, options.Seed * 1000 + 999);
        var running = agents.Select(agent => Task.Run(() => agent.RunAsync(stop.Token))).ToList();
        running.Add(Task.Run(() => flood.RunAsync(stop.Token)));

        var coldEnd = options.Cold;
        var steadyEnd = coldEnd + options.Steady;
        await DelayUntil(origin, coldEnd);
        Console.WriteLine("cold window done");
        string environment;
        try
        {
            environment = EnvironmentProbe.Describe(options.HostDll, host);
        }
        catch (Exception error)
        {
            environment = "- 环境探测失败：" + error.Message;
        }

        await DelayUntil(origin, steadyEnd);
        Console.WriteLine("steady window done");

        // (d): bursts of concurrent logins over the background load.
        var burstLogins = new List<(double RegisterMs, double LauncherLoginMs, double GameLoginMs)>();
        var loginsStart = Stopwatch.GetElapsedTime(origin);
        for (var burst = 0; burst < options.Bursts; burst++)
        {
            await DelayUntil(origin, steadyEnd + options.BurstEvery * burst + TimeSpan.FromSeconds(2));
            var start = Stopwatch.GetTimestamp();
            var results = await Task.WhenAll(Enumerable.Range(0, options.BurstSize).Select(index =>
                BurstLoginAsync(context, $"{options.Tag}-burst{burst}-{index}", stop.Token)));
            recorder.Mark(new Interval($"burst{burst}", start, Stopwatch.GetTimestamp()));
            burstLogins.AddRange(results.Where(result => result is not null).Select(result => result!.Value));
        }

        var floodStart = steadyEnd + options.BurstEvery * options.Bursts + TimeSpan.FromSeconds(2);
        await DelayUntil(origin, floodStart);
        Console.WriteLine("logins done, flooding");

        // (e): one connection keeps a window of fences in flight.
        var floodBegin = Stopwatch.GetTimestamp();
        try
        {
            await flood.FloodAsync(options.Flood, options.FloodWindow, stop.Token);
        }
        catch (Exception error)
        {
            recorder.Error("flood", error.Message);
        }

        recorder.Mark(new Interval("flood", floodBegin, Stopwatch.GetTimestamp()));
        await Task.Delay(TimeSpan.FromSeconds(2));
        var stopAt = Stopwatch.GetTimestamp();
        stop.Cancel();
        await Task.WhenAll(running);
        var end = Stopwatch.GetTimestamp();
        // Slots the agents never got to send before the stop are censored samples (coordinated omission).
        foreach (var agent in agents)
            foreach (var slot in agent.UnsentSlots(stopAt))
                recorder.Add(new Sample(UnsentKind, Groups.Background, agent.Name, stopAt, null, stopAt, 0, TimedOut: true, Scheduled: slot));
        await Task.Delay(1000); // let the Host's console queue drain

        recorder.WriteSamplesCsv(Path.Combine(options.Output, "samples.csv"), origin);
        var report = new RunReport(options, recorder, context, host, origin, startup, environment, agents, flood, burstLogins, end);
        await File.WriteAllTextAsync(Path.Combine(options.Output, "summary.md"), report.ToMarkdown());
        await File.WriteAllTextAsync(Path.Combine(options.Output, "summary.json"), report.Json());
        Console.WriteLine($"wrote {options.Output}/summary.md; errors {recorder.Errors.Count}");
        return 0;
    }

    private static async Task<(double, double, double)?> BurstLoginAsync(LoadContext context, string loginId, CancellationToken cancellationToken)
    {
        try
        {
            var (mId, ticket, register, launcher) = await context.Host.RegisterAndLoginAsync(loginId, cancellationToken);
            using var connection = await WireConnection.ConnectAsync(loginId, context.Host.GamePort, cancellationToken);
            await connection.InitialNotice.WaitAsync(WireConnection.RequestTimeout, cancellationToken);
            var login = await connection.RequestAsync(Protocol.Login, Protocol.LoginBody(mId, ticket), Protocol.Cmd(Protocol.Login), true, cancellationToken);
            context.Sent(Protocol.Name(Protocol.Login), Groups.Burst, login.SendTimestamp);
            context.Sent("STEP_FENCE", Groups.Burst, login.SendTimestamp);
            context.Recorder.Add(new Sample(Protocol.Name(Protocol.Login), Groups.Burst, loginId, login.SendTimestamp, login.FirstReplyTimestamp, login.EndTimestamp, login.BytesInWindow, login.TimedOut));
            return (register, launcher, login.FirstReplyTimestamp is { } first ? Stopwatch.GetElapsedTime(login.SendTimestamp, first).TotalMilliseconds : double.NaN);
        }
        catch (Exception error)
        {
            context.Recorder.Error(loginId, error.Message);
            return null;
        }
    }

    public static async Task DelayUntil(long origin, TimeSpan at)
    {
        var wait = at - Stopwatch.GetElapsedTime(origin);
        if (wait > TimeSpan.Zero) await Task.Delay(wait);
    }

    /// <summary>Kind of the synthetic censored sample for a schedule slot that was never sent.</summary>
    public const string UnsentKind = Sample.UnsentKind;

    // ------------------------------------------------------------------ ramp

    public static async Task<int> RampAsync(RunOptions options)
    {
        Directory.CreateDirectory(options.Output);
        var recorder = new Recorder();
        await using var host = await HostProcess.StartAsync(options.HostDll, options.Pvf, Path.Combine(options.Output, "host.log"), CancellationToken.None);
        var origin = host.ReadyTimestamp;
        var context = new LoadContext(host, recorder, options.Tag)
        {
            RatePerConnection = options.Rate,
            EntryGate = options.EntryConcurrency > 0 ? new SemaphoreSlim(options.EntryConcurrency) : null,
        };
        using var stop = new CancellationTokenSource();
        var agents = new List<Agent>();
        for (var index = 0; index < options.Solos; index++) agents.Add(new SoloAgent(context, index, options.Seed * 1000 + index, options.Weights));
        for (var index = 0; index < options.Pairs; index++) agents.Add(new PairAgent(context, index, options.Seed * 1000 + 100 + index, options.Dwell));
        var connections = options.Solos + options.Pairs * 2;
        var running = agents.Select(agent => Task.Run(() => agent.RunAsync(stop.Token))).ToList();
        await DelayUntil(origin, options.RampWarm);

        var text = new StringBuilder();
        text.AppendLine($"# 吞吐爬坡（合成负载）— {options.Tag}");
        text.AppendLine();
        text.AppendLine($"连接 {connections}（单人 {options.Solos}，双人组 {options.Pairs}），预热 {options.RampWarm.TotalSeconds:F0} s（按 {options.Rate * connections:F0} 包/秒，不计），" +
                        $"每档 {options.RampStep.TotalSeconds:F0} s，每档前 2 s 不计。");
        text.AppendLine();
        text.AppendLine("判定规则：某档的背景请求端到端 p99（判定口径：计划发出 → 第一条回包，截尾样本计入）> 50 ms、或该档出现错误（超时、断线、拒绝）、" +
                        "或实际包速率 < 目标的 90%，即停止；可持续吞吐取此前最后一个通过档的实际包速率。每档结束后先等计划发出在档内、仍在途的请求收口" +
                        "（完成、超时或断线，最多 65 s）再统计，负载在等待期间继续按本档速率运行。");
        text.AppendLine();
        var rows = new List<string[]>();
        double? sustainable = null;
        string? stopReason = null;
        foreach (var target in options.RampRates)
        {
            context.RatePerConnection = target / connections;
            var stepStart = Stopwatch.GetElapsedTime(origin);
            var errorsBefore = recorder.Errors.Count;
            await DelayUntil(origin, stepStart + options.RampStep);
            var from = origin + (long)((stepStart + TimeSpan.FromSeconds(2)).TotalSeconds * Stopwatch.Frequency);
            var to = origin + (long)((stepStart + options.RampStep).TotalSeconds * Stopwatch.Frequency);
            // Requests intended inside the step but still in flight are the slowest ones; wait for them
            // (they finish, time out or are closed) before counting the step. The load keeps running.
            var settled = Stopwatch.GetTimestamp();
            while (((context.OldestInFlight is { } oldest && oldest < to) || agents.Any(agent => agent.NextUnsentSlot < to))
                   && Stopwatch.GetElapsedTime(settled) < TimeSpan.FromSeconds(65))
                await Task.Delay(50);
            var countedAt = Stopwatch.GetTimestamp();
            var unsent = agents.SelectMany(agent => agent.UnsentSlots(to).Select(slot =>
                new Sample(UnsentKind, Groups.Background, agent.Name, countedAt, null, countedAt, 0, TimedOut: true, Scheduled: slot))).ToList();
            var seconds = (to - from) / (double)Stopwatch.Frequency;
            var stepPackets = context.PacketEvents.Where(e => e.At >= from && e.At < to && e.Group == Groups.Background).ToList();
            var packets = stepPackets.Count;
            var longPackets = stepPackets.Count(e => e.Kind is "SELECT_CHARACTER" or "CREATE_CHARACTER" or "SELECT_DUNGEON");
            var samples = recorder.Samples.Where(s => s.Group == Groups.Background && s.Intended >= from && s.Intended < to)
                .Concat(unsent.Where(s => s.Intended >= from)).ToList();
            var distribution = Distribution.OfSamples(samples.Where(s => s.Counted));
            var heavy = Distribution.OfSamples(samples.Where(s => s.Counted && s.Kind is "SELECT_CHARACTER" or "CREATE_CHARACTER" or "SELECT_DUNGEON"));
            var errors = recorder.Errors.Count - errorsBefore;
            var achieved = packets / seconds;
            var pass = distribution.P99 <= 50 && errors == 0 && achieved >= 0.9 * target;
            var actual = Distribution.OfActual(samples);
            rows.Add([target.ToString("F0", CultureInfo.InvariantCulture), achieved.ToString("F0", CultureInfo.InvariantCulture),
                distribution.Row(), Distribution.F(actual.P99),
                $"{longPackets}（{(packets == 0 ? 0 : 100.0 * longPackets / packets).ToString("F3", CultureInfo.InvariantCulture)}%）",
                Distribution.F(heavy.P99), (heavy.MaxIsLowerBound ? "≥" : "") + Distribution.F(heavy.Max),
                errors.ToString(CultureInfo.InvariantCulture), pass ? "通过" : "停止"]);
            Console.WriteLine($"ramp {target}: achieved {achieved:F0}, p99 {distribution.P99:F2}, errors {errors}");
            if (!pass)
            {
                stopReason = distribution.P99 > 50 ? "p99 > 50 ms" : errors > 0 ? "出现错误" : "实际速率 < 目标 90%";
                break;
            }

            sustainable = achieved;
        }

        stop.Cancel();
        await Task.WhenAll(running);
        Markdown.Table(text, ["目标包/秒", "实际包/秒", "判定：样本 | p50 | p95 | p99 | p99.9 | max (ms)", "参考（实际发出）p99", "长请求包数（占比）", "长请求 p99", "长请求 max", "错误", "判定"], rows);
        text.AppendLine(Distribution.Footnote);
        text.AppendLine();
        text.AppendLine($"可持续吞吐：**{(sustainable is null ? "无通过档" : sustainable.Value.ToString("F0", CultureInfo.InvariantCulture) + " 包/秒")}**；" +
                        $"停止原因：{stopReason ?? "全部档位通过（未触顶，最高档即下限）"}。");
        text.AppendLine();
        text.AppendLine("长请求 = SELECT_CHARACTER、CREATE_CHARACTER、SELECT_DUNGEON。错误清单：");
        foreach (var error in recorder.Errors.Take(20))
            text.AppendLine($"- t={Stopwatch.GetElapsedTime(origin, error.Timestamp).TotalSeconds:F1}s {error.Connection}: {error.Message}");
        await File.WriteAllTextAsync(Path.Combine(options.Output, "ramp.md"), text.ToString());
        recorder.WriteSamplesCsv(Path.Combine(options.Output, "samples.csv"), origin);
        Console.WriteLine($"wrote {options.Output}/ramp.md");
        return 0;
    }

    // ------------------------------------------------------------------ outbound

    /// <summary>
    /// TASK-01's sequences on an idle Host with exactly the connections involved, so every byte a
    /// connection receives in a request's window belongs to that request.
    /// </summary>
    public static async Task<int> OutboundAsync(RunOptions options)
    {
        Directory.CreateDirectory(options.Output);
        var recorder = new Recorder();
        await using var host = await HostProcess.StartAsync(options.HostDll, options.Pvf, Path.Combine(options.Output, "host.log"), CancellationToken.None);
        var context = new LoadContext(host, recorder, options.Tag);
        var frames = new StringBuilder();
        var exact = new OutboundAgent(context, frames);
        await exact.RunAsync(CancellationToken.None);

        var text = new StringBuilder();
        text.AppendLine("# 单条请求对单个连接的出站字节（精确，空闲 Host、仅涉及的连接）");
        text.AppendLine();
        text.AppendLine("口径：一个连接从这次请求发出到它的栅栏应答到达之间收到的全部帧字节（含 15 字节帧头，不含栅栏应答本身）。" +
                        "请求者用请求后紧跟的步骤栅栏收口；同伴在请求者的栅栏应答之后再发一个栅栏收口。序列取自 TASK-01 的表征步骤。");
        text.AppendLine();
        Markdown.Table(text, ["步骤", "请求", "连接", "角色", "字节", "帧数"],
            exact.Steps.Select(step => new[] { step.Step, step.Kind, step.Connection, step.Role, step.Bytes.ToString(CultureInfo.InvariantCulture), step.Frames.ToString(CultureInfo.InvariantCulture) }));
        var max = exact.Steps.MaxBy(step => step.Bytes)!;
        text.AppendLine($"本次最大值：**{max.Bytes} 字节**，{max.Kind}（{max.Step}，{max.Role} {max.Connection}）。");
        text.AppendLine();
        text.AppendLine("**这是新建角色（job 0，只有起始物品）上的实测值，不是选角出站量的上界。** 选角序列里多帧随角色数据增长，" +
                        "例如 ITEM_LIST（NOTI 0x000D）为「5 B ＋ 条目数 × 101 B」（protocol/game/server-to-client/NOTI-0x000D.md），本次 1520 B 即 5 ＋ 15 × 101。" +
                        "据此取的「2 倍」只是 RQ-036 下限的下限；上界须按各容器容量推算，或用容器装满的角色实测（T-003）。");
        if (recorder.Errors.Count > 0)
        {
            text.AppendLine();
            foreach (var error in recorder.Errors) text.AppendLine($"- 错误 {error.Connection}: {error.Message}");
        }

        await File.WriteAllTextAsync(Path.Combine(options.Output, "outbound.md"), text.ToString());
        await File.WriteAllTextAsync(Path.Combine(options.Output, "outbound-frames.txt"), frames.ToString());
        Console.WriteLine($"wrote {options.Output}/outbound.md");
        return recorder.Errors.Count == 0 ? 0 : 1;
    }
}

internal sealed record OutboundStep(string Step, string Kind, string Connection, string Role, long Bytes, int Frames);

/// <summary>One player (first select, return, reselect, create-and-select) and one pair (TASK-01 (c)(d)(f)).</summary>
internal sealed class OutboundAgent(LoadContext context, StringBuilder frames) : Agent(context, "outbound", 1)
{
    public List<OutboundStep> Steps { get; } = [];

    private readonly Dictionary<string, int> frameCounts = [];

    private WireConnection Watch(WireConnection connection)
    {
        connection.Tap = frame =>
        {
            lock (frames)
            {
                frames.AppendLine($"{connection.Label}\t{frame}");
                frameCounts[connection.Label] = frameCounts.GetValueOrDefault(connection.Label) + 1;
            }
        };
        return connection;
    }

    private int Count(WireConnection connection)
    {
        lock (frames) return frameCounts.GetValueOrDefault(connection.Label);
    }

    private void Mark(string step, params WireConnection[] connections)
    {
        lock (frames) foreach (var connection in connections) frames.AppendLine($"{connection.Label}\t## {step}");
    }

    private async Task Measure(string step, WireConnection requester, ushort type, byte[] body, Func<Frame, bool>? signature, CancellationToken stop, params WireConnection[] companions)
    {
        Mark(step, [requester, .. companions]);
        var starts = companions.Select(companion => (companion, Bytes: companion.PayloadBytes, Frames: Count(companion))).ToList();
        var framesBefore = Count(requester);
        var outcome = await RequestAsync(requester, type, body, signature, true, step, stop);
        // The step fence's own answer is one frame of the requester's count; it is not part of the bytes.
        Steps.Add(new OutboundStep(step, Protocol.Name(type), requester.Label, "请求者", outcome.BytesInWindow, Count(requester) - framesBefore - 1));
        foreach (var (companion, bytes, frameCount) in starts)
        {
            var fence = await companion.FenceAsync(stop);
            Steps.Add(new OutboundStep(step, Protocol.Name(type), companion.Label, "同伴", companion.PayloadBytes - bytes, Count(companion) - frameCount - 1));
        }
    }

    public override async Task RunAsync(CancellationToken stop)
    {
        try
        {
            // Solo: the selection sequence (TASK-01 (a)) and its repeats.
            var (mId, ticket, _, _) = await Context.Host.RegisterAndLoginAsync("outbound-solo", stop);
            var solo = Watch(await WireConnection.ConnectAsync("solo", Context.Host.GamePort, stop));
            await solo.InitialNotice.WaitAsync(WireConnection.RequestTimeout, stop);
            await Measure("solo login", solo, Protocol.Login, Protocol.LoginBody(mId, ticket), Protocol.Cmd(Protocol.Login), stop);
            await Measure("solo create", solo, Protocol.CreateCharacter, Protocol.CreateBody("Solo"), Protocol.Cmd(Protocol.CreateCharacter), stop);
            await Measure("solo first select", solo, Protocol.SelectCharacter, Protocol.SelectBody(0), Protocol.Cmd(Protocol.SelectCharacter), stop);
            await Measure("solo return to selection", solo, Protocol.ReturnToSelection, [], Protocol.Cmd(Protocol.ReturnToSelection), stop);
            await Measure("solo reselect", solo, Protocol.SelectCharacter, Protocol.SelectBody(0), Protocol.Cmd(Protocol.SelectCharacter), stop);
            await Measure("solo return to selection 2", solo, Protocol.ReturnToSelection, [], Protocol.Cmd(Protocol.ReturnToSelection), stop);
            await Measure("solo create second", solo, Protocol.CreateCharacter, Protocol.CreateBody("SoloTwo"), Protocol.Cmd(Protocol.CreateCharacter), stop);
            await Measure("solo select second", solo, Protocol.SelectCharacter, Protocol.SelectBody(1), Protocol.Cmd(Protocol.SelectCharacter), stop);

            // Pair: TASK-01 TwoPlayersInTown, FormParty, (c) entry, (d) leave, (f) leave party.
            var leader = Watch(await EnterQuietlyAsync("leader", "outbound-leader", "Leader", stop));
            var member = Watch(await EnterQuietlyAsync("member", "outbound-member", "Member", stop));
            await Measure("leader set user area 1:1", leader, Protocol.SetUserArea, Protocol.AreaBody(1, 0), Protocol.OwnArea(leader.WireId), stop, member);
            await Measure("member set user area 1:1", member, Protocol.SetUserArea, Protocol.AreaBody(1, 0), Protocol.OwnArea(member.WireId), stop, leader);
            await Measure("leader position", leader, Protocol.SetUserPosition, Protocol.PositionBody(900, 300, 1), Protocol.OwnPosition(leader.WireId), stop, member);
            await Measure("leader invites", leader, Protocol.RequestPeer, Protocol.RequestPeerBody(member.WireId), null, stop, member);
            await Measure("member accepts", member, Protocol.ResponsePeer, Protocol.ResponsePeerBody(leader.WireId), Protocol.Noti(0x0009), stop, leader);
            await Measure("leader opens dungeon selection", leader, Protocol.EnterSelectDungeon, Protocol.EnterSelectDungeonBody(), Protocol.DungeonFirst(Protocol.EnterSelectDungeon), stop, member);
            await Measure("leader selects dungeon 144", leader, Protocol.SelectDungeon, Protocol.SelectDungeonBody(Protocol.PartyDungeonId), Protocol.DungeonFirst(Protocol.SelectDungeon), stop, member);
            await Measure("leader finishes loading", leader, Protocol.FinishLoading, Protocol.FinishLoadingBody(), Protocol.Noti(0x001E), stop, member);
            await Measure("member finishes loading", member, Protocol.FinishLoading, Protocol.FinishLoadingBody(), Protocol.Noti(0x001E), stop, leader);
            await Measure("leader leaves the dungeon", leader, Protocol.Eplp, Protocol.LeaveDungeonBody(), Protocol.Cmd(Protocol.Eplp), stop, member);
            await Measure("leader position after run", leader, Protocol.SetUserPosition, Protocol.PositionBody(900, 300, 1), Protocol.OwnPosition(leader.WireId), stop, member);
            await Measure("leader opens dungeon selection 2", leader, Protocol.EnterSelectDungeon, Protocol.EnterSelectDungeonBody(), Protocol.DungeonFirst(Protocol.EnterSelectDungeon), stop, member);
            await Measure("leader selects dungeon 144 again", leader, Protocol.SelectDungeon, Protocol.SelectDungeonBody(Protocol.PartyDungeonId), Protocol.DungeonFirst(Protocol.SelectDungeon), stop, member);
            await Measure("leader finishes loading 2", leader, Protocol.FinishLoading, Protocol.FinishLoadingBody(), Protocol.Noti(0x001E), stop, member);
            await Measure("member finishes loading 2", member, Protocol.FinishLoading, Protocol.FinishLoadingBody(), Protocol.Noti(0x001E), stop, leader);
            await Measure("leader leaves the dungeon 2", leader, Protocol.Eplp, Protocol.LeaveDungeonBody(), Protocol.Cmd(Protocol.Eplp), stop, member);
            await Measure("member leaves the party", member, Protocol.LeaveParty, [], Protocol.Noti(0x0009), stop, leader);
            await Measure("leader invites again", leader, Protocol.RequestPeer, Protocol.RequestPeerBody(member.WireId), null, stop, member);
            await Measure("member accepts again", member, Protocol.ResponsePeer, Protocol.ResponsePeerBody(leader.WireId), Protocol.Noti(0x0009), stop, leader);
            await Measure("leader returns to selection in party", leader, Protocol.ReturnToSelection, [], Protocol.Cmd(Protocol.ReturnToSelection), stop, member);
            await Measure("leader reselects", leader, Protocol.SelectCharacter, Protocol.SelectBody(0), Protocol.Cmd(Protocol.SelectCharacter), stop, member);
        }
        catch (Exception error)
        {
            Context.Recorder.Error(Name, error.Message);
        }
    }

    /// <summary>Login, create and select without recording steps (the solo already measured them).</summary>
    private async Task<WireConnection> EnterQuietlyAsync(string label, string loginId, string name, CancellationToken stop)
    {
        var connection = await EnterWorldAsync(label, loginId, name, stop);
        await connection.FenceAsync(stop);
        return connection;
    }
}

/// <summary>Aggregates one full run into the baseline record.</summary>
internal sealed class RunReport(
    RunOptions options,
    Recorder recorder,
    LoadContext context,
    HostProcess host,
    long origin,
    TimeSpan startup,
    string environment,
    IReadOnlyList<Agent> agents,
    FloodAgent flood,
    List<(double RegisterMs, double LauncherLoginMs, double GameLoginMs)> burstLogins,
    long end)
{
    private long At(TimeSpan offset) => origin + (long)(offset.TotalSeconds * Stopwatch.Frequency);

    private double Seconds(long timestamp) => Stopwatch.GetElapsedTime(origin, timestamp).TotalSeconds;

    private IEnumerable<Sample> Background(long from, long to) =>
        recorder.Samples.Where(sample => sample.Group == Groups.Background && sample.Intended >= from && sample.Intended < to);

    private (long From, long To) SteadyWindow => (At(options.Cold), At(options.Cold + options.Steady));

    private readonly Dictionary<string, object> json = [];

    public string Json() => JsonSerializer.Serialize(json, new JsonSerializerOptions { WriteIndented = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals });

    public string ToMarkdown()
    {
        var text = new StringBuilder();
        var (from, to) = SteadyWindow;
        var connections = options.Solos + options.Pairs * 2;
        text.AppendLine($"# 基线测量 — {options.Tag}（合成负载）");
        text.AppendLine();
        text.AppendLine("> **合成负载**：用户没有提供 A21 客户端的真实游玩录制，负载由本工具按 TASK-01 表征序列合成（RQ-090 第②项）。");
        text.AppendLine();
        text.AppendLine("## 环境");
        text.AppendLine();
        text.AppendLine(environment);
        text.AppendLine($"- Host 冷启动：进程启动 → 启动器 HTTP 与游戏端口就绪 {startup.TotalMilliseconds:F0} ms");
        text.AppendLine();

        // ---- load composition
        text.AppendLine("## 负载");
        text.AppendLine();
        text.AppendLine($"- 连接：单人 {options.Solos}，双人组 {options.Pairs}（{options.Pairs * 2} 条），合计 {connections} 条背景连接；另有 1 条灌包连接（平时每 2 s 一个栅栏，不计入背景）。");
        text.AppendLine($"- 目标速率：每连接 {options.Rate:F0} 包/秒，合计 {options.Rate * connections:F0} 包/秒；种子 {options.Seed}；双人组每个状态停留 {options.Dwell.TotalSeconds:F0}–{options.Dwell.TotalSeconds * 1.5:F0} s。");
        text.AppendLine($"- 进入序列并发上限（登录→建角→选角，含重连）：{(options.EntryConcurrency > 0 ? options.EntryConcurrency.ToString(CultureInfo.InvariantCulture) : "不限（Host 就绪即全部同时进入）")}。");
        text.AppendLine($"- 单人动作权重：栅栏 {options.Weights.Fence}，移动 {options.Weights.Move}，跨区 {options.Weights.Area}，回选角+再选角 {options.Weights.Reselect}，回选角+建角+选新角 {options.Weights.ReselectWithCreate}。");
        text.AppendLine($"- 时间线（从 Host 就绪起）：冷启动窗 [0, {options.Cold.TotalSeconds:F0}) s；稳态窗 [{options.Cold.TotalSeconds:F0}, {(options.Cold + options.Steady).TotalSeconds:F0}) s；" +
                        $"之后 {options.Bursts} 轮 × {options.BurstSize} 个并发登录（每 {options.BurstEvery.TotalSeconds:F0} s 一轮）；然后灌包 {options.Flood.TotalSeconds:F0} s（窗口 {options.FloodWindow}）。总时长 {Seconds(end):F0} s。");
        var steadySeconds = (to - from) / (double)Stopwatch.Frequency;
        var steadyPackets = context.PacketEvents.Where(e => e.Group == Groups.Background && e.At >= from && e.At < to).ToList();
        text.AppendLine($"- 稳态窗实际包速率：{steadyPackets.Count / steadySeconds:F1} 包/秒（{steadyPackets.Count} 包 / {steadySeconds:F0} s，含步骤栅栏）。");
        text.AppendLine();
        text.AppendLine("稳态窗的负载构成（按入站包计；STEP_FENCE 是工具为收口回包窗口在请求后紧跟的栅栏，客户端本来也会周期性发这个包）：");
        text.AppendLine();
        Markdown.Table(text, ["请求", "包数", "占比"],
            steadyPackets.GroupBy(e => e.Kind).OrderByDescending(g => g.Count())
                .Select(g => new[] { g.Key, g.Count().ToString(CultureInfo.InvariantCulture), (100.0 * g.Count() / steadyPackets.Count).ToString("F2", CultureInfo.InvariantCulture) + "%" }));
        json["steadyRate"] = steadyPackets.Count / steadySeconds;
        json["composition"] = steadyPackets.GroupBy(e => e.Kind).ToDictionary(g => g.Key, g => g.Count());

        // ---- end to end
        text.AppendLine("## 端到端延迟（稳态窗）");
        text.AppendLine();
        text.AppendLine("判定口径：该请求的**计划发出时刻**（agent 节拍表上的时刻，落后时不重置）→ 第一条回包（按请求类型的回包签名识别，见 README）被接收线程读到。" +
                        "参考口径：实际写出请求帧的时刻 → 第一条回包。超时或连接被关闭、没等到回包的请求作为截尾样本计入（取到超时/关闭为止，是下界）；" +
                        "协议上本来没有回包的请求（如 REQUEST_PEER）不计入，单列「协议无回包」。" +
                        "基线上多段请求的首条回包可能早于整段结束；改造后按 VC-1 会推迟到 CMD 结束。");
        text.AppendLine();
        var steady = Background(from, to).ToList();
        text.AppendLine(LatencyTable(steady, out var steadyJson));
        json["steady"] = steadyJson;
        var overall = Distribution.OfSamples(steady.Where(s => s.Counted));
        json["steadyOverall"] = overall;
        json["steadyOverallActual"] = Distribution.OfActual(steady);
        json["steadyCensored"] = steady.Count(s => s.Censored);
        json["steadyProtocolNoReply"] = steady.Count(s => s.ProtocolNoReply);
        // Judged minus reference: how late each request left against its slot. Without a server stall this
        // is the tool's own timer lateness (Task.Delay granularity), a small pessimistic bias of the judged series.
        var lateness = Distribution.Of(steady.Where(s => s.Kind != Scenarios.UnsentKind).Select(s => Stopwatch.GetElapsedTime(s.Intended, s.Send).TotalMilliseconds));
        json["steadyLateness"] = lateness;
        text.AppendLine($"发出滞后（实际发出 − 计划发出）：{lateness.Row()}（样本 | p50 | p95 | p99 | p99.9 | max，ms）。没有服务端停顿时，这就是工具自身定时器的迟到，" +
                        "会让判定口径略偏大（偏保守）；有停顿时它包含该连接在停顿后积压待发的时间。");
        text.AppendLine();

        // ---- commands
        text.AppendLine("## 服务端命令执行时长（`CommandLoggingBehavior` 的 `ElapsedMilliseconds`）");
        text.AppendLine();
        text.AppendLine("范围：Host 就绪之后整次运行的全部命令（含冷启动窗与 (d)(e) 阶段）。读到日志行的时刻用于判断落在哪个窗口。");
        text.AppendLine();
        var commands = host.Commands.Where(c => c.ReadTimestamp >= origin).ToList();
        var commandRows = commands.GroupBy(c => c.CommandType).OrderByDescending(g => g.Max(c => c.ElapsedMs)).Select(g =>
        {
            var distribution = Distribution.Of(g.Select(c => c.ElapsedMs));
            var worst = g.MaxBy(c => c.ElapsedMs)!;
            return new[] { g.Key, g.First().Kind, distribution.Row(), Seconds(worst.ReadTimestamp).ToString("F1", CultureInfo.InvariantCulture), g.Count(c => c.Failed).ToString(CultureInfo.InvariantCulture) };
        }).ToList();
        Markdown.Table(text, ["命令", "执行方式", "样本 | p50 | p95 | p99 | p99.9 | max (ms)", "max 出现于 (s)", "失败"], commandRows);
        var warnings = commands.GroupBy(c => c.CommandType)
            .Select(g => (Name: g.Key, Distribution: Distribution.Of(g.Select(c => c.ElapsedMs)), Worst: g.MaxBy(c => c.ElapsedMs)!,
                ColdMax: g.Where(c => c.ReadTimestamp < At(options.Cold)).Select(c => c.ElapsedMs).DefaultIfEmpty(double.NaN).Max(),
                WarmMax: g.Where(c => c.ReadTimestamp >= At(options.Cold)).Select(c => c.ElapsedMs).DefaultIfEmpty(double.NaN).Max()))
            .Where(w => w.Distribution.P999 > 100 || w.Distribution.Max > 250).ToList();
        json["commands"] = commands.GroupBy(c => c.CommandType).ToDictionary(g => g.Key, g => Distribution.Of(g.Select(c => c.ElapsedMs)));
        json["commandWarnings"] = warnings.Select(w => new { w.Name, w.Distribution, w.ColdMax, w.WarmMax }).ToList();
        text.AppendLine($"命令日志行 {commands.Count} 条（其中失败 {commands.Count(c => c.Failed)}）。");
        text.AppendLine();

        // ---- cold start
        text.AppendLine($"## (f) 冷启动：Host 就绪后前 {options.Cold.TotalSeconds:F0} 秒");
        text.AppendLine();
        text.AppendLine("包含建号与选角在内的全部请求（setup 与 background 组，截尾样本计入），按计划发出时刻所在的秒聚合。" +
                        "最长单次停顿 = 窗口内的最大单请求延迟（判定口径：计划发出 → 第一条回包；截尾样本取到超时/关闭，是下界）。");
        text.AppendLine();
        var coldSamples = recorder.Samples.Where(s => s.Group is Groups.Setup or Groups.Background && s.Intended >= origin && s.Intended < At(options.Cold) && s.Counted).ToList();
        var coldRows = new List<string[]>();
        for (var second = 0; second < (int)options.Cold.TotalSeconds; second++)
        {
            var inSecond = coldSamples.Where(s => s.Intended >= At(TimeSpan.FromSeconds(second)) && s.Intended < At(TimeSpan.FromSeconds(second + 1))).ToList();
            var distribution = Distribution.OfSamples(inSecond);
            var worst = inSecond.MaxBy(s => s.LatencyMs);
            coldRows.Add([second.ToString(CultureInfo.InvariantCulture), distribution.Count.ToString(CultureInfo.InvariantCulture),
                Distribution.F(distribution.P99), Distribution.F(distribution.Max), worst?.Kind ?? "–"]);
        }

        var stall = coldSamples.MaxBy(s => s.LatencyMs);
        text.AppendLine($"最长单次停顿：**{(stall is { Censored: true } ? "≥" : "")}{(stall is null ? "–" : Distribution.F(stall.LatencyMs))} ms**" +
                        $"（{stall?.Kind}{(stall is { Censored: true } ? "，截尾：超时或连接被关闭" : "")}，计划发出 t={(stall is null ? 0 : Seconds(stall.Intended)):F2} s，{stall?.Connection}；" +
                        $"参考口径 {(stall is null ? "–" : Distribution.F(stall.ActualLatencyMs))} ms）。" +
                        $"冷启动窗整体：{Distribution.OfSamples(coldSamples).Row()}（样本 | p50 | p95 | p99 | p99.9 | max）。");
        text.AppendLine();
        json["coldStall"] = stall is null ? double.NaN : stall.LatencyMs;
        json["coldStallCensored"] = stall?.Censored ?? false;
        json["coldStallActual"] = stall is null ? double.NaN : stall.ActualLatencyMs;
        json["coldOverall"] = Distribution.OfSamples(coldSamples);
        json["coldPerSecond"] = coldRows;
        text.AppendLine("<details><summary>逐秒</summary>");
        text.AppendLine();
        Markdown.Table(text, ["秒", "样本", "p99 (ms)", "max (ms)", "max 的请求"], coldRows);
        text.AppendLine("</details>");
        text.AppendLine();

        // ---- (d)
        text.AppendLine("## (d) 10 个并发登录");
        text.AppendLine();
        text.AppendLine("每轮 10 个并发：启动器 HTTP 注册（口令哈希）→ 启动器 HTTP 登录（口令校验）→ TCP 连接 → LOGIN；LOGIN 应答后断开。" +
                        "「轮内」= 从该轮开始到 10 个都完成的区间；比较轮内发出的背景请求与稳态窗的端到端分布。");
        text.AppendLine();
        var bursts = recorder.Intervals.Where(i => i.Name.StartsWith("burst", StringComparison.Ordinal)).ToList();
        var inBursts = recorder.Samples.Where(s => s.Group == Groups.Background && s.Counted && bursts.Any(b => s.Intended >= b.Start && s.Intended < b.End)).ToList();
        var burstStart = bursts.Count > 0 ? bursts.Min(b => b.Start) : end;
        var burstEnd = bursts.Count > 0 ? bursts.Max(b => b.End) : end;
        var burstPhase = Background(burstStart, burstEnd).Where(s => s.Counted).ToList();
        Markdown.Table(text, ["背景请求", "样本 | p50 | p95 | p99 | p99.9 | max (ms)"],
        [
            ["稳态窗（对照）", overall.Row()],
            ["10 并发登录的轮内", Distribution.OfSamples(inBursts).Row()],
            ["（参考：实际发出口径）稳态窗", Distribution.OfActual(steady).Row()],
            ["（参考：实际发出口径）轮内", Distribution.OfActual(inBursts).Row()],
            [$"登录阶段整体（{Seconds(burstStart):F0}–{Seconds(burstEnd):F0} s）", Distribution.OfSamples(burstPhase).Row()],
        ]);
        text.AppendLine($"每轮时长（开始 → 10 个 LOGIN 都应答）：{string.Join(", ", bursts.Select(b => ((b.End - b.Start) * 1000.0 / Stopwatch.Frequency).ToString("F0", CultureInfo.InvariantCulture)))} ms。");
        text.AppendLine();
        // By connection role: each connection has its own schedule, so a role's numbers are its own.
        static string RoleOf(string connection) =>
            connection.StartsWith("solo", StringComparison.Ordinal) ? "单人"
            : connection.StartsWith("pair", StringComparison.Ordinal) && connection.EndsWith('L') ? "双人组领队"
            : connection.StartsWith("pair", StringComparison.Ordinal) && connection.EndsWith('M') ? "双人组队员"
            : "其他";
        var roleRows = new List<string[]>();
        var roleJson = new Dictionary<string, object>();
        foreach (var role in new[] { "单人", "双人组领队", "双人组队员" })
        {
            var steadyRole = steady.Where(s => RoleOf(s.Connection) == role).ToList();
            var burstRole = inBursts.Where(s => RoleOf(s.Connection) == role).ToList();
            var (judgedSteady, judgedBurst) = (Distribution.OfSamples(steadyRole), Distribution.OfSamples(burstRole));
            roleRows.Add([role, "稳态窗", judgedSteady.Row(), Distribution.F(Distribution.OfActual(steadyRole).P99)]);
            roleRows.Add([role, "轮内", judgedBurst.Row(), Distribution.F(Distribution.OfActual(burstRole).P99)]);
            roleJson[role] = new { steady = judgedSteady, burst = judgedBurst, burstActual = Distribution.OfActual(burstRole) };
        }

        text.AppendLine("按连接角色拆分（每条连接有自己的节拍表，一个角色的数字只含它自己的请求；双人组的组队、进本、离本由领队发起，记在领队连接上）：");
        text.AppendLine();
        Markdown.Table(text, ["连接角色", "窗口", "判定：样本 | p50 | p95 | p99 | p99.9 | max (ms)", "参考 p99"], roleRows);
        json["burstByRole"] = roleJson;
        Markdown.Table(text, ["登录者自身", "样本 | p50 | p95 | p99 | p99.9 | max (ms)"],
        [
            ["启动器注册（HTTP，含哈希）", Distribution.Of(burstLogins.Select(b => b.RegisterMs)).Row()],
            ["启动器登录（HTTP，含校验）", Distribution.Of(burstLogins.Select(b => b.LauncherLoginMs)).Row()],
            ["Game LOGIN 0x0001", Distribution.Of(burstLogins.Select(b => b.GameLoginMs)).Row()],
        ]);
        text.AppendLine(Distribution.Footnote);
        text.AppendLine();
        json["burstBackground"] = Distribution.OfSamples(inBursts);
        json["burstBackgroundActual"] = Distribution.OfActual(inBursts);
        json["burstPhase"] = Distribution.OfSamples(burstPhase);

        // ---- (e)
        text.AppendLine("## (e) 单连接灌包");
        text.AppendLine();
        var floodInterval = recorder.Intervals.FirstOrDefault(i => i.Name == "flood");
        var floodSamples = floodInterval is null ? [] : recorder.Samples.Where(s => s.Group == Groups.Flood && s.Send >= floodInterval.Start && s.Send < floodInterval.End).ToList();
        var others = floodInterval is null ? [] : Background(floodInterval.Start, floodInterval.End).Where(s => s.Counted).ToList();
        text.AppendLine($"灌包连接在城门区（不共享在场，无扇出）以窗口 {options.FloodWindow} 持续发 CHECK_USER_CONNECTION；实际应答速率 {flood.AchievedPerSecond:F0} 包/秒（{flood.Answered} 个）。");
        text.AppendLine();
        Markdown.Table(text, ["", "样本 | p50 | p95 | p99 | p99.9 | max (ms)"],
        [
            ["其余连接，稳态窗（对照）", overall.Row()],
            ["其余连接，灌包期间", Distribution.OfSamples(others).Row()],
            ["（参考：实际发出口径）其余连接，灌包期间", Distribution.OfActual(others).Row()],
            ["灌包连接自身（含其窗口内排队）", Distribution.OfSamples(floodSamples).Row()],
        ]);
        text.AppendLine($"灌包窗口 {options.FloodWindow} 决定灌包连接最多有多少个请求同时在服务端排队；改造后它在单执行器队列里最多占这么多项，(e) 的结果直接取决于这个窗口（见 README）。");
        text.AppendLine();
        json["floodOthers"] = Distribution.OfSamples(others);
        json["floodOthersActual"] = Distribution.OfActual(others);
        json["floodRate"] = flood.AchievedPerSecond;

        // ---- queue wait
        text.AppendLine("## 入队等待");
        text.AppendLine();
        text.AppendLine("基线不存在：基线没有执行器，也就没有「入队 → 开始执行」这个量（RQ-090）。上面的 (d)(e)(f) 用端到端延迟作对照口径，不拿别的量代替入队等待。");
        text.AppendLine();

        // ---- outbound bytes under load
        text.AppendLine("## 负载下的单请求出站字节（每类请求、每个收件连接的最大值）");
        text.AppendLine();
        text.AppendLine("口径：带步骤栅栏的请求，请求者从发出到栅栏应答之间收到的字节（不含栅栏应答）；双人组的同伴从请求发出到它自己的收口栅栏应答之间收到的字节。" +
                        "负载下其他玩家的城镇投影也会落进窗口，所以这里是「含他人投影」的上界；精确值见 outbound 运行。");
        text.AppendLine();
        Markdown.Table(text, ["请求", "角色", "窗口数", "最大字节", "出自场景", "时刻 (s)"],
            recorder.Windows.GroupBy(w => (w.Kind, w.Role)).OrderByDescending(g => g.Max(w => w.Bytes)).Select(g =>
            {
                var worst = g.MaxBy(w => w.Bytes)!;
                return new[] { g.Key.Kind, g.Key.Role == "requester" ? "请求者" : "同伴", g.Count().ToString(CultureInfo.InvariantCulture),
                    worst.Bytes.ToString(CultureInfo.InvariantCulture), worst.Scenario, Seconds(worst.Send).ToString("F1", CultureInfo.InvariantCulture) };
            }));
        json["outboundMax"] = recorder.Windows.GroupBy(w => w.Kind + "|" + w.Role).ToDictionary(g => g.Key, g => g.Max(w => w.Bytes));

        // ---- RQ-091
        text.AppendLine("## 与 RQ-091 阈值的对照（基线预判）");
        text.AppendLine();
        Markdown.Table(text, ["项", "阈值", "基线（本次）", "说明"],
        [
            ["(b) 可持续吞吐", "≥ 2000 CMD/秒", "见 ramp 运行", "本运行只跑 400 包/秒"],
            ["(c) 400/秒下等待 p99", "≤ 50 ms", "基线不存在", $"端到端对照：p99 {Distribution.F(overall.P99)} ms"],
            ["(d) 10 并发登录下 p99", "≤ 50 ms", "基线不存在", $"端到端对照：轮内 p99 {Distribution.F(Distribution.OfSamples(inBursts).P99)} ms"],
            ["(e) 灌包下其他连接 p99", "≤ 50 ms", "基线不存在", $"端到端对照：p99 {Distribution.F(Distribution.OfSamples(others).P99)} ms"],
            ["单条命令 p99.9 / max", "≤ 100 / ≤ 250 ms", $"{warnings.Count} 个命令超阈值", "见下"],
            ["(f) 最长单次停顿", "≤ 250 ms", stall is null ? "–" : Distribution.F(stall.LatencyMs) + " ms", "冷启动窗，端到端"],
        ]);
        text.AppendLine("M-1 预警（命令执行时长 p99.9 > 100 ms 或 max > 250 ms）：");
        text.AppendLine();
        if (warnings.Count == 0) text.AppendLine("- 无。");
        foreach (var warning in warnings.OrderByDescending(w => w.Distribution.Max))
            text.AppendLine($"- `{warning.Name}`：样本 {warning.Distribution.Count}，p99.9 {Distribution.F(warning.Distribution.P999)} ms，max {Distribution.F(warning.Distribution.Max)} ms" +
                            $"（出现于 t={Seconds(warning.Worst.ReadTimestamp):F1} s；冷启动窗内 max {Distribution.F(warning.ColdMax)} ms，之后 max {Distribution.F(warning.WarmMax)} ms）");
        text.AppendLine();

        // ---- errors
        text.AppendLine("## 错误与无回包");
        text.AppendLine();
        text.AppendLine($"错误 {recorder.Errors.Count} 条；背景 agent 重连 {agents.Sum(a => a.Reconnects)} 次（失败后重新取票、LOGIN、选角，双人组两条一起重连）；" +
                        $"双人组进本 {agents.OfType<PairAgent>().Sum(p => p.Entries)} 次；服务端关闭连接或超时的请求 {recorder.Samples.Count(s => s.Closed || s.TimedOut)} 个。");
        text.AppendLine();
        var stalls = recorder.Samples.Where(s => s.Group is Groups.Setup or Groups.Background && s.Counted && s.LatencyMs > 250)
            .OrderBy(s => s.Send).ToList();
        text.AppendLine($"判定口径端到端 > 250 ms 的请求（setup 与 background，含截尾样本）：{stalls.Count} 个；" +
                        $"截尾样本（超时或连接被关闭）{recorder.Samples.Count(s => s.Group is Groups.Setup or Groups.Background && s.Censored)} 个，" +
                        $"协议无回包 {recorder.Samples.Count(s => s.Group is Groups.Setup or Groups.Background && s.ProtocolNoReply)} 个。");
        foreach (var group in stalls.GroupBy(s => s.Kind).OrderByDescending(g => g.Count()))
            text.AppendLine($"- {group.Key}：{group.Count()} 个（其中截尾 {group.Count(s => s.Censored)}），最大 {group.Max(s => s.LatencyMs):F0} ms，" +
                            $"计划发出时刻 {string.Join(", ", group.Take(12).Select(s => Seconds(s.Intended).ToString("F1", CultureInfo.InvariantCulture)))}{(group.Count() > 12 ? " …" : "")} s");
        text.AppendLine();
        var failedCommands = host.Commands.Where(c => c.Failed && c.ReadTimestamp >= origin).ToList();
        text.AppendLine($"失败的命令 {failedCommands.Count} 条：" + string.Join("；", failedCommands.GroupBy(c => c.CommandType)
            .Select(g => $"{g.Key} ×{g.Count()}（最长 {g.Max(c => c.ElapsedMs):F0} ms）")));
        foreach (var error in recorder.Errors.Take(30))
            text.AppendLine($"- t={Seconds(error.Timestamp):F1}s {error.Connection}: {error.Message}");
        text.AppendLine();
        json["errors"] = recorder.Errors.Count;
        json["stallsOver250"] = stalls.Count;
        json["failedCommands"] = failedCommands.Count;
        json["entries"] = agents.OfType<PairAgent>().Sum(p => p.Entries);
        json["hostLogLines"] = host.LogLines;
        return text.ToString();
    }

    private static string LatencyTable(List<Sample> samples, out Dictionary<string, object> json)
    {
        var text = new StringBuilder();
        var local = new Dictionary<string, object>();
        string[] Row(string name, IEnumerable<Sample> group)
        {
            var list = group.ToList();
            var judged = Distribution.OfSamples(list);
            var actual = Distribution.OfActual(list);
            return [name, judged.Row(), list.Count(s => s.ProtocolNoReply).ToString(CultureInfo.InvariantCulture),
                Distribution.F(actual.P99), Distribution.F(actual.P999), (actual.MaxIsLowerBound ? "≥" : "") + Distribution.F(actual.Max)];
        }

        var rows = samples.GroupBy(s => s.Kind).OrderByDescending(g => g.Count()).Select(g =>
        {
            local[g.Key] = new
            {
                distribution = Distribution.OfSamples(g),
                actual = Distribution.OfActual(g),
                censored = g.Count(s => s.Censored),
                protocolNoReply = g.Count(s => s.ProtocolNoReply),
            };
            return Row(g.Key, g);
        }).ToList();
        rows.Add(Row("**全部计入的请求**", samples));
        Markdown.Table(text, ["请求", "判定：样本 | p50 | p95 | p99 | p99.9 | max (ms)", "协议无回包", "参考（实际发出）p99", "参考 p99.9", "参考 max"], rows);
        text.AppendLine(Distribution.Footnote);
        json = local;
        return text.ToString();
    }
}
