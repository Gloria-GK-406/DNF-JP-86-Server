using System.Globalization;

namespace CrapMeter.Runtime;

public static class RerunScript
{
    public static string Create(string repositoryRoot, string pvfArchive, string configuration, int top, string? simulateFailure)
    {
        List<string> arguments = ["run", "--project", Path.Combine(repositoryRoot, "Tool", "CrapMeter", "CrapMeter.csproj"),
            "-c", "Release", "--", "--repo", repositoryRoot, "--pvf-archive", pvfArchive,
            "--configuration", configuration, "--top", top.ToString(CultureInfo.InvariantCulture)];
        if (simulateFailure is not null) arguments.AddRange(["--simulate-failure", simulateFailure]);
        return "$ErrorActionPreference = 'Stop'\n"
            + "Set-Location -LiteralPath " + Quote(repositoryRoot) + "\n"
            + "$env:PVF_ARCHIVE_PATH = " + Quote(pvfArchive) + "\n"
            + "& dotnet " + string.Join(' ', arguments.Select(Quote)) + "\nexit $LASTEXITCODE\n";
    }

    private static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}
