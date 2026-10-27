using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace Dfo.Server.Abstractions.Execution;

// TEMPORARY TASK-07 probe. Never committed: records hook hits made off the gameplay executor.
public static class GameplayProbe
{
    private static readonly string? Directory = Environment.GetEnvironmentVariable("GAMEPLAY_PROBE_OUT");
    private static readonly ConcurrentDictionary<string, int> Hits = new();
    private static readonly object FlushSync = new();
    private static readonly object AppendSync = new();
    private static readonly Timer? Flusher;

    static GameplayProbe()
    {
        if (Directory is null) return;
        System.IO.Directory.CreateDirectory(Directory);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush();
        Flusher = new Timer(_ => Flush(), null, 20_000, 20_000);
    }

    public static T Touch<T>(T target, string hook) where T : class
    {
        Hit(hook);
        return target;
    }

    public static void Hit(string hook)
    {
        if (Directory is null || GameplayJob.IsOnExecutor) return;
        var trace = new StackTrace(1, false);
        var text = new StringBuilder(hook);
        var taken = 0;
        foreach (var frame in trace.GetFrames())
        {
            var method = frame.GetMethod();
            if (method is null) continue;
            var type = method.DeclaringType;
            if (type == typeof(GameplayProbe)) continue;
            text.Append('\n').Append(type?.Assembly.GetName().Name).Append('!')
                .Append(type?.FullName).Append("::").Append(method.Name);
            if (++taken == 40) break;
        }
        var key = text.ToString();
        if (Hits.TryAdd(key, 1))
        {
            // A new record is appended at once, so it survives even if the process ends without ProcessExit.
            lock (AppendSync)
                File.AppendAllText(Path.Combine(Directory, Environment.ProcessId + ".new.txt"), "=== 1 " + key + "\n\n");
            return;
        }
        Hits.AddOrUpdate(key, 1, static (_, n) => n + 1);
    }

    private static void Flush()
    {
        if (Directory is null) return;
        lock (FlushSync)
        {
            var output = new StringBuilder();
            foreach (var (key, count) in Hits.ToArray())
                output.Append("=== ").Append(count).Append(' ').Append(key).Append("\n\n");
            var path = Path.Combine(Directory, Environment.ProcessId + ".txt");
            var temp = path + ".tmp";
            File.WriteAllText(temp, output.ToString());
            File.Move(temp, path, overwrite: true);
        }
    }
}
