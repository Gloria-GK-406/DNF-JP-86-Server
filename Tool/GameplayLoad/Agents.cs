using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace GameplayLoad;

/// <summary>Shared state of one load run.</summary>
internal sealed class LoadContext(HostProcess host, Recorder recorder, string runTag)
{
    public HostProcess Host { get; } = host;

    public Recorder Recorder { get; } = recorder;

    public string RunTag { get; } = runTag;

    /// <summary>Target inbound packets per second per connection; the ramp changes it while agents run.</summary>
    public double RatePerConnection { get; set; } = 20;

    /// <summary>
    /// When set, limits how many agents run their entry sequence (login, create, select) at once.
    /// Unset by default: every agent enters the moment the Host is ready.
    /// </summary>
    public SemaphoreSlim? EntryGate { get; init; }

    /// <summary>Packet send events: (timestamp, kind, group). Step fences are kind STEP_FENCE.</summary>
    public ConcurrentQueue<(long At, string Kind, string Group)> PacketEvents { get; } = new();

    public void Sent(string kind, string group, long at) => PacketEvents.Enqueue((at, kind, group));

    private readonly ConcurrentDictionary<long, long> inFlight = new();
    private long inFlightIds;

    /// <summary>Marks a request in flight from <paramref name="intended"/> until the returned handle is disposed.</summary>
    public IDisposable Track(long intended)
    {
        var id = Interlocked.Increment(ref inFlightIds);
        inFlight[id] = intended;
        return new InFlight(this, id);
    }

    /// <summary>The earliest intended send among requests still in flight, if any.</summary>
    public long? OldestInFlight => inFlight.IsEmpty ? null : inFlight.Values.DefaultIfEmpty(long.MaxValue).Min();

    private sealed class InFlight(LoadContext owner, long id) : IDisposable
    {
        public void Dispose() => owner.inFlight.TryRemove(id, out _);
    }
}

/// <summary>
/// One connection's timetable. The schedule never resets: a connection that fell behind (a long
/// action, a stall, a reconnect) sends its next actions back to back until it is on schedule again,
/// and each action's first request carries its slot, so the judged latency (slot → first reply)
/// includes the time the request spent waiting to be sent. This is the coordinated-omission correction.
/// Every connection has its own schedule, so one connection's slow request is never charged to another.
/// </summary>
internal sealed class Schedule(LoadContext context)
{
    private readonly object sync = new();
    private double nextAtMs;
    private double intervalMs;

    /// <summary>The slot of the next paced action, consumed by its first request.</summary>
    private long? slot;

    private static long ToTimestamp(double milliseconds) => (long)(milliseconds * Stopwatch.Frequency / 1000.0);

    public long? TakeSlot()
    {
        lock (sync)
        {
            var taken = slot;
            slot = null;
            return taken;
        }
    }

    /// <summary>After an action of <paramref name="packets"/> packets, waits for this connection's next slot.</summary>
    public async Task PaceAsync(int packets, CancellationToken cancellationToken)
    {
        double wait;
        lock (sync)
        {
            var now = Stopwatch.GetElapsedTime(0).TotalMilliseconds;
            if (nextAtMs == 0) nextAtMs = now;
            intervalMs = 1000.0 / context.RatePerConnection;
            nextAtMs += packets * intervalMs;
            slot = ToTimestamp(nextAtMs);
            wait = nextAtMs - now;
        }

        if (wait >= 1) await Task.Delay(TimeSpan.FromMilliseconds(wait), cancellationToken);
    }

    /// <summary>
    /// Counts packets this connection sent as its part of an action another connection drove (a pair
    /// step), so its own rate stays at the target. They carry no slot: the time they waited for the
    /// other side is not theirs.
    /// </summary>
    public void Charge(int packets)
    {
        lock (sync)
        {
            if (nextAtMs != 0 && intervalMs > 0) nextAtMs += packets * intervalMs;
        }
    }

