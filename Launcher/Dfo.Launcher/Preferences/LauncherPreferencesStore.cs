using System.IO;
using System.Text.Json;

namespace Dfo.Launcher.Preferences;

public sealed record LauncherPreferences(string? LoginId, string? Password, bool Remember)
{
    public static LauncherPreferences Empty { get; } = new(null, null, false);
}

public interface ILauncherPreferencesStore
{
    ValueTask<LauncherPreferences> LoadAsync(CancellationToken cancellationToken);

    ValueTask SaveAsync(LauncherPreferences preferences, CancellationToken cancellationToken);
}

public sealed class JsonLauncherPreferencesStore(string path) : ILauncherPreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DfoLauncher",
        "preferences.json");

    public async ValueTask<LauncherPreferences> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            var value = await JsonSerializer.DeserializeAsync<LauncherPreferences>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            return value is { Remember: true, LoginId: not null }
                ? value
                : LauncherPreferences.Empty;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return LauncherPreferences.Empty;
        }
    }

    public async ValueTask SaveAsync(
        LauncherPreferences preferences,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var safe = preferences.Remember
            ? new LauncherPreferences(preferences.LoginId, preferences.Password, true)
            : LauncherPreferences.Empty;
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Preferences path must have a directory.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    safe,
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                File.Replace(temporary, path, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporary, path);
            }
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
