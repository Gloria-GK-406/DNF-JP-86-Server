using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Dfo.Server.Host.Tests;

public sealed class GmMailBrowserFixtureTests
{
    [Fact]
    public async Task ExplicitMailBrowserFixture()
    {
        if (Environment.GetEnvironmentVariable("DNF_GM_MAIL_BROWSER_FIXTURE") != "1") return;
        var workspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));
        var allowed = Path.GetFullPath(Path.Combine(workspace, "artifacts/gm-mail-browser/spec004"));
        var directory = Path.GetFullPath(Environment.GetEnvironmentVariable("DNF_GM_MAIL_BROWSER_FIXTURE_DIRECTORY") ?? allowed);
        if (!string.Equals(directory, allowed, StringComparison.OrdinalIgnoreCase) && !directory.StartsWith(allowed + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Browser fixture must remain within its isolated artifact directory.");
        if (File.Exists(Path.Combine(directory, "test.db"))) throw new InvalidOperationException("Use a fresh fixture database; this test never resets a database.");
        Directory.CreateDirectory(directory);
        await using var host = await GmTestHost.StartAsync(directory: directory, listenerPort: 18083);
        await GmPlayerDirectoryHttpTests.SeedAsync(host);
        await host.ExecuteAsync("INSERT INTO character_stats(character_id,carried_gold) VALUES(1,0),(2,0),(3,0);");
        using var page = await host.Client.GetAsync("/gm/"); page.EnsureSuccessStatusCode();
        Assert.Contains("<div id=\"root\"></div>", await page.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await host.LoginAsync(await host.CsrfAsync())).StatusCode);
        var csrf = await host.CsrfAsync();
        var input = new { title = "官方补偿", body = "隔离后台邮件验证，请在邮箱领取。", recipientIds = new[] { 1 }, items = new[] { new { itemId = 3037, count = 5 } }, gold = 1000, expectedRevision = 0, name = "测试补偿" };
        using var preview = await host.PostAsync("/api/gm/v1/mail/preview", csrf, input); Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.True((await preview.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("utf8Bytes").GetInt32() > 0);
        Assert.Equal(HttpStatusCode.OK, (await host.PostAsync("/api/gm/v1/mail/templates", csrf, input)).StatusCode);
        Assert.Equal(new[] { "0" }, await host.ReadAsync("SELECT COUNT(*) FROM mailbox_messages;"));
        await File.WriteAllTextAsync(Path.Combine(directory, "ready.txt"), "http://127.0.0.1:18083/gm/mail\n");
        Console.WriteLine("GM_MAIL_BROWSER_FIXTURE_URL=http://127.0.0.1:18083/gm/mail");
        Console.WriteLine("GM_MAIL_BROWSER_FIXTURE_LOGIN=operator; PASSWORD=" + GmTestHost.Password);
        Console.WriteLine("Fresh isolated SQLite; 45 accounts/47 characters; recipients 1/2/3 usable. Permanent items 27312 equipment, 3 consumable, 3037 account material. Template 测试补偿, no mail sent. Game network disabled. Stop using stop.txt.");
        if (Environment.GetEnvironmentVariable("DNF_GM_MAIL_BROWSER_FIXTURE_VERIFY_ONLY") == "1") return;
        var deadline = DateTimeOffset.UtcNow.AddMinutes(15);
        while (DateTimeOffset.UtcNow < deadline && !File.Exists(Path.Combine(directory, "stop.txt"))) await Task.Delay(500);
    }
}