    /// <summary>
    /// Slots (one per packet at the current rate) due before <paramref name="until"/> and not sent.
    /// One sample per packet interval, while a multi-packet action would have yielded only one or two
    /// judged samples, so under a large backlog this slightly overcounts; it only matters when the
    /// run is already far over the thresholds.
    /// </summary>
    public IReadOnlyList<long> UnsentSlots(long until)
    {
        double first, interval;
        lock (sync)
        {
            if (nextAtMs == 0 || intervalMs <= 0) return [];
            // A pending slot has not been sent yet; a taken one belongs to the request now in flight.
            first = slot is not null ? nextAtMs : nextAtMs + intervalMs;
            interval = intervalMs;
        }

        var slots = new List<long>();
        for (var at = first; ToTimestamp(at) < until; at += interval) slots.Add(ToTimestamp(at));
        return slots;
    }

    public long NextUnsentSlot
    {
        get
        {
            lock (sync) return nextAtMs == 0 ? long.MaxValue : ToTimestamp(slot is not null ? nextAtMs : nextAtMs + intervalMs);
        }
    }
}

/// <summary>Request helpers every agent uses; each call records its sample, its packets and its byte window.</summary>
internal abstract class Agent(LoadContext context, string name, int seed)
{
    protected LoadContext Context { get; } = context;

    protected Random Random { get; } = new(seed);

    public string Name { get; } = name;

    public bool Broken { get; protected set; }

    protected string Group { get; set; } = Groups.Setup;

    /// <summary>The schedule of the agent's (first) connection.</summary>
    protected Schedule Primary { get; } = new(context);

    /// <summary>Which schedule a connection's requests take their slot from.</summary>
    protected virtual Schedule ScheduleOf(WireConnection connection) => Primary;

    protected virtual IEnumerable<Schedule> Schedules => [Primary];

    /// <summary>Unsent slots of all this agent's connections (only while it is in the background group).</summary>
    public IEnumerable<long> UnsentSlots(long until) =>
        Group != Groups.Background ? [] : Schedules.SelectMany(schedule => schedule.UnsentSlots(until)).ToList();

    public long NextUnsentSlot => Group != Groups.Background ? long.MaxValue : Schedules.Min(schedule => schedule.NextUnsentSlot);

