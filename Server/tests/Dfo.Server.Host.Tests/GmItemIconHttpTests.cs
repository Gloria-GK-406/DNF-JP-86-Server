using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Dfo.Server.Host.Tests;

public sealed class GmItemIconHttpTests
{
    private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10];

    private static void WritePack(string path, byte marker, bool manifest = true)
    {
        var temp = path + ".new";
        using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            using (var icon = zip.CreateEntry("icons/27312.png").Open()) { icon.Write(Signature); icon.WriteByte(marker); }
            using (var huge = zip.CreateEntry("icons/3037.png").Open()) huge.Write(new byte[64 * 1024 + 1]);
            if (manifest) using (var text = zip.CreateEntry("manifest.json").Open())
                text.Write(Encoding.UTF8.GetBytes("""{"generatedAt":"2026-10-16T00:00:00Z","pvfSha256":"ab12","count":2}"""));
        }
        // The operator procedure: remove the old pack (shared for delete), then move the new one in place.
        if (File.Exists(path)) File.Delete(path);
        File.Move(temp, path);
    }

    [Fact]
    public async Task IconsAreServedFromTheZipWithPrivateCachingAndMissingOnesAre404()
    {
        var directory = GmTestHost.NewDirectory(); var pack = Path.Combine(directory, "gm-icons.zip");
        await using var host = await GmTestHost.StartAsync(directory: directory, permissions: "QueryPlayers",
            configuration: new Dictionary<string, string?> { ["GmWeb:IconPack"] = pack });
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/gm/v1/items/27312/icon")).StatusCode);
        using var login = await host.LoginAsync(await host.CsrfAsync());

        var absent = await host.Client.GetFromJsonAsync<JsonElement>("/api/gm/v1/items/icon-pack");
        Assert.False(absent.GetProperty("available").GetBoolean()); Assert.Equal(JsonValueKind.Null, absent.GetProperty("count").ValueKind);
        using (var missing = await host.Client.GetAsync("/api/gm/v1/items/27312/icon"))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.False(string.IsNullOrEmpty((await missing.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString()));
        }

        WritePack(pack, 1);
        var info = await host.Client.GetFromJsonAsync<JsonElement>("/api/gm/v1/items/icon-pack");
        Assert.True(info.GetProperty("available").GetBoolean()); Assert.Equal(2, info.GetProperty("count").GetInt32());
        Assert.Equal("2026-10-16T00:00:00Z", info.GetProperty("generatedAt").GetString()); Assert.Equal("ab12", info.GetProperty("pvfSha256").GetString());
        using (var icon = await host.Client.GetAsync("/api/gm/v1/items/27312/icon"))
        {
            Assert.Equal(HttpStatusCode.OK, icon.StatusCode);
            Assert.Equal("image/png", icon.Content.Headers.ContentType!.MediaType);
            Assert.True(icon.Headers.CacheControl!.Private); Assert.Equal(TimeSpan.FromDays(1), icon.Headers.CacheControl.MaxAge);
            Assert.False(icon.Headers.CacheControl.NoStore);
            Assert.Equal([.. Signature, 1], await icon.Content.ReadAsByteArrayAsync());
        }
        foreach (var path in new[] { "/api/gm/v1/items/3037/icon", "/api/gm/v1/items/1/icon", "/api/gm/v1/items/0/icon" })
        {
            using var response = await host.Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); Assert.True(response.Headers.CacheControl!.NoStore);
        }

        // Replacing the pack while the server holds it is picked up on the next request.
        await Task.Delay(20); WritePack(pack, 2, manifest: false); File.SetLastWriteTimeUtc(pack, DateTime.UtcNow.AddMinutes(1));
        Assert.Equal([.. Signature, 2], await host.Client.GetByteArrayAsync("/api/gm/v1/items/27312/icon"));
        var bare = await host.Client.GetFromJsonAsync<JsonElement>("/api/gm/v1/items/icon-pack");
        Assert.True(bare.GetProperty("available").GetBoolean()); Assert.Equal(JsonValueKind.Null, bare.GetProperty("pvfSha256").ValueKind);
    }
}
