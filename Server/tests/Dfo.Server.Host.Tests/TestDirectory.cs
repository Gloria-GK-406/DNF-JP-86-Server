namespace Dfo.Server.Host.Tests;

internal static class TestDirectory
{
    public static async Task DeleteAsync(string directory)
    {
        var fullPath = Path.GetFullPath(directory);
        var relative = Path.GetRelativePath(Path.GetTempPath(), fullPath);
        if (relative == "." || relative == ".." || Path.IsPathRooted(relative)
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("Only an isolated test directory under the temporary root may be removed.", nameof(directory));

        // Windows can briefly retain SQLite WAL/SHM mappings after the child has exited.
        // Retry cleanup only; test assertions have already run and failures are not hidden.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (Directory.Exists(fullPath)) Directory.Delete(fullPath, recursive: true);
                return;
            }
            catch (Exception error) when (OperatingSystem.IsWindows() && attempt < 25
                && error is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(200);
            }
        }
    }
}
