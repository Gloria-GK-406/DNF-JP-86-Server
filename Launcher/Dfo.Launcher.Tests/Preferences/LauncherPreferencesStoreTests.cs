using System.Text.Json;
using Dfo.Launcher.Preferences;

namespace Dfo.Launcher.Tests.Preferences;

public sealed class LauncherPreferencesStoreTests
{
    [Fact]
    public async Task Remembered_preferences_contain_login_id_password_and_remember()
    {
        var directory = CreateDirectory();
        try
        {
            var path = Path.Combine(directory, "preferences.json");
            var store = new JsonLauncherPreferencesStore(path);
            await store.SaveAsync(
                new LauncherPreferences("player", "password-secret", true),
                CancellationToken.None);

            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            Assert.Equal(["loginId", "password", "remember"], json.RootElement.EnumerateObject()
                .Select(property => property.Name).Order().ToArray());
            Assert.Equal("player", json.RootElement.GetProperty("loginId").GetString());
            Assert.Equal("password-secret", json.RootElement.GetProperty("password").GetString());
            Assert.True(json.RootElement.GetProperty("remember").GetBoolean());
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
            await store.SaveAsync(
                new LauncherPreferences("first", "first-password", true),
                CancellationToken.None);
            await store.SaveAsync(
                new LauncherPreferences("must-not-remain", "must-not-remain-password", false),
                CancellationToken.None);

            Assert.Equal(LauncherPreferences.Empty, await store.LoadAsync(CancellationToken.None));
            var text = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain("must-not-remain", text, StringComparison.Ordinal);
            Assert.DoesNotContain("first-password", text, StringComparison.Ordinal);
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