    protected async Task<RequestOutcome> RequestAsync(
        WireConnection connection,
        ushort type,
        byte[] body,
        Func<Frame, bool>? signature,
        bool stepFence,
        string scenario,
        CancellationToken cancellationToken,
        string? kind = null,
        bool takesSlot = true)
    {
        kind ??= Protocol.Name(type);
        var scheduled = takesSlot ? ScheduleOf(connection).TakeSlot() : null;
        using var inFlight = Context.Track(scheduled ?? Stopwatch.GetTimestamp());
        RequestOutcome outcome;
        try
        {
            outcome = await connection.RequestAsync(type, body, signature, stepFence, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            RecordAbandoned(kind, connection.Label, scheduled);
            throw;
        }

        Context.Sent(kind, Group, outcome.SendTimestamp);
        if (stepFence) Context.Sent("STEP_FENCE", Group, outcome.SendTimestamp);
        Context.Recorder.Add(new Sample(kind, Group, connection.Label, outcome.SendTimestamp, outcome.FirstReplyTimestamp,
            outcome.EndTimestamp, outcome.BytesInWindow, outcome.TimedOut, outcome.Closed, scheduled));
        if (stepFence && !outcome.Closed && !outcome.TimedOut)
            Context.Recorder.Add(new ByteWindow(kind, "requester", scenario, connection.Label, outcome.SendTimestamp, outcome.BytesInWindow));
        if (outcome.TimedOut) throw new TimeoutException($"{connection.Label}: {kind} timed out.");
        if (outcome.Closed)
            throw new IOException($"{connection.Label}: connection closed during {kind} after " +
                                  $"{Stopwatch.GetElapsedTime(outcome.SendTimestamp, outcome.EndTimestamp).TotalMilliseconds:F0} ms " +
                                  $"(first reply {(outcome.FirstReply is { } reply ? reply + " " + Convert.ToHexString(reply.Body.AsSpan(0, Math.Min(4, reply.Body.Length))) : "none")}).");
        return outcome;
    }

    /// <summary>A request still waiting when the run stopped: censored at the stop.</summary>
    private void RecordAbandoned(string kind, string connection, long? scheduled)
    {
        var now = Stopwatch.GetTimestamp();
        Context.Recorder.Add(new Sample(kind, Group, connection, scheduled ?? now, null, now, 0, TimedOut: true, Scheduled: scheduled));
    }

    /// <summary>Fences the companion after the requester's step and records what the companion received.</summary>
    protected async Task CompanionFenceAsync(
        WireConnection companion,
        long startBytes,
        long send,
        string kind,
        string scenario,
        CancellationToken cancellationToken)
    {
        var outcome = await companion.FenceAsync(cancellationToken);
        Context.Sent("STEP_FENCE", Group, outcome.SendTimestamp);
        if (outcome.TimedOut) throw new TimeoutException($"{companion.Label}: companion fence timed out.");
        if (outcome.Closed) throw new IOException($"{companion.Label}: connection closed during the companion fence.");
        Context.Recorder.Add(new ByteWindow(kind, "companion", scenario, companion.Label, send, companion.PayloadBytes - startBytes));
    }

    /// <summary>A bare fence as a light packet: its own sample, answered by CMD 0x04DD.</summary>
    protected async Task LightFenceAsync(WireConnection connection, CancellationToken cancellationToken)
    {
        var scheduled = ScheduleOf(connection).TakeSlot();
        using var inFlight = Context.Track(scheduled ?? Stopwatch.GetTimestamp());
        RequestOutcome outcome;
        try
        {
            outcome = await connection.FenceAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            RecordAbandoned(Protocol.Name(Protocol.Fence), connection.Label, scheduled);
            throw;
        }

        Context.Sent(Protocol.Name(Protocol.Fence), Group, outcome.SendTimestamp);
        Context.Recorder.Add(new Sample(Protocol.Name(Protocol.Fence), Group, connection.Label, outcome.SendTimestamp,
            outcome.FirstReplyTimestamp, outcome.EndTimestamp, outcome.BytesInWindow, outcome.TimedOut, outcome.Closed, scheduled));
        if (outcome.TimedOut) throw new TimeoutException($"{connection.Label}: fence timed out.");
        if (outcome.Closed) throw new IOException($"{connection.Label}: connection closed during a fence.");
    }

    protected async Task LightMoveAsync(WireConnection connection, CancellationToken cancellationToken, Random? random = null)
    {
        random ??= Random;
        var body = Protocol.PositionBody((short)random.Next(400, 1900), (short)random.Next(220, 380), (byte)random.Next(0, 8));
        await RequestAsync(connection, Protocol.SetUserPosition, body, Protocol.OwnPosition(connection.WireId), false, "town", cancellationToken);
    }

    /// <summary>Launcher registration, then LOGIN, CREATE_CHARACTER and SELECT_CHARACTER on a new connection.</summary>
    protected async Task<WireConnection> EnterWorldAsync(string label, string loginId, string characterName, CancellationToken cancellationToken)
    {
        if (Context.EntryGate is { } gate) await gate.WaitAsync(cancellationToken);
        try
        {
            var (mId, ticket, _, _) = await Context.Host.RegisterAndLoginAsync(loginId, cancellationToken);
            var connection = await LoginAsync(label, mId, ticket, cancellationToken);
            try
            {
                await CreateAsync(connection, characterName, cancellationToken);
                await SelectAsync(connection, 0, "first selection", cancellationToken);
                return connection;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }
        finally
        {
            Context.EntryGate?.Release();
        }
    }

    /// <summary>
    /// After a failure (the server closed the connection, or a request timed out): a fresh launcher
    /// ticket for the existing account, LOGIN, and SELECT_CHARACTER of <paramref name="rosterIndex"/>.
    /// </summary>
    protected async Task<WireConnection> ReenterWorldAsync(string label, string loginId, int rosterIndex, CancellationToken cancellationToken)
    {
        if (Context.EntryGate is { } gate) await gate.WaitAsync(cancellationToken);
        try
        {
            var (mId, ticket) = await Context.Host.LoginAsync(loginId, cancellationToken);
            var connection = await LoginAsync(label, mId, ticket, cancellationToken);
            try
            {
                await SelectAsync(connection, rosterIndex, "selection after reconnect", cancellationToken);
                return connection;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }
        finally
        {
            Context.EntryGate?.Release();
        }
    }

    private async Task<WireConnection> LoginAsync(string label, string mId, string ticket, CancellationToken cancellationToken)
    {
        var connection = await WireConnection.ConnectAsync(label, Context.Host.GamePort, cancellationToken);
        try
        {
            var notice = connection.InitialNotice;
            await notice.WaitAsync(WireConnection.RequestTimeout, cancellationToken);
            var login = await RequestAsync(connection, Protocol.Login, Protocol.LoginBody(mId, ticket), Protocol.Cmd(Protocol.Login), true, "login", cancellationToken);
            if (login.FirstReply?.Body is not [1, ..]) throw new InvalidOperationException($"{label}: LOGIN refused.");
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public int Reconnects { get; protected set; }

    /// <summary>Records a failure and backs off briefly before the agent reconnects.</summary>
    protected async Task RecoverAsync(Exception error, CancellationToken stop)
    {
        Context.Recorder.Error(Name, error.Message);
        Reconnects++;
        await Task.Delay(500, stop);
    }

    protected async Task CreateAsync(WireConnection connection, string characterName, CancellationToken cancellationToken)
    {
        var created = await RequestAsync(connection, Protocol.CreateCharacter, Protocol.CreateBody(characterName),
            Protocol.Cmd(Protocol.CreateCharacter), true, "create", cancellationToken);
        if (created.FirstReply?.Body is not [1, ..]) throw new InvalidOperationException($"{connection.Label}: CREATE_CHARACTER {characterName} refused.");
    }

    protected async Task SelectAsync(WireConnection connection, int rosterIndex, string scenario, CancellationToken cancellationToken)
    {
        var selected = await RequestAsync(connection, Protocol.SelectCharacter, Protocol.SelectBody(rosterIndex),
            Protocol.Cmd(Protocol.SelectCharacter), true, scenario, cancellationToken);
        if (selected.FirstReply is not { Body.Length: > 10 } answer || answer.Body[0] != 1)
            throw new InvalidOperationException($"{connection.Label}: SELECT_CHARACTER {rosterIndex} refused.");
        connection.WireId = BinaryPrimitives.ReadUInt16LittleEndian(answer.Body.AsSpan(9));
    }

    protected async Task MoveAreaAsync(WireConnection connection, byte area, byte leaving, CancellationToken cancellationToken)
    {
        await RequestAsync(connection, Protocol.SetUserArea, Protocol.AreaBody(area, leaving), Protocol.OwnArea(connection.WireId), true, "town", cancellationToken);
    }

    /// <summary>Paces a single-connection agent on its schedule.</summary>
    protected Task PaceAsync(int packets, CancellationToken cancellationToken) => Primary.PaceAsync(packets, cancellationToken);

    public abstract Task RunAsync(CancellationToken stop);
}

/// <summary>
/// A player on its own: light packets in town, area crossings, and now and then a return to the
/// selection screen followed by a selection (sometimes of a newly created character).
/// </summary>
internal sealed class SoloAgent(LoadContext context, int index, int seed, SoloWeights weights) : Agent(context, $"solo{index:D2}", seed)
{
    private WireConnection? connection;
    private int roster = 1;
    private byte area;

    public override async Task RunAsync(CancellationToken stop)
    {
        var registered = false;
        while (!stop.IsCancellationRequested)
        {
            try
            {
                if (!registered)
                {
                    registered = true;
                    connection = await EnterWorldAsync(Name, LoginId, CharacterName(0), stop);
                }
                else
                {
                    connection = await ReenterWorldAsync(Name, LoginId, 0, stop);
                }

                area = 0;
                Group = Groups.Background;
                while (!stop.IsCancellationRequested)
                {
                    var packets = await ActAsync(stop);
                    await PaceAsync(packets, stop);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                connection?.Dispose();
                connection = null;
                try
                {
                    await RecoverAsync(error, stop);
                }
                catch (OperationCanceledException)
                {
                }
            }
        }

        connection?.Dispose();
    }

    private string LoginId => $"{Context.RunTag}-{Name}";

    private string CharacterName(int ordinal) => $"S{Context.RunTag}{Name[4..]}{ordinal:D2}";

    private async Task<int> ActAsync(CancellationToken stop)
    {
        var connection = this.connection!;
        var roll = Random.NextDouble() * (weights.Fence + weights.Move + weights.Area + weights.Reselect + weights.ReselectWithCreate);
        if (weights.Reselect + weights.ReselectWithCreate <= 0) roll = Math.Min(roll, weights.Fence + weights.Move + weights.Area - 1e-9);
        if ((roll -= weights.Fence) < 0)
        {
            await LightFenceAsync(connection, stop);
            return 1;
        }

        if ((roll -= weights.Move) < 0)
        {
            await LightMoveAsync(connection, stop);
            return 1;
        }

        if ((roll -= weights.Area) < 0)
        {
            var target = area == 0 ? (byte)1 : (byte)0;
            await MoveAreaAsync(connection, target, area, stop);
            area = target;
            return 2;
        }

        if ((roll -= weights.Reselect) < 0 || roster >= 32)
        {
            await ReturnToSelectionAsync(connection, stop);
            await SelectAsync(connection, Random.Next(roster), "reselection", stop);
            area = 0;
            return 4;
        }

        await ReturnToSelectionAsync(connection, stop);
        await CreateAsync(connection, CharacterName(roster), stop);
        roster++;
        await SelectAsync(connection, roster - 1, "selection after create", stop);
        area = 0;
        return 6;
    }

    private Task<RequestOutcome> ReturnToSelectionAsync(WireConnection connection, CancellationToken stop) =>
        RequestAsync(connection, Protocol.ReturnToSelection, [], Protocol.Cmd(Protocol.ReturnToSelection), true, "return to selection", stop);
}

internal sealed record SoloWeights(double Fence, double Move, double Area, double Reselect, double ReselectWithCreate);

/// <summary>
/// Two players who meet in area 1:1 and cycle: form a party, enter dungeon 144, both finish loading,
/// the leader leaves the run (EPLP 0101), the member leaves the party. Between steps both send light packets.
/// </summary>
/// <remarks>
/// The two connections are two players: each runs its own loop on its own schedule, so the member
/// keeps sending its light packets while a leader step waits on the server. The cycle is driven from
/// the leader's loop, and a step counts on the connection that starts the action (its first request
/// carries the leader's slot). The member's part of a step (its own requests, its companion fences)
/// is sent from the leader's loop under the member's connection gate, without a slot, and is charged
/// to the member's schedule; the time it waited for the leader is not in its samples.
/// </remarks>
internal sealed class PairAgent(LoadContext context, int index, int seed, TimeSpan dwell) : Agent(context, $"pair{index:D2}", seed)
{
    private enum State { Town, Party, Dungeon, PartyAfterRun }

    private readonly Schedule memberSchedule = new(context);
    private readonly SemaphoreSlim memberGate = new(1, 1);
    private readonly Random memberRandom = new(seed * 31 + 7);
    private WireConnection? leader;
    private WireConnection? member;
    private volatile int stateValue;

    /// <summary>
    /// Set while the leader moves the party in or out of a dungeon. SET_USER_POSITION is answered only
    /// while the character stands in a town area (a report from anywhere else is ignored without a
    /// reply), so during the move the member sends fences only.
    /// </summary>
    private volatile bool moving;
    private long stateSince;

    private State CurrentState
    {
        get => (State)stateValue;
        set => stateValue = (int)value;
    }

    public int Entries { get; private set; }

    protected override Schedule ScheduleOf(WireConnection connection) => ReferenceEquals(connection, member) ? memberSchedule : Primary;

    protected override IEnumerable<Schedule> Schedules => [Primary, memberSchedule];

    public override async Task RunAsync(CancellationToken stop)
    {
        var (leaderLogin, memberLogin) = ($"{Context.RunTag}-{Name}L", $"{Context.RunTag}-{Name}M");
        var (leaderRegistered, memberRegistered) = (false, false);
        while (!stop.IsCancellationRequested)
        {
            try
            {
                if (!leaderRegistered)
                {
                    leaderRegistered = true;
                    leader = await EnterWorldAsync(Name + "L", leaderLogin, $"L{Context.RunTag}{Name[4..]}", stop);
                }
                else
                {
                    leader = await ReenterWorldAsync(Name + "L", leaderLogin, 0, stop);
                }

                if (!memberRegistered)
                {
                    memberRegistered = true;
                    member = await EnterWorldAsync(Name + "M", memberLogin, $"M{Context.RunTag}{Name[4..]}", stop);
                }
                else
                {
                    member = await ReenterWorldAsync(Name + "M", memberLogin, 0, stop);
                }

                await MoveAreaAsync(leader, 1, 0, stop);
                var start = member.PayloadBytes;
                var send = Stopwatch.GetTimestamp();
                await MoveAreaAsync(member, 1, 0, stop);
                await CompanionFenceAsync(leader, start, send, Protocol.Name(Protocol.SetUserArea), "town", stop);
                Group = Groups.Background;
                CurrentState = State.Town;
                moving = false;

                using var session = CancellationTokenSource.CreateLinkedTokenSource(stop);
                var loops = new[] { LeaderLoopAsync(session.Token), MemberLoopAsync(session.Token) };
                await Task.WhenAny(loops);
                session.Cancel();
                try
                {
                    await Task.WhenAll(loops);
                }
                catch (Exception)
                {
                    // Inspected below; the one that failed first decides.
                }

                if (stop.IsCancellationRequested) break;
                var failed = loops.FirstOrDefault(loop => loop.IsFaulted);
                throw failed?.Exception?.InnerException ?? new InvalidOperationException($"{Name}: a pair loop ended.");
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                // Either side failing ends both sessions; the pair starts over in town.
                leader?.Dispose();
                member?.Dispose();
                (leader, member) = (null, null);
                try
                {
                    await RecoverAsync(error, stop);
                }
                catch (OperationCanceledException)
                {
                }
            }
        }

        leader?.Dispose();
        member?.Dispose();
    }

    private async Task LeaderLoopAsync(CancellationToken token)
    {
        stateSince = Stopwatch.GetTimestamp();
        var nextDwell = Dwell();
        while (!token.IsCancellationRequested)
        {
            int packets;
            if (Stopwatch.GetElapsedTime(stateSince) >= nextDwell)
            {
                packets = await AdvanceAsync(token);
                stateSince = Stopwatch.GetTimestamp();
                nextDwell = Dwell();
            }
            else
            {
                if (CurrentState == State.Dungeon || Random.Next(2) == 0) await LightFenceAsync(leader!, token);
                else await LightMoveAsync(leader!, token);
                packets = 1;
            }

            await Primary.PaceAsync(packets, token);
        }
    }

    private async Task MemberLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await memberGate.WaitAsync(token);
            try
            {
                if (moving || CurrentState == State.Dungeon || memberRandom.Next(2) == 0) await LightFenceAsync(member!, token);
                else await LightMoveAsync(member!, token, memberRandom);
            }
            finally
            {
                memberGate.Release();
            }

            await memberSchedule.PaceAsync(1, token);
        }
    }

    /// <summary>Stops member position reports, and waits out one already in flight, before the party moves.</summary>
    private async Task FreezeMemberMovesAsync(CancellationToken token)
    {
        moving = true;
        await memberGate.WaitAsync(token);
        memberGate.Release();
    }

    private TimeSpan Dwell() => dwell + TimeSpan.FromMilliseconds(Random.Next(0, (int)Math.Max(1, dwell.TotalMilliseconds / 2)));

    /// <summary>Runs <paramref name="action"/> on the member's connection, charging its packets to the member's schedule.</summary>
    private async Task<T> AsMemberAsync<T>(int packets, Func<Task<T>> action, CancellationToken token)
    {
        await memberGate.WaitAsync(token);
        try
        {
            return await action();
        }
        finally
        {
            memberGate.Release();
            memberSchedule.Charge(packets);
        }
    }

    /// <summary>
    /// One pair step: the requester's request with a step fence, then the companion's fence. Returns the
    /// packets the leader's connection sent; the member's are charged to its own schedule.
    /// </summary>
    private async Task<(RequestOutcome Outcome, int LeaderPackets)> StepAsync(
        WireConnection requester,
        WireConnection companion,
        ushort type,
        byte[] body,
        Func<Frame, bool>? signature,
        string scenario,
        CancellationToken stop,
        Func<Frame, bool>? companionExpects = null)
    {
        var start = companion.PayloadBytes;
        var expected = companionExpects is null ? null : companion.WaitFor(companionExpects);
        RequestOutcome outcome;
        int leaderPackets;
        if (ReferenceEquals(requester, member))
        {
            outcome = await AsMemberAsync(2, () => RequestAsync(requester, type, body, signature, true, scenario, stop, takesSlot: false), stop);
            await CompanionFenceAsync(companion, start, outcome.SendTimestamp, Protocol.Name(type), scenario, stop);
            leaderPackets = 1;
        }
        else
        {
            outcome = await RequestAsync(requester, type, body, signature, true, scenario, stop);
            await AsMemberAsync(1, async () =>
            {
                await CompanionFenceAsync(companion, start, outcome.SendTimestamp, Protocol.Name(type), scenario, stop);
                return 0;
            }, stop);
            leaderPackets = 2;
        }

        if (expected is not null && !expected.IsCompletedSuccessfully)
            throw new InvalidOperationException($"{companion.Label}: did not receive the expected frame for {Protocol.Name(type)}.");
        return (outcome, leaderPackets);
    }

    private async Task<int> AdvanceAsync(CancellationToken stop)
    {
        var (l, m) = (leader!, member!);
        var packets = 0;
        switch (CurrentState)
        {
            case State.Town:
                packets += (await StepAsync(l, m, Protocol.RequestPeer, Protocol.RequestPeerBody(m.WireId), null, "party", stop, Protocol.Noti(0x0007))).LeaderPackets;
                packets += (await StepAsync(m, l, Protocol.ResponsePeer, Protocol.ResponsePeerBody(l.WireId), Protocol.Noti(0x0009), "party", stop, Protocol.Noti(0x0008))).LeaderPackets;
                CurrentState = State.Party;
                return packets;
            case State.Party:
                await FreezeMemberMovesAsync(stop);
                packets += (await StepAsync(l, m, Protocol.EnterSelectDungeon, Protocol.EnterSelectDungeonBody(),
                    Protocol.DungeonFirst(Protocol.EnterSelectDungeon), "party dungeon entry", stop)).LeaderPackets;
                var startMap = l.WaitFor(Protocol.Noti(0x001D));
                var entry = await StepAsync(l, m, Protocol.SelectDungeon, Protocol.SelectDungeonBody(Protocol.PartyDungeonId),
                    Protocol.DungeonFirst(Protocol.SelectDungeon), "party dungeon entry", stop, Protocol.Noti(0x001D));
                packets += entry.LeaderPackets;
                if (entry.Outcome.FirstReply is { Selector: 0x01, Type: Protocol.SelectDungeon } || !startMap.IsCompletedSuccessfully)
                    throw new InvalidOperationException($"{l.Label}: SELECT_DUNGEON refused: {Convert.ToHexString(entry.Outcome.FirstReply?.Body ?? [])}");
                CurrentState = State.Dungeon;
                moving = false;
                packets += (await StepAsync(l, m, Protocol.FinishLoading, Protocol.FinishLoadingBody(), Protocol.Noti(0x001E), "party dungeon entry", stop)).LeaderPackets;
                packets += (await StepAsync(m, l, Protocol.FinishLoading, Protocol.FinishLoadingBody(), Protocol.Noti(0x001E), "party dungeon entry", stop)).LeaderPackets;
                Entries++;
                return packets;
            case State.Dungeon:
                await FreezeMemberMovesAsync(stop);
                packets += (await StepAsync(l, m, Protocol.Eplp, Protocol.LeaveDungeonBody(), Protocol.Cmd(Protocol.Eplp), "leave dungeon", stop)).LeaderPackets;
                CurrentState = State.PartyAfterRun;
                moving = false;
                return packets;
            default:
                packets += (await StepAsync(m, l, Protocol.LeaveParty, [], Protocol.Noti(0x0009), "leave party", stop)).LeaderPackets;
                CurrentState = State.Town;
                return packets;
        }
    }
}

/// <summary>A logged-in player at the town gate that idles (a fence every 2 s) until asked to flood.</summary>
internal sealed class FloodAgent(LoadContext context, int seed) : Agent(context, "flood", seed)
{
    private WireConnection? connection;
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Ready => ready.Task;

    public double AchievedPerSecond { get; private set; }

    public long Answered { get; private set; }

    public override async Task RunAsync(CancellationToken stop)
    {
        try
        {
            connection = await EnterWorldAsync(Name, $"{Context.RunTag}-flood", $"F{Context.RunTag}00", stop);
            Group = Groups.Idle;
            ready.TrySetResult();
            while (!stop.IsCancellationRequested)
            {
                await Task.Delay(2000, stop);
                if (!flooding) await LightFenceAsync(connection, stop);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            Broken = true;
            ready.TrySetException(error);
            Context.Recorder.Error(Name, error.Message);
        }
    }

    private volatile bool flooding;

    /// <summary>Keeps <paramref name="window"/> fences in flight for <paramref name="duration"/>.</summary>
    public async Task FloodAsync(TimeSpan duration, int window, CancellationToken stop)
    {
        await Ready;
        flooding = true;
        var connection = this.connection!;
        var inFlight = new Queue<(long Sent, Task<Frame> Answer)>();
        var started = Stopwatch.GetTimestamp();
        long answered = 0;
        try
        {
            while (Stopwatch.GetElapsedTime(started) < duration && !stop.IsCancellationRequested)
            {
                while (inFlight.Count < window) inFlight.Enqueue(connection.SendFenceNoWait());
                var (sent, answer) = inFlight.Dequeue();
                var frame = await answer.WaitAsync(WireConnection.RequestTimeout, stop);
                answered++;
                Context.Sent(Protocol.Name(Protocol.Fence), Groups.Flood, sent);
                Context.Recorder.Add(new Sample(Protocol.Name(Protocol.Fence), Groups.Flood, connection.Label, sent, frame.Timestamp, frame.Timestamp, 0, false));
            }

            while (inFlight.Count > 0)
            {
                var (sent, answer) = inFlight.Dequeue();
                var frame = await answer.WaitAsync(WireConnection.RequestTimeout, stop);
                Context.Sent(Protocol.Name(Protocol.Fence), Groups.Flood, sent);
                Context.Recorder.Add(new Sample(Protocol.Name(Protocol.Fence), Groups.Flood, connection.Label, sent, frame.Timestamp, frame.Timestamp, 0, false));
            }
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(started).TotalSeconds;
            Answered = answered;
            AchievedPerSecond = answered / elapsed;
            flooding = false;
        }
    }
}
