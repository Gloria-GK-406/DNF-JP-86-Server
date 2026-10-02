using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Administration;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Application.Administration;
using Dfo.Server.Application.Commands;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Data.World;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Protocol.Game.CeraShopPurchase;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Host.Tests;

public sealed class GmCeraHttpTests
{
    internal static Task SeedAsync(GmTestHost host) => host.ExecuteAsync("INSERT INTO accounts(account_id,m_id,cera,token_cera,happy_token_cera,honor_exp) VALUES(1,'zero-role',10,20,30,77),(2,'second',100,200,300,88);");
    internal static object Payload(Guid id, int[]? accounts = null, string currency = "Cera", int amount = 50, string reason = "测试补偿") => new { requestId = id, accountIds = accounts ?? [1], currency, amount, reason };
    internal static async Task<string> Login(GmTestHost host, string name = "operator")
    {
        using var response = await host.LoginAsync(await host.CsrfAsync(), name);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await host.CsrfAsync();
    }

    [Fact]
    public async Task GrantOnlyAdministratorCreditsAccountWithoutCharacterAndReplaysOnce()
    {
        await using var host = await GmTestHost.StartAsync(permissions: "GrantCera");
        await SeedAsync(host); var csrf = await Login(host); var payload = Payload(Guid.NewGuid());
        using var first = await host.PostAsync("/api/gm/v1/cera/grant", csrf, payload);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var replay = await host.PostAsync("/api/gm/v1/cera/grant", csrf, payload);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.True((await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("replayed").GetBoolean());
        Assert.Equal(new[] { "60:20:30:77" }, await host.ReadAsync("SELECT cera||':'||token_cera||':'||happy_token_cera||':'||honor_exp FROM accounts WHERE account_id=1;"));
        Assert.Equal(new[] { "1" }, await host.ReadAsync("SELECT COUNT(*) FROM gm_cera_operations;"));
        Assert.Equal(new[] { "1" }, await host.ReadAsync("SELECT COUNT(*) FROM gm_audit WHERE action='GrantCera' AND outcome='Succeeded';"));
    }

    [Theory]
    [InlineData("Cera", "60:20:30")]
    [InlineData("TokenCera", "10:70:30")]
    [InlineData("HappyTokenCera", "10:20:80")]
    public async Task EachCurrencyPreviewHasNoWritesAndReceiptUsesTransactionCurrentBalance(string currency, string expected)
    {
        await using var host = await GmTestHost.StartAsync(permissions: "GrantCera"); await SeedAsync(host); var csrf = await Login(host);
        using var preview = await host.PostAsync("/api/gm/v1/cera/preview", csrf, Payload(Guid.Empty, [1], currency));
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var data = await preview.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(50, data.GetProperty("totalAmount").GetInt64());
        Assert.Equal(new[] { "10:20:30" }, await host.ReadAsync("SELECT cera||':'||token_cera||':'||happy_token_cera FROM accounts WHERE account_id=1;"));
        Assert.Equal(new[] { "0" }, await host.ReadAsync("SELECT COUNT(*) FROM gm_cera_operations;"));
        Assert.Equal(HttpStatusCode.OK, (await host.PostAsync("/api/gm/v1/cera/grant", csrf, Payload(Guid.NewGuid(), [1], currency))).StatusCode);
        Assert.Equal(new[] { expected }, await host.ReadAsync("SELECT cera||':'||token_cera||':'||happy_token_cera FROM accounts WHERE account_id=1;"));
        using var final = await host.PostAsync("/api/gm/v1/cera/grant", csrf, Payload(Guid.NewGuid(), [1], currency));
        var receipt = (await final.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("receipt");
        var key = char.ToLowerInvariant(currency[0]) + currency[1..];
        Assert.Equal(data.GetProperty("accounts")[0].GetProperty("after").GetProperty(key).GetInt32(), receipt.GetProperty("accounts")[0].GetProperty("before").GetProperty(key).GetInt32());
    }

    [Fact]
    public async Task ConcurrentDuplicateBatchSurvivesRestartAndReplaysAfterAccountDeletion()
    {
        var directory = GmTestHost.NewDirectory(); var id = Guid.NewGuid(); string original;
        await using (var host = await GmTestHost.StartAsync(directory: directory))
        {
            await SeedAsync(host); var csrf = await Login(host);
            var replies = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => host.PostAsync("/api/gm/v1/cera/grant", csrf, Payload(id, [2,1,2], reason: "  测试\r\n补偿  "))));
            Assert.All(replies, x => Assert.Equal(HttpStatusCode.OK, x.StatusCode));
            original = (await replies[0].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("receipt").GetRawText(); foreach(var r in replies) r.Dispose();
            Assert.Equal(new[] { "60", "150" }, await host.ReadAsync("SELECT cera FROM accounts ORDER BY account_id;"));
            foreach (var payload in new[] { Payload(id, [1], reason: "测试\n补偿"), Payload(id, [1,2], amount: 51, reason: "测试\n补偿"), Payload(id, [1,2], currency: "TokenCera", reason: "测试\n补偿"), Payload(id, [1,2], reason: "另一个原因") })
                Assert.Equal(HttpStatusCode.Conflict, (await host.PostAsync("/api/gm/v1/cera/grant", csrf, payload)).StatusCode);
            await host.ExecuteAsync("DELETE FROM accounts WHERE account_id=1;");
        }
        await using (var reopened = await GmTestHost.StartAsync(directory: directory))
        {
            var csrf = await Login(reopened);
            using var result = await reopened.PostAsync("/api/gm/v1/cera/grant", csrf, new {requestId=id.ToString("N").ToUpperInvariant(),accountIds=new[]{1,2},currency="Cera",amount=50,reason="测试\n补偿"});
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.Equal(original, (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("receipt").GetRawText());
            Assert.Equal(new[] { "1" }, await reopened.ReadAsync("SELECT COUNT(*) FROM gm_cera_operations;"));
            Assert.Equal(new[] { "1" }, await reopened.ReadAsync("SELECT COUNT(*) FROM gm_audit WHERE action='GrantCera' AND outcome='Succeeded';"));
        }
    }

    [Theory]
    [InlineData("CREATE TRIGGER fault BEFORE UPDATE OF cera ON accounts WHEN OLD.account_id=2 BEGIN SELECT RAISE(IGNORE); END;",409)]
    [InlineData("CREATE TRIGGER fault BEFORE INSERT ON gm_cera_operations BEGIN SELECT RAISE(ABORT,'receipt fault'); END;",500)]
    [InlineData("CREATE TRIGGER fault BEFORE INSERT ON gm_audit WHEN NEW.action='GrantCera' AND NEW.outcome='Succeeded' BEGIN SELECT RAISE(ABORT,'audit fault'); END;",500)]
    public async Task CasReceiptAndAuditFailuresRollBackAllAccounts(string sql, int status)
    {
        await using var host = await GmTestHost.StartAsync(); await SeedAsync(host); var csrf = await Login(host); await host.ExecuteAsync(sql);
        using var result = await host.PostAsync("/api/gm/v1/cera/grant", csrf, Payload(Guid.NewGuid(), [2,1]));
        Assert.Equal(status, (int)result.StatusCode);
        Assert.Equal("rolledBack", (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("outcome").GetString());
        Assert.Equal(new[]{"10","100"},await host.ReadAsync("SELECT cera FROM accounts ORDER BY account_id;"));
        Assert.Equal(new[]{"0"},await host.ReadAsync("SELECT COUNT(*) FROM gm_cera_operations;"));
        Assert.Equal(new[]{"RolledBack"},await host.ReadAsync("SELECT outcome FROM gm_audit WHERE action='GrantCera';"));
    }

    [Fact]
    public async Task OverlappingOrderedBatchesDoNotLoseCurrencyOrOtherFields()
    {
        await using var host = await GmTestHost.StartAsync(); await SeedAsync(host); var csrf = await Login(host);
        var results=await Task.WhenAll(Enumerable.Range(0,12).Select(i=>host.PostAsync("/api/gm/v1/cera/grant",csrf,Payload(Guid.NewGuid(),i%2==0?[2,1]:[1,2],amount:1))));
        Assert.All(results,r=>Assert.Equal(HttpStatusCode.OK,r.StatusCode)); foreach(var r in results)r.Dispose();
        Assert.Equal(new[]{"22:20:30:77","112:200:300:88"},await host.ReadAsync("SELECT cera||':'||token_cera||':'||happy_token_cera||':'||honor_exp FROM accounts ORDER BY account_id;"));
    }

    [Fact]
    public async Task BoundariesMissingFieldsUnknownTypesAndCorruptWalletNeverCredit()
    {
        await using var host = await GmTestHost.StartAsync(); await SeedAsync(host); var csrf=await Login(host);
        object[] bad=[Payload(Guid.Empty),Payload(Guid.NewGuid(),[1,3]),Payload(Guid.NewGuid(),[-1]),Payload(Guid.NewGuid(),[]),Payload(Guid.NewGuid(),Enumerable.Repeat(1,101).ToArray()),
            Payload(Guid.NewGuid(),amount:0),Payload(Guid.NewGuid(),amount:-1),Payload(Guid.NewGuid(),currency:"Gold"),Payload(Guid.NewGuid(),reason:" "),Payload(Guid.NewGuid(),reason:new string('中',257)),Payload(Guid.NewGuid(),reason:"坏\0字符"),
            new{requestId=Guid.NewGuid(),accountIds=new[]{1},currency="Cera",reason="没有amount"},
            new{requestId=Guid.NewGuid(),accountIds=new[]{1},currency="Cera",amount=1.5,reason="小数"},
            new{requestId=Guid.NewGuid(),accountIds=new[]{1},currency="Cera",amount="1",reason="字符串"},
            new{requestId=Guid.NewGuid(),accountIds=new[]{1},currency="Cera",amount=2147483648L,reason="越界"},
            new{requestId=Guid.NewGuid(),accountIds=new[]{1},currency="Cera",amount=1,reason="未知字段",administratorId=999}];
        foreach(var payload in bad)
        {
            using var rejected=await host.PostAsync("/api/gm/v1/cera/grant",csrf,payload);
            Assert.True(rejected.StatusCode==HttpStatusCode.BadRequest,$"Expected 400 for {JsonSerializer.Serialize(payload)}; received {(int)rejected.StatusCode}: {await rejected.Content.ReadAsStringAsync()}");
        }
        foreach(var value in new[]{-1L,2147483648L,long.MaxValue})
        {
            await host.ExecuteAsync($"UPDATE accounts SET token_cera={value} WHERE account_id=2;");
            using var result=await host.PostAsync("/api/gm/v1/cera/grant",csrf,Payload(Guid.NewGuid(),[1,2]));
            Assert.Equal(HttpStatusCode.BadRequest,result.StatusCode);Assert.Contains("钱包数据异常",await result.Content.ReadAsStringAsync());
        }
        Assert.Equal(new[]{"10"},await host.ReadAsync("SELECT cera FROM accounts WHERE account_id=1;"));
        Assert.Equal(new[]{"0"},await host.ReadAsync("SELECT COUNT(*) FROM gm_cera_operations;"));
        await host.ExecuteAsync("UPDATE accounts SET cera=0,token_cera=0,happy_token_cera=0;");
        Assert.Equal(HttpStatusCode.OK,(await host.PostAsync("/api/gm/v1/cera/grant",csrf,Payload(Guid.NewGuid(),amount:int.MaxValue,reason:new string('中',256)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await host.PostAsync("/api/gm/v1/cera/grant",csrf,Payload(Guid.NewGuid(),amount:1))).StatusCode);
        Assert.Equal(new[]{"2147483647"},await host.ReadAsync("SELECT cera FROM accounts WHERE account_id=1;"));
    }

    [Fact]
    public async Task NarrowSelectionPermissionAndPersonalReceiptIsolation()
    {
        await using var host=await GmTestHost.StartAsync(permissions:"GrantCera"); await SeedAsync(host);
        Assert.Equal(HttpStatusCode.Unauthorized,(await host.Client.GetAsync("/api/gm/v1/cera/accounts")).StatusCode);
        var csrf=await Login(host); var id=Guid.NewGuid();
        using var page=await host.Client.GetAsync("/api/gm/v1/cera/accounts?pageSize=1&q=zero");Assert.Equal(HttpStatusCode.OK,page.StatusCode);
        var row=(await page.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items")[0];
        Assert.Equal(new[]{"accountId","balances","loginName"},row.EnumerateObject().Select(x=>x.Name).Order().ToArray());
        foreach(var query in new[]{"page=0","pageSize=101","accountId=-1","page=1&page=2"})Assert.Equal(HttpStatusCode.BadRequest,(await host.Client.GetAsync("/api/gm/v1/cera/accounts?"+query)).StatusCode);
        Assert.Empty((await host.Client.GetFromJsonAsync<JsonElement>("/api/gm/v1/cera/accounts?q=%25")).GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden,(await host.PostAsync("/api/gm/v1/cera/grant",null,Payload(id))).StatusCode);
        host.Client.DefaultRequestHeaders.Add("Origin","https://foreign.example");
        Assert.Equal(HttpStatusCode.Forbidden,(await host.PostAsync("/api/gm/v1/cera/grant",csrf,Payload(id))).StatusCode);host.Client.DefaultRequestHeaders.Remove("Origin");
        Assert.Equal(HttpStatusCode.OK,(await host.PostAsync("/api/gm/v1/cera/grant",csrf,Payload(id))).StatusCode);
        await host.ExecuteAsync("INSERT INTO gm_administrators(login_name,password_record,enabled,permissions) SELECT 'other',password_record,1,4 FROM gm_administrators LIMIT 1;");
        csrf=await Login(host,"other");Assert.Equal(HttpStatusCode.NotFound,(await host.Client.GetAsync("/api/gm/v1/cera/operations/"+id)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await host.PostAsync("/api/gm/v1/cera/grant",csrf,Payload(id))).StatusCode);
        Assert.Equal(new[]{"110"},await host.ReadAsync("SELECT cera FROM accounts WHERE account_id=1;"));
        await host.ExecuteAsync("UPDATE gm_administrators SET permissions=2 WHERE login_name='other';");
        Assert.Equal(HttpStatusCode.Forbidden,(await host.Client.GetAsync("/api/gm/v1/cera/accounts")).StatusCode);
    }

    [Fact]
    public async Task CommittedGrantSurvivesReminderExceptionAndProjectionReadsCurrentWallet()
    {
        var reminder=new FailingReminder();
        await using var host=await GmTestHost.StartAsync(configureServices:s=>s.AddSingleton<IGmCeraReminderDomainService>(reminder));
        await SeedAsync(host);var csrf=await Login(host);var id=Guid.NewGuid();
        reminder.Check=async()=>Assert.Equal(new[]{"60"},await host.ReadAsync("SELECT cera FROM accounts WHERE account_id=1;"));
        using var granted=await host.PostAsync("/api/gm/v1/cera/grant",csrf,Payload(id));Assert.Equal(HttpStatusCode.OK,granted.StatusCode);
        Assert.Equal("unconfirmed",(await granted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reminder").GetString());
        await host.ExecuteAsync("UPDATE accounts SET cera=59,token_cera=22,happy_token_cera=33 WHERE account_id=1;");
        await host.ExecuteAsync("INSERT INTO characters(character_id,account_id,name) VALUES(1,1,'actual-role');");
        var sessions=host.Services.GetRequiredService<ISelectedSessionDomainService>();
        var session=SelectedSessionIdentity.Create(new(1),new(1),new("cera-test-session"),1,21011);sessions.Select(session);
        var handler=host.Services.GetRequiredService<INotificationHandler<AccountWalletChanged>>();
        var recipient=new NotificationRecipient(session.CharacterId,session.ConnectionId,session);
        var message=Assert.Single(await handler.ComposeAsync(recipient,new(new(1)),default));
        var registration=host.Services.GetServices<ServerMessageEncoderRegistration>().Single(x=>x.MessageType==message.GetType());
        var frame=Assert.Single(((IServerMessageEncoder)host.Services.GetRequiredService(registration.EncoderType)).Encode(message));
        Assert.Equal((ushort)0x0035,BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1,2)));var body=frame[15..];
        Assert.Equal(13,body.Length);Assert.Equal(1,body[0]);Assert.Equal(59,BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(1,4)));
        Assert.Equal(22,BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(5,4)));Assert.Equal(33,BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(9,4)));
        Assert.Empty(await handler.ComposeAsync(recipient,new(new(2)),default));
        sessions.Select(session with{ConnectionId=new("replacement"),ConnectionGeneration=2});
        Assert.Empty(await handler.ComposeAsync(recipient,new(new(1)),default));
        reminder.Check=null;Assert.Equal(HttpStatusCode.OK,(await host.PostAsync("/api/gm/v1/cera/grant",csrf,Payload(id))).StatusCode);
        Assert.Equal(new[]{"59"},await host.ReadAsync("SELECT cera FROM accounts WHERE account_id=1;"));
    }

    [Fact]
    public async Task UncertainPostCommitRecoversOriginalKeyWithoutSecondCredit()
    {
        var poison=new PoisonCommit();
        await using var host=await GmTestHost.StartAsync(configureServices:s=>s.AddSingleton<IGmAuditDomainService>(p=>new CommitFaultAudit(p.GetRequiredService<IGmAdministrationPersistence>(),p.GetRequiredService<IDomainTransactionAccessor>(),poison)));
        await SeedAsync(host);var csrf=await Login(host);var id=Guid.NewGuid();
        using var result=await host.PostAsync("/api/gm/v1/cera/grant",csrf,Payload(id));Assert.Equal(HttpStatusCode.InternalServerError,result.StatusCode);
        Assert.Equal("unknown",(await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("outcome").GetString());
        Assert.Equal(new[]{"60"},await host.ReadAsync("SELECT cera FROM accounts WHERE account_id=1;"));
        Assert.Equal(HttpStatusCode.OK,(await host.Client.GetAsync("/api/gm/v1/cera/operations/"+id)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await host.PostAsync("/api/gm/v1/cera/grant",csrf,Payload(id))).StatusCode);
        Assert.Equal(new[]{"Succeeded","Unknown"},await host.ReadAsync("SELECT outcome FROM gm_audit WHERE action='GrantCera' ORDER BY rowid;"));
        Assert.Equal(new[]{"60"},await host.ReadAsync("SELECT cera FROM accounts WHERE account_id=1;"));
    }

    [Fact]
    public async Task FailureAuditUnavailableIsReportedAsUnknownRatherThanUncredited()
    {
        await using var host=await GmTestHost.StartAsync();await SeedAsync(host);var csrf=await Login(host);
        await host.ExecuteAsync("CREATE TRIGGER bad_audit BEFORE INSERT ON gm_audit WHEN NEW.action='GrantCera' BEGIN SELECT RAISE(ABORT,'all audits fail'); END;");
        using var result=await host.PostAsync("/api/gm/v1/cera/grant",csrf,Payload(Guid.NewGuid()));Assert.Equal(HttpStatusCode.InternalServerError,result.StatusCode);
        Assert.Equal("unknown",(await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("outcome").GetString());
        Assert.Equal(new[]{"10"},await host.ReadAsync("SELECT cera FROM accounts WHERE account_id=1;"));
    }

    [Fact]
    public async Task PermissionsRevokedDuringQueuedGrantAreRecheckedInsideTransaction()
    {
        var gate=new CeraGate();
        await using var host=await GmTestHost.StartAsync(configureServices:s=>
        {
            var original=s.Single(x=>x.ServiceType==typeof(IGmCeraPersistence));
            s.AddSingleton<IGmCeraPersistence>(p=>new GatedPersistence((IGmCeraPersistence)ActivatorUtilities.CreateInstance(p,original.ImplementationType!),gate));
        });
        await SeedAsync(host);var csrf=await Login(host);gate.Armed=true;
        var queued=host.PostAsync("/api/gm/v1/cera/grant",csrf,Payload(Guid.NewGuid()));await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await host.ExecuteAsync("UPDATE gm_administrators SET permissions=2;");gate.Release.SetResult();
        using var result=await queued;Assert.Equal(HttpStatusCode.Forbidden,result.StatusCode);
        Assert.Equal(new[]{"10"},await host.ReadAsync("SELECT cera FROM accounts WHERE account_id=1;"));
    }

    private sealed class CeraGate
    {
        internal bool Armed{get;set;}
        internal TaskCompletionSource Entered{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    [Fact]
    public async Task MaximumBatchTotalUsesWideIntegerAndMalformedOversizedInputHasNoCredits()
    {
        await using var host=await GmTestHost.StartAsync();var csrf=await Login(host);
        var sql=new System.Text.StringBuilder();for(var id=1;id<=100;id++)sql.Append($"INSERT INTO accounts(account_id,m_id) VALUES({id},'batch{id}');");await host.ExecuteAsync(sql.ToString());
        var ids=Enumerable.Range(1,100).ToArray();using var preview=await host.PostAsync("/api/gm/v1/cera/preview",csrf,Payload(Guid.Empty,ids,amount:int.MaxValue));
        Assert.Equal(HttpStatusCode.OK,preview.StatusCode);Assert.Equal(214748364700L,(await preview.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("totalAmount").GetInt64());
        Assert.Equal(HttpStatusCode.OK,(await host.PostAsync("/api/gm/v1/cera/grant",csrf,Payload(Guid.NewGuid(),ids,amount:int.MaxValue))).StatusCode);
        Assert.Equal(new[]{"100"},await host.ReadAsync("SELECT COUNT(*) FROM accounts WHERE cera=2147483647;"));
        var malformed=new HttpRequestMessage(HttpMethod.Post,"/api/gm/v1/cera/grant"){Content=new StringContent("{\"accountIds\":[1],\"currency\":\"Cera\",\"amount\":1,\"reason\":\"\\uD800\"}",System.Text.Encoding.UTF8,"application/json")};malformed.Headers.Add("X-GM-CSRF",csrf);
        Assert.Equal(HttpStatusCode.BadRequest,(await host.Client.SendAsync(malformed)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await host.PostAsync("/api/gm/v1/cera/grant",csrf,Payload(Guid.NewGuid(),reason:new string('x',33000)))).StatusCode);
        Assert.Equal(new[]{"1"},await host.ReadAsync("SELECT COUNT(*) FROM gm_cera_operations;"));
    }
    [Fact]
    public async Task RealShopPurchasesCompeteWithGrantsWithoutLostSpendingOrRoleMultiplication()
    {
        await using var host=await GmTestHost.StartAsync();await GmPlayerDirectoryHttpTests.SeedAsync(host);
        await host.ExecuteAsync("UPDATE accounts SET cera=100000,token_cera=0,happy_token_cera=0 WHERE account_id=1; INSERT INTO character_stats(character_id,carried_gold) VALUES(1,0);");
        var user=await GmPlayerDirectoryHttpTests.ConnectAsync(host,1,21011);var session=GmPlayerDirectoryHttpTests.Publish(host,user,1,1,21011);
        var csrf=await Login(host);var commands=host.Services.GetRequiredService<ICommandBus>();
        var grants=Enumerable.Range(0,3).Select(_=>host.PostAsync("/api/gm/v1/cera/grant",csrf,Payload(Guid.NewGuid(),[1,1]))).ToArray();
        var purchases=Enumerable.Range(0,3).Select(_=>commands.SendAsync(new CeraShopPurchaseCommand(user.OnlineUserId,session,new(0,0,0,[new(0,255,255,100039,[],[])]))).AsTask()).ToArray();
        await Task.WhenAll(grants.Cast<Task>().Concat(purchases));var responses=await Task.WhenAll(grants);var bought=await Task.WhenAll(purchases);
        Assert.All(responses,x=>Assert.Equal(HttpStatusCode.OK,x.StatusCode));Assert.All(bought,x=>Assert.NotNull(x.Receipt));foreach(var r in responses)r.Dispose();
        Assert.Equal(new[]{"97810:0:0"},await host.ReadAsync("SELECT cera||':'||token_cera||':'||happy_token_cera FROM accounts WHERE account_id=1;"));
        Assert.Equal(new[]{"3"},await host.ReadAsync("SELECT COUNT(*) FROM gm_cera_operations;"));GmPlayerDirectoryHttpTests.Cleanup(host,session);
    }
    private sealed class GatedPersistence(IGmCeraPersistence inner,CeraGate gate):IGmCeraPersistence
    {
        public async ValueTask AcquireWriteAsync(CancellationToken token){if(gate.Armed){gate.Armed=false;gate.Entered.SetResult();await gate.Release.Task.WaitAsync(token);}await inner.AcquireWriteAsync(token);}
        public ValueTask<GmStoredPage<GmCeraAccountIdentity>> AccountsAsync(GmPlayerFilter f,CancellationToken t)=>inner.AccountsAsync(f,t);
        public ValueTask<GmCeraOperation?> OperationAsync(long a,Guid i,CancellationToken t)=>inner.OperationAsync(a,i,t);
        public ValueTask<GmPage<GmCeraReceipt>> OperationsAsync(long a,int p,int s,CancellationToken t)=>inner.OperationsAsync(a,p,s,t);
        public ValueTask SaveOperationAsync(long a,GmCeraOperation o,CancellationToken t)=>inner.SaveOperationAsync(a,o,t);
    }
    private sealed class PoisonCommit:ITransactionParticipant
    {
        public bool CommitCanFail=>false;
        public ValueTask PrepareAsync(CancellationToken t)=>ValueTask.CompletedTask;
        public ValueTask CommitAsync(CancellationToken t)=>throw new IOException("post-SQLite commit fault");
        public ValueTask RollbackAsync(CancellationToken t)=>ValueTask.CompletedTask;
    }
    private sealed class CommitFaultAudit(IGmAdministrationPersistence persistence,IDomainTransactionAccessor transactions,PoisonCommit poison):IGmAuditDomainService
    {
        public async ValueTask AppendAsync(GmAuditRecord r,CancellationToken t){await persistence.AppendAuditAsync(r,t);if(r.Action=="GrantCera"&&r.Outcome=="Succeeded")transactions.Current!.Enlist(poison,()=>poison);}
        public ValueTask<GmAuditPage> QueryAsync(int p,int s,CancellationToken t)=>persistence.QueryAuditAsync(p,s,t);
    }

    private sealed class FailingReminder : IGmCeraReminderDomainService
    {
        internal Func<Task>? Check {get;set;}
        public async ValueTask<string> RemindAsync(IReadOnlyList<int> accounts,CancellationToken token){if(Check is not null)await Check();throw new IOException("reminder injected");}
    }
}
