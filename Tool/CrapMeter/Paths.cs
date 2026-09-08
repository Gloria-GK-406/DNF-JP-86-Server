namespace CrapMeter;

/// <summary>
/// 路径规范化。覆盖率报告写的是构建机上的绝对 Windows 路径，清册写的是仓库相对路径；
/// 两端必须先化到同一形状才能对接。
/// </summary>
public static class Paths
{
    public static string Normalize(string path) => path.Replace('\\', '/');

    /// <summary>化成以仓库根为基准、正斜杠分隔的相对路径。</summary>
    public static string Relative(string repositoryRoot, string path) =>
        Normalize(Path.GetRelativePath(repositoryRoot, path));

    /// <summary>
    /// 覆盖率报告里的绝对路径 → 仓库相对路径。落在仓库外的返回 null，由调用方计数，
    /// 不静默丢弃。
    /// </summary>
    public static string? TryRelative(string repositoryRoot, string absolutePath)
    {
        string root = Normalize(Path.GetFullPath(repositoryRoot)).TrimEnd('/') + "/";
        string full = Normalize(absolutePath);
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? full[root.Length..]
            : null;
    }
}
