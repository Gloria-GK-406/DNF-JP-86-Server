using System.Globalization;

namespace CrapMeter.Runtime;

/// <summary>
/// 仓库根 <c>.crap/</c> 下的一次运行目录。
///
/// 名字取本地时间的 <c>YYYY-MM-DD_HH-mm-ss</c>：字典序即时间序；不含冒号（Windows 拒绝）、
/// 不含空格（省得每条命令都要加引号）。同秒冲突加 <c>-2</c>、<c>-3</c> 后缀，
/// 绝不覆盖或复用既有目录。
/// </summary>
public sealed class RunDirectory
{
    public const string TimestampFormat = "yyyy-MM-dd_HH-mm-ss";

    private RunDirectory(string path, string name, DateTimeOffset startedAt)
    {
        Path = path;
        Name = name;
        StartedAt = startedAt;
    }

    public string Path { get; }

    public string Name { get; }

    public DateTimeOffset StartedAt { get; }

    public string RawDirectory => System.IO.Path.Combine(Path, "raw");

    public string LogDirectory => System.IO.Path.Combine(Path, "logs");

    public string File(string relative) => System.IO.Path.Combine(Path, relative);

    public static RunDirectory Create(string crapRoot, DateTimeOffset now)
    {
        Directory.CreateDirectory(crapRoot);
        using FileStream allocationLock = AcquireLock(System.IO.Path.Combine(crapRoot, ".allocation.lock"));
        string stamp = now.ToString(TimestampFormat, CultureInfo.InvariantCulture);

        for (int suffix = 1; suffix < 1000; suffix++)
        {
            string name = suffix == 1 ? stamp : stamp + "-" + suffix.ToString(CultureInfo.InvariantCulture);
            string path = System.IO.Path.Combine(crapRoot, name);
            if (Directory.Exists(path) || System.IO.File.Exists(path))
            {
                continue;
            }

            Directory.CreateDirectory(path);
            Directory.CreateDirectory(System.IO.Path.Combine(path, "raw"));
            Directory.CreateDirectory(System.IO.Path.Combine(path, "logs"));
            return new RunDirectory(path, name, now);
        }

        throw new InvalidOperationException(
            "同一秒内已有 1000 个运行目录，拒绝继续：" + crapRoot);
    }

    private static FileStream AcquireLock(string path)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (timer.Elapsed < TimeSpan.FromSeconds(10)) { Thread.Sleep(10); }
        }
    }
}
