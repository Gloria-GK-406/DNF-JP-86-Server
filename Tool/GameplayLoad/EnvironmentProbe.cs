using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace GameplayLoad;

/// <summary>Machine, build and SQLite facts for the record, read at run time rather than copied from configuration.</summary>
internal static class EnvironmentProbe
{
    public static string Describe(string hostDll, HostProcess host)
    {
        var text = new StringBuilder();
        text.AppendLine($"- 机器：{Run("sysctl", "-n machdep.cpu.brand_string")}，{Environment.ProcessorCount} 逻辑核，" +
                        $"内存 {long.Parse(Run("sysctl", "-n hw.memsize")) / (1024 * 1024 * 1024)} GiB，{RuntimeInformation.OSDescription}");
        text.AppendLine($"- 磁盘（数据库所在卷 `{Path.GetPathRoot(host.DatabasePath)}`，临时目录 `{Path.GetTempPath()}`）：{Disk(host.Directory)}");
        text.AppendLine($"- .NET 运行时（工具进程）：{RuntimeInformation.FrameworkDescription}");
        text.AppendLine($"- Host：`{hostDll}`，构建配置 {(hostDll.Contains("/Release/", StringComparison.Ordinal) ? "Release" : "非 Release")}，" +
                        $"Host.dll 修改时间 {File.GetLastWriteTimeUtc(hostDll):yyyy-MM-ddTHH:mm:ssZ}");
        text.AppendLine($"- Host 进程：`dotnet Dfo.Server.Host.dll`，工作站/服务器 GC 取 Host 自身默认（csproj 未设置），日志 Default=Information（appsettings.json），" +
                        "额外设置 `Logging__Console__FormatterOptions__SingleLine=true`");
        text.AppendLine(Sqlite(hostDll, host.DatabasePath));
        return text.ToString();
    }

    /// <summary>
    /// journal_mode is a property of the database file (WAL persists in the file header), so a separate
    /// read-only connection to the Host's live database reads the Host's actual value. synchronous is a
    /// per-connection setting: the server never issues PRAGMA synchronous (no occurrence in Server/src),
    /// so its connections run at the native library's compiled default. That default is read on a fresh
    /// connection opened by this tool with the same connection-string form (Data Source only) and the
    /// same native library — the two dylibs are compared by SHA-256 below.
    /// </summary>
    private static string Sqlite(string hostDll, string databasePath)
    {
        var text = new StringBuilder();
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        string Scalar(string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToString(command.ExecuteScalar()) ?? "";
        }

        var journal = Scalar("PRAGMA journal_mode;");
        var synchronous = Scalar("PRAGMA synchronous;");
        var version = Scalar("select sqlite_version();");
        var options = new List<string>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA compile_options;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var option = reader.GetString(0);
                if (option.Contains("SYNCHRONOUS", StringComparison.Ordinal) || option.Contains("WAL", StringComparison.Ordinal)
                    || option.StartsWith("THREADSAFE", StringComparison.Ordinal))
                    options.Add(option);
            }
        }

        var rid = RuntimeInformation.RuntimeIdentifier.StartsWith("osx-arm64", StringComparison.Ordinal) ? "osx-arm64" : RuntimeInformation.RuntimeIdentifier;
        var library = OperatingSystem.IsMacOS() ? "libe_sqlite3.dylib" : OperatingSystem.IsWindows() ? "e_sqlite3.dll" : "libe_sqlite3.so";
        var hostLibrary = Path.Combine(Path.GetDirectoryName(hostDll)!, "runtimes", rid, "native", library);
        var toolLibrary = Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native", library);
        text.AppendLine($"- SQLite：版本 {version}；`PRAGMA journal_mode` = **{journal}**（对 Host 运行中的数据库文件以只读连接读取，文件级属性）；" +
                        $"`PRAGMA synchronous` = **{synchronous}**（{SynchronousName(synchronous)}；连接级，取值见下）");
        text.AppendLine($"  - synchronous 的取法：服务端源码不设置 synchronous（`Server/src` 中检索 `synchronous` 无 PRAGMA 命中；唯一的 PRAGMA 写入是 " +
                        "`Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs` 的 `PRAGMA journal_mode=WAL;`），连接串只有 `Data Source`" +
                        "（`SqliteDatabase.cs:41` 等）。所以服务端每条连接取原生库的编译期默认值。上面的值来自本工具以同样形式的连接串、对同一数据库、" +
                        "先读 journal_mode（WAL 下 SQLite 按 WAL 默认调整）后读取的结果，用的是与 Host 相同的原生库：");
        text.AppendLine($"  - Host 原生库 `{hostLibrary}` SHA-256 {Hash(hostLibrary)}");
        text.AppendLine($"  - 工具原生库 `{toolLibrary}` SHA-256 {Hash(toolLibrary)}");
        text.AppendLine($"  - 相关编译选项：{(options.Count == 0 ? "（无 SYNCHRONOUS/WAL 相关项，即用 SQLite 默认）" : string.Join(", ", options))}");
        return text.ToString().TrimEnd();
    }

    private static string SynchronousName(string value) => value switch
    {
        "0" => "OFF",
        "1" => "NORMAL",
        "2" => "FULL",
        "3" => "EXTRA",
        _ => "?",
    };

    private static string Hash(string path) => File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))[..16] + "…" : "（文件不存在）";

    private static string Disk(string directory)
    {
        if (!OperatingSystem.IsMacOS()) return "（非 macOS，未取）";
        var df = Run("df", directory).Split('\n').LastOrDefault()?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        var info = Run("diskutil", "info " + df);
        string Field(string name) => info.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.StartsWith(name, StringComparison.Ordinal))?[name.Length..].Trim() ?? "?";
        var physical = Run("diskutil", "info /").Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.StartsWith("Solid State:", StringComparison.Ordinal));
        return $"设备 {df}，文件系统 {Field("File System Personality:")}，{Field("Device Location:")}，{Field("Protocol:")}，" +
               $"Solid State: {Field("Solid State:")}（`diskutil info /`：{physical ?? "?"}）";
    }

    public static string Run(string file, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(file, arguments) { RedirectStandardOutput = true, UseShellExecute = false })!;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return output.Trim();
        }
        catch (Exception error)
        {
            return "?" + error.Message;
        }
    }
}
