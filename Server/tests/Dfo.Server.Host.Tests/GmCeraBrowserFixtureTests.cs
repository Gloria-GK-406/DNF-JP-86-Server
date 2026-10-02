using System.Net;
using System.Text;

namespace Dfo.Server.Host.Tests;

public sealed class GmCeraBrowserFixtureTests
{
    [Fact]
    public async Task ExplicitCeraBrowserFixture()
    {
        if(Environment.GetEnvironmentVariable("DNF_GM_CERA_BROWSER_FIXTURE")!="1")return;
        var workspace=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../.."));
        var allowed=Path.GetFullPath(Path.Combine(workspace,"artifacts/gm-cera-browser/spec005"));
        var directory=Path.GetFullPath(Environment.GetEnvironmentVariable("DNF_GM_CERA_BROWSER_FIXTURE_DIRECTORY")??allowed);
        if(!string.Equals(directory,allowed,StringComparison.OrdinalIgnoreCase)&&!directory.StartsWith(allowed+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Cera fixture must remain in isolated artifacts.");
        for(var check=new DirectoryInfo(directory);check is not null;check=check.Parent)
            if(check.Exists&&(check.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("No reparse fixture directories.");
        if(File.Exists(Path.Combine(directory,"test.db")))throw new InvalidOperationException("Use a fresh fixture directory; existing databases are never reset.");
        Directory.CreateDirectory(directory);
        await using var host=await GmTestHost.StartAsync(permissions:"GrantCera",directory:directory,listenerPort:18084);
        await GmCeraHttpTests.SeedAsync(host);
        var sql=new StringBuilder();for(var id=3;id<=25;id++)sql.Append($"INSERT INTO accounts(account_id,m_id,cera,token_cera,happy_token_cera) VALUES({id},'fixture{id:D2}',100,200,300);");
        sql.Append("INSERT INTO characters(character_id,account_id,name) VALUES(1,2,'角色一'),(2,2,'角色二');");await host.ExecuteAsync(sql.ToString());
        using var page=await host.Client.GetAsync("/gm/wallet");page.EnsureSuccessStatusCode();Assert.Contains("<div id=\"root\"></div>",await page.Content.ReadAsStringAsync());
        var csrf=await GmCeraHttpTests.Login(host);using var preview=await host.PostAsync("/api/gm/v1/cera/preview",csrf,new{accountIds=new[]{1,2},currency="Cera",amount=50,reason="隔离测试补偿"});Assert.Equal(HttpStatusCode.OK,preview.StatusCode);
        Assert.Equal(new[]{"0"},await host.ReadAsync("SELECT COUNT(*) FROM gm_cera_operations;"));
        await File.WriteAllTextAsync(Path.Combine(directory,"ready.txt"),"http://127.0.0.1:18084/gm/wallet\n");
        Console.WriteLine("GM_CERA_BROWSER_FIXTURE_URL=http://127.0.0.1:18084/gm/wallet");
        Console.WriteLine("GM_CERA_BROWSER_FIXTURE_LOGIN=operator; PASSWORD="+GmTestHost.Password);
        Console.WriteLine("Fresh isolated SQLite, GrantCera only; account1 zero-role, account2 two roles, 25 accounts; no grants sent; game network disabled. Stop using stop.txt.");
        if(Environment.GetEnvironmentVariable("DNF_GM_CERA_BROWSER_FIXTURE_VERIFY_ONLY")=="1")return;
        var deadline=DateTimeOffset.UtcNow.AddMinutes(15);
        while(DateTimeOffset.UtcNow<deadline&&!File.Exists(Path.Combine(directory,"stop.txt")))await Task.Delay(500);
    }
}
