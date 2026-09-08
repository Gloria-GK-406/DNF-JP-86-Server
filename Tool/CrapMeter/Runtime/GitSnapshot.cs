using System.Diagnostics;
using System.Text;
using System.Security.Cryptography;
using System.Text.Json;

namespace CrapMeter.Runtime;

/// <summary>
/// 采集快照身份。CRAP 的两个输入必须同属一次快照，所以采集前后各记一次，
/// 中途仓库被改动能被看见。
/// </summary>
public sealed record GitSnapshot(string Commit, string Branch, bool Dirty, List<string> DirtyPaths)
{
    public SortedDictionary<string, string?> Files { get; init; } = new(StringComparer.Ordinal);

    public bool SameContent(GitSnapshot other) => Commit == other.Commit
        && Files.Count == other.Files.Count
        && Files.All(pair => other.Files.TryGetValue(pair.Key, out string? hash) && pair.Value == hash);

    public void Save(string path) => File.WriteAllText(path,
        JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));

    public static GitSnapshot Read(string repositoryRoot)
    {
        string commit = Capture(repositoryRoot, "rev-parse", "HEAD").Trim();
        string branch = Capture(repositoryRoot, "rev-parse", "--abbrev-ref", "HEAD").Trim();
        List<string> dirty = [.. Capture(repositoryRoot, "status", "--porcelain")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

        var files = new SortedDictionary<string, string?>(StringComparer.Ordinal);
        foreach (string path in Capture(repositoryRoot, "ls-files", "--cached", "--others", "--exclude-standard", "-z")
            .Split('\0', StringSplitOptions.RemoveEmptyEntries).Where(InScope).Distinct(StringComparer.Ordinal))
        {
            string fullPath = Path.Combine(repositoryRoot, path);
            // A tracked deletion is part of the snapshot too.
            if (!File.Exists(fullPath)) { files.Add(path, null); continue; }
            using FileStream stream = File.OpenRead(fullPath);
            files.Add(path, Convert.ToHexString(SHA256.HashData(stream)));
        }

        return new GitSnapshot(
            commit,
            branch,
            dirty.Count > 0,
            dirty) { Files = files };
    }

    private static bool InScope(string path)
    {
        string[] parts = path.Split('/');
        if (parts.Any(p => p.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || p.Equals("obj", StringComparison.OrdinalIgnoreCase)
            || p.Equals(".crap", StringComparison.OrdinalIgnoreCase)
            || p.Equals(".rks-agent", StringComparison.OrdinalIgnoreCase)
            || p.Equals("Docs", StringComparison.OrdinalIgnoreCase)
            || p.Equals("Document", StringComparison.OrdinalIgnoreCase))
            || path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) return false;

        return path.StartsWith("Server/", StringComparison.Ordinal)
            || path.StartsWith("Tool/CrapMeter/", StringComparison.Ordinal)
            || path.StartsWith("Tool/CrapMeter.Tests/", StringComparison.Ordinal)
            || path == ".config/dotnet-tools.json"
            || (parts.Length == 1 || (parts.Length == 2 && parts[0] == "Tool"))
                && (path.EndsWith(".props", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".targets", StringComparison.OrdinalIgnoreCase)
                    || parts[^1].Equals("global.json", StringComparison.OrdinalIgnoreCase)
                    || parts[^1].Equals("NuGet.Config", StringComparison.OrdinalIgnoreCase));
    }

    private static string Capture(string workingDirectory, params string[] arguments)
    {
        var info = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
        };

        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(info)
            ?? throw new InvalidOperationException("无法启动 git。");
        Task<string> error = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} 失败 ({process.ExitCode}): {error.GetAwaiter().GetResult()}");
        return output;
    }
}
