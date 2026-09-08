using System.Diagnostics;
using System.Text;

namespace CrapMeter.Runtime;

public sealed record ProcessResult(
    string Command,
    int ExitCode,
    TimeSpan Duration,
    string LogPath)
{
    public string StandardOutput { get; init; } = "";
}

/// <summary>
/// 跑外部命令并把完整输出留到日志文件里。日志是运行目录的原始证据之一——
/// 报告里关于测试是否真的跑过的说法，最终要能回到这里。
/// </summary>
public static class ProcessRunner
{
    public static ProcessResult Run(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string logPath,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var info = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach ((string name, string value) in environment)
            {
                info.Environment[name] = value;
            }
        }

        string command = fileName + " " + string.Join(
            ' ', arguments.Select(a => a.Contains(' ') ? "\"" + a + "\"" : a));

        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        using var log = new StreamWriter(logPath, append: false, Encoding.UTF8);
        log.WriteLine("$ " + command);
        log.WriteLine("(cwd: " + workingDirectory + ")");
        log.WriteLine();

        var stopwatch = Stopwatch.StartNew();
        using Process process = Process.Start(info)
            ?? throw new InvalidOperationException("无法启动进程：" + command);

        var sync = new Lock();
        var stdout = new StringBuilder();
        void Write(string? line, bool isOutput)
        {
            if (line is null)
            {
                return;
            }

            lock (sync)
            {
                log.WriteLine(line);
                log.Flush();
                if (isOutput) stdout.AppendLine(line);
            }

            Console.WriteLine("  | " + line);
        }

        process.OutputDataReceived += (_, e) => Write(e.Data, true);
        process.ErrorDataReceived += (_, e) => Write(e.Data, false);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();
        stopwatch.Stop();

        lock (sync)
        {
            log.WriteLine();
            log.WriteLine("(exit code: " + process.ExitCode + ")");
            log.Flush();
        }

        return new ProcessResult(command, process.ExitCode, stopwatch.Elapsed, logPath)
            { StandardOutput = fileName == "dotnet" && arguments.SequenceEqual(["--version"]) ? stdout.ToString().Trim() : "" };
    }
}
