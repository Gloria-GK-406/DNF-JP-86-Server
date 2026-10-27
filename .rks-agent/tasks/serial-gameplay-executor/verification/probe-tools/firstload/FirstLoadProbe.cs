using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace Dfo.Server.Abstractions.Execution;

// TEMPORARY TASK-15 probe. Never committed: records PvfArchiveProvider.Archive reads made ON the gameplay
// executor (the opposite of GameplayProbe), i.e. content loads that happen inside a job.
public static class FirstLoadProbe
{
    private static readonly string? Directory = Environment.GetEnvironmentVariable("FIRSTLOAD_PROBE_OUT");
    private static readonly ConcurrentDictionary<(Guid Job, MethodBase Caller), Entry> Entries = new();
    private static readonly ConcurrentDictionary<string, int> Seen = new();
    private static readonly object AppendSync = new();
    private static readonly object FlushSync = new();
    private static readonly long Origin = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
    private static readonly Timer? Flusher;

    private sealed class Entry(string line)
    {
        public readonly string Line = line;
        public int Hits = 1;
    }

    static FirstLoadProbe()
    {
        if (Directory is null) return;
        System.IO.Directory.CreateDirectory(Directory);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush();
        Flusher = new Timer(_ => Flush(), null, 5_000, 5_000);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Hit()
    {
        if (Directory is null || !GameplayJob.IsOnExecutor) return;
        var chain = GameplayJob.Current!.Value;
        var caller = CallerOf(new StackFrame(1, false));
        if (caller is null) return;
        var key = (chain.Id, caller);
        if (Entries.TryGetValue(key, out var known))
        {
            Interlocked.Increment(ref known.Hits);
            return;
        }
        var trace = new StackTrace(1, false);
        string? content = null, application = null;
        var frames = new StringBuilder();
        var taken = 0;
        foreach (var frame in trace.GetFrames())
        {
            var method = frame.GetMethod();
            var type = method?.DeclaringType;
            if (method is null || type is null) continue;
            var name = type.FullName + "::" + method.Name;
            var ns = type.Namespace ?? "";
            if (content is null && ns.StartsWith("Dfo.Server.Infrastructure.Content", StringComparison.Ordinal)
                && type.Name != "PvfArchiveProvider") content = name;
            if (application is null && ns.StartsWith("Dfo.Server.Application", StringComparison.Ordinal)) application = name;
            if (taken++ < 80) frames.Append("\n  ").Append(type.Assembly.GetName().Name).Append('!').Append(name);
        }
        content ??= "(none)";
        var nth = Seen.AddOrUpdate(content, 1, static (_, n) => n + 1);
        var at = (DateTime.UtcNow.Ticks - Origin) / TimeSpan.TicksPerMillisecond;
        var line = string.Join('\t', at, chain.CommandType ?? "(null)", chain.Id, content, application ?? "(none)", nth,
            caller.DeclaringType?.FullName + "::" + caller.Name);
        if (!Entries.TryAdd(key, new Entry(line)))
        {
            Interlocked.Increment(ref Entries[key].Hits);
            return;
        }
        lock (AppendSync)
            File.AppendAllText(Path.Combine(Directory, Environment.ProcessId + ".records.txt"), "=== " + line + frames + "\n\n");
    }

    // Frame 1 is the Archive getter unless the JIT inlined it into its caller; either way the key is the
    // first frame outside the provider. StackFrame(n) walks only n frames, so repeated hits stay cheap.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static MethodBase? CallerOf(StackFrame first)
    {
        var method = first.GetMethod();
        if (method?.DeclaringType?.Name == "PvfArchiveProvider") method = new StackFrame(3, false).GetMethod();
        return method;
    }

    private static void Flush()
    {
        if (Directory is null) return;
        lock (FlushSync)
        {
            var output = new StringBuilder();
            foreach (var (_, entry) in Entries.ToArray())
                output.Append(Volatile.Read(ref entry.Hits)).Append('\t').Append(entry.Line).Append('\n');
            var path = Path.Combine(Directory, Environment.ProcessId + ".counts.tsv");
            File.WriteAllText(path + ".tmp", output.ToString());
            File.Move(path + ".tmp", path, overwrite: true);
        }
    }
}
