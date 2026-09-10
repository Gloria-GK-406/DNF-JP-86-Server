using System.Text.Json;
using Dfo.Launcher.Preferences;

namespace Dfo.Launcher.Tests.Preferences;

public sealed class LauncherPreferencesStoreTests
{
    [Fact]
    public async Task Remembered_preferences_contain_only_login_id_and_remember()
    {
        var directory = CreateDirectory();
        try
        {
            var path = Path.Combine(directory, "preferences.json");
            var store = new JsonLauncherPreferencesStore(path);
            await store.SaveAsync(new LauncherPreferences("player", true), CancellationToken.None);

            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            Assert.Equal(["loginId", "remember"], json.RootElement.EnumerateObject()
                .Select(property => property.Name).Order().ToArray());
            Assert.Equal("player", json.RootElement.GetProperty("loginId").GetString());
            Assert.True(json.RootElement.GetProperty("remember").GetBoolean());
            Assert.DoesNotContain("password", await File.ReadAllTextAsync(path), StringComparison.OrdinalIgnoreCase);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Replacing_preferences_is_atomic_and_forgetting_removes_the_login_id()
    {
        var directory = CreateDirectory();
        try
        {
            var path = Path.Combine(directory, "preferences.json");
            var store = new JsonLauncherPreferencesStore(path);
            await store.SaveAsync(new LauncherPreferences("first", true), CancellationToken.None);
            await store.SaveAsync(new LauncherPreferences("must-not-remain", false), CancellationToken.None);

            Assert.Equal(LauncherPreferences.Empty, await store.LoadAsync(CancellationToken.None));
            var text = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain("must-not-remain", text, StringComparison.Ordinal);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Missing_or_malformed_preferences_degrade_to_empty()
    {
        var directory = CreateDirectory();
        try
        {
            var path = Path.Combine(directory, "preferences.json");
            var store = new JsonLauncherPreferencesStore(path);
            Assert.Equal(LauncherPreferences.Empty, await store.LoadAsync(CancellationToken.None));
            await File.WriteAllTextAsync(path, "{not json");
            Assert.Equal(LauncherPreferences.Empty, await store.LoadAsync(CancellationToken.None));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static string CreateDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-launcher-pref-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
