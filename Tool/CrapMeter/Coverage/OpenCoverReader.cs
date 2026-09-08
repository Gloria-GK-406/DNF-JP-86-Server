using System.Collections.Immutable;
using System.Globalization;
using System.Xml;

namespace CrapMeter.Coverage;

/// <summary>opencover 里的一个点（序列点或分支点），已化到仓库相对路径。</summary>
/// <param name="Key">跨报告合并用的身份：同一份程序集里唯一且稳定。</param>
public sealed record RawPoint(
    string Module,
    string Key,
    string? RelativePath,
    string? AbsolutePath,
    int Line,
    long VisitCount);

/// <summary>一个 IL 方法的自报复杂度，仅用于与源码级复杂度交叉核对。</summary>
public sealed record RawMethod(
    string Module,
    string Name,
    int IlComplexity,
    string? RelativePath,
    int FirstLine);

public sealed record OpenCoverReport(
    string SourcePath,
    ImmutableArray<RawPoint> SequencePoints,
    ImmutableArray<RawPoint> BranchPoints,
    ImmutableArray<RawMethod> Methods,
    ImmutableSortedSet<string> FilesOutsideRepository);

/// <summary>
/// 流式读 opencover XML。单份报告可达十几 MB，而且一次采集要读 7 份，
/// 所以不整份载进 DOM。
/// </summary>
public static class OpenCoverReader
{
    public static OpenCoverReport Read(string path, string repositoryRoot)
    {
        var sequencePoints = ImmutableArray.CreateBuilder<RawPoint>();
        var branchPoints = ImmutableArray.CreateBuilder<RawPoint>();
        var methods = ImmutableArray.CreateBuilder<RawMethod>();
        var outside = ImmutableSortedSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase);

        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        string module = string.Empty;
        string methodName = string.Empty;
        int methodComplexity = 0;
        bool methodRecorded = true;

        var settings = new XmlReaderSettings { IgnoreComments = true, IgnoreWhitespace = true };
        using XmlReader reader = XmlReader.Create(path, settings);

        (string? relative, string? absolute) ResolveFile(string? fileId)
        {
            if (fileId is null || !files.TryGetValue(fileId, out string? absolutePath))
            {
                return (null, null);
            }

            string? rel = Paths.TryRelative(repositoryRoot, absolutePath);
            if (rel is null)
            {
                outside.Add(absolutePath);
            }

            return (rel, absolutePath);
        }

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            switch (reader.Name)
            {
                case "Module":
                    // 刻意不读 hash 属性：那是插桩后程序集的 MVID，每个测试宿主各不相同，
                    // 不是快照身份。判快照一致性用序列点集合的指纹，见 CoverageMerge。
                    files.Clear();
                    module = string.Empty;
                    break;

                case "ModuleName":
                    module = reader.ReadElementContentAsString();
                    break;

                case "File":
                {
                    string? uid = reader.GetAttribute("uid");
                    string? fullPath = reader.GetAttribute("fullPath");
                    if (uid is not null && fullPath is not null)
                    {
                        files[uid] = fullPath;
                    }

                    break;
                }

                case "Method":
                    methodName = string.Empty;
                    methodComplexity = Int(reader.GetAttribute("cyclomaticComplexity"));
                    methodRecorded = false;
                    break;

                case "Name":
                    methodName = reader.ReadElementContentAsString();
                    break;

                case "SequencePoint":
                {
                    (string? rel, string? abs) = ResolveFile(reader.GetAttribute("fileid"));
                    int line = Int(reader.GetAttribute("sl"));
                    sequencePoints.Add(new RawPoint(
                        module,
                        methodName + "#sp" + reader.GetAttribute("ordinal"),
                        rel,
                        abs,
                        line,
                        Long(reader.GetAttribute("vc"))));

                    if (!methodRecorded)
                    {
                        methods.Add(new RawMethod(module, methodName, methodComplexity, rel, line));
                        methodRecorded = true;
                    }

                    break;
                }

                case "BranchPoint":
                {
                    (string? rel, string? abs) = ResolveFile(reader.GetAttribute("fileid"));
                    branchPoints.Add(new RawPoint(
                        module,
                        methodName + "#bp" + reader.GetAttribute("offset") + ":" + reader.GetAttribute("path"),
                        rel,
                        abs,
                        Int(reader.GetAttribute("sl")),
                        Long(reader.GetAttribute("vc"))));
                    break;
                }
            }
        }

        return new OpenCoverReport(
            path,
            sequencePoints.ToImmutable(),
            branchPoints.ToImmutable(),
            methods.ToImmutable(),
            outside.ToImmutable());
    }

    private static int Int(string? value) =>
        value is not null && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            ? result
            : 0;

    private static long Long(string? value) =>
        value is not null && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long result)
            ? result
            : 0;
}
