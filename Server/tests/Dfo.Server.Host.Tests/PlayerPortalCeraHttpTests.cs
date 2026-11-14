using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.PlayerPortal;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Administration;
using Dfo.Server.Data.PlayerPortal;
using Microsoft.Extensions.DependencyInjection;
using static Dfo.Server.Host.Tests.PlayerPortalTestSupport;

namespace Dfo.Server.Host.Tests;

/// <summary>#329 TASK-04: a player adds cera or token cera to the own account, once per request id, within the limits.</summary>
public sealed class PlayerPortalCeraHttpTests
{
    private const string GrantPath = "/api/player/v1/cera/grant";

    private static async Task<(GmTestHost Host, PlayerClient Player, string Csrf)> StartAsync(TimeProvider? clock = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var host = await GmTestHost.StartAsync(clock: clock, configureServices: configureServices);
        await SeedAccountAsync(host, 1, "alice"); await SeedAccountAsync(host, 2, "bob", cera: 100, tokenCera: 200);
        await SetPortalAsync(host);
        var player = new PlayerClient(host);
        return (host, player, await player.SignInAsync("alice"));
    }

    private static Task<List<string>> AuditAsync(GmTestHost host) =>
        host.ReadAsync("SELECT outcome||':'||actor_kind||':'||IFNULL(account_id,'-')||':'||administrator_name||':'||IFNULL(administrator_id,'-') FROM gm_audit WHERE action='PlayerGrantCera' ORDER BY rowid;");
    private static Task<List<string>> OperationCountAsync(GmTestHost host) => host.ReadAsync("SELECT COUNT(*) FROM player_portal_operations;");

    private static async Task AssertRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code, string outcome = "rejected")
    {
        Assert.Equal(status, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.Equal(outcome, body.GetProperty("outcome").GetString());
    }

    /// <summary>AC-010: only the named currency grows; other currencies, amounts and a wallet overflow are refused unchanged.</summary>
    [Fact]
    public async Task Adding_cera_or_token_cera_credits_only_that_currency_and_refuses_anything_else()
    {
        var (host, player, csrf) = await StartAsync();
        await using var _ = host; using var __ = player;
        var id = Guid.NewGuid();
        using (var granted = await player.GrantAsync(csrf, "Cera", 1000, id))
        {
            Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
            var body = await JsonAsync(granted);
            Assert.False(body.GetProperty("replayed").GetBoolean());
            Assert.Equal("offline", body.GetProperty("reminder").GetString());
            var receipt = body.GetProperty("receipt");
            Assert.Equal(["requestId", "kind", "committedAt", "summary", "result", "currency", "amount", "before", "after"],
                receipt.EnumerateObject().Select(property => property.Name));
            Assert.Equal(id, receipt.GetProperty("requestId").GetGuid());
            Assert.Equal("cera", receipt.GetProperty("kind").GetString());
            Assert.Equal("点券 +1000", receipt.GetProperty("summary").GetString());
            Assert.Equal("成功", receipt.GetProperty("result").GetString());
            Assert.Equal("Cera", receipt.GetProperty("currency").GetString());
            Assert.Equal(1000, receipt.GetProperty("amount").GetInt32());
            Assert.Equal("{\"cera\":10,\"tokenCera\":20}", receipt.GetProperty("before").GetRawText());
            Assert.Equal("{\"cera\":1010,\"tokenCera\":20}", receipt.GetProperty("after").GetRawText());
        }
        Assert.Equal("1010:20", await BalanceAsync(host, 1));
        using (var token = await player.GrantAsync(csrf, "TokenCera", 5))
        {
            Assert.Equal(HttpStatusCode.OK, token.StatusCode);
            Assert.Equal("代币券 +5", (await JsonAsync(token)).GetProperty("receipt").GetProperty("summary").GetString());
        }
        Assert.Equal("1010:25", await BalanceAsync(host, 1));

        foreach (var (currency, amount) in new[] { ("Cera", 0), ("Cera", -1), ("HappyTokenCera", 1), ("Gold", 1), ("cera", 1) })
            await AssertRefusedAsync(await player.GrantAsync(csrf, currency, amount), HttpStatusCode.BadRequest, "invalid");
        await host.ExecuteAsync("UPDATE accounts SET cera=2147483000 WHERE account_id=1;");
        await AssertRefusedAsync(await player.GrantAsync(csrf, "Cera", 1000), HttpStatusCode.BadRequest, "invalid");
        Assert.Equal("2147483000:25", await BalanceAsync(host, 1));
        await host.ExecuteAsync("UPDATE accounts SET cera=1010 WHERE account_id=1;");

        // Only the session names the account: a body field is undeclared, a query parameter is ignored (RQ-003).
        Assert.Equal("invalid", await CodeAsync(await player.PostAsync(GrantPath, csrf, new { requestId = Guid.NewGuid(), currency = "Cera", amount = 1, accountId = 2 })));
        using (var query = await player.PostAsync(GrantPath + "?accountId=2", csrf, new { requestId = Guid.NewGuid(), currency = "Cera", amount = 1 }))
            Assert.Equal(HttpStatusCode.OK, query.StatusCode);
        Assert.Equal("1011:25", await BalanceAsync(host, 1));
        Assert.Equal("100:200", await BalanceAsync(host, 2));
        foreach (var payload in new object[] { new { currency = "Cera", amount = 1 }, new { requestId = Guid.Empty, currency = "Cera", amount = 1 },
            new { requestId = "x", currency = "Cera", amount = 1 }, new { requestId = Guid.NewGuid(), currency = "Cera", amount = 2147483648L },
            new { requestId = Guid.NewGuid(), currency = "Cera", amount = "1" } })
            Assert.Equal("invalid", await CodeAsync(await player.PostAsync(GrantPath, csrf, payload)));

        Assert.Equal(["3"], await OperationCountAsync(host));
        // Six refusals after the player was identified are audited; unparsed requests are not (SD-1a).
        Assert.Equal([.. Enumerable.Repeat("Succeeded:player:1:alice:-", 2), .. Enumerable.Repeat("Rejected:player:1:alice:-", 6), "Succeeded:player:1:alice:-"],
            await AuditAsync(host));
    }

    /// <summary>All limits zero: any amount goes, still within the wallet's own rule.</summary>
    [Fact]
    public async Task Zero_limits_leave_only_the_wallet_rule()
    {
        var (host, player, csrf) = await StartAsync();
        await using var _ = host; using var __ = player;
        Assert.Equal(HttpStatusCode.OK, (await player.GrantAsync(csrf, "Cera", 2000000000)).StatusCode);
        await AssertRefusedAsync(await player.GrantAsync(csrf, "Cera", 2000000000), HttpStatusCode.BadRequest, "invalid");
        Assert.Equal("2000000010:20", await BalanceAsync(host, 1));
    }

    /// <summary>RQ-010: the account is reminded after the commit, on a replay too; a failed reminder leaves the grant a success.</summary>
    [Fact]
    public async Task The_reminder_goes_to_the_own_account_and_a_failed_one_leaves_the_grant_a_success()
    {
        var reminder = new RecordingReminder();
        var (host, player, csrf) = await StartAsync(configureServices: services => services.AddSingleton<IGmCeraReminderDomainService>(reminder));
        await using var _ = host; using var __ = player;
        var id = Guid.NewGuid();
        Assert.Equal("attempted", (await JsonAsync(await player.GrantAsync(csrf, "Cera", 1, id))).GetProperty("reminder").GetString());
        Assert.Equal("attempted", (await JsonAsync(await player.GrantAsync(csrf, "Cera", 1, id))).GetProperty("reminder").GetString());
        reminder.Fail = true;
        using (var failed = await player.GrantAsync(csrf, "TokenCera", 2))
        {
            Assert.Equal(HttpStatusCode.OK, failed.StatusCode);
            var body = await JsonAsync(failed);
            Assert.Equal("failed", body.GetProperty("reminder").GetString());
            Assert.False(body.GetProperty("replayed").GetBoolean());
        }
        Assert.Equal([[1], [1], [1]], reminder.Calls.ToArray());
        Assert.Equal("11:22", await BalanceAsync(host, 1));
        Assert.Contains(host.Logs.Messages, message => message.Contains("reminderFailed", StringComparison.Ordinal));
        Assert.Equal(["2"], await OperationCountAsync(host));
    }

    /// <summary>AC-012: one credit per request id; a replay answers the original even once the limit is used up or the switch is closed.</summary>
    [Fact]
    public async Task A_request_id_succeeds_once_and_replays_even_after_the_limit_or_the_switch_closes()
    {
        var (host, player, csrf) = await StartAsync();
        await using var _ = host; using var __ = player;
        await SetCeraLimitsAsync(host, ceraPerDay: 1000);
        var id = Guid.NewGuid();
        string original;
        using (var first = await player.GrantAsync(csrf, "Cera", 1000, id))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            original = (await JsonAsync(first)).GetProperty("receipt").GetRawText();
        }
        async Task Replays()
        {
            using var again = await player.GrantAsync(csrf, "Cera", 1000, id);
            Assert.Equal(HttpStatusCode.OK, again.StatusCode);
            var body = await JsonAsync(again);
            Assert.True(body.GetProperty("replayed").GetBoolean());
            Assert.Equal(original, body.GetProperty("receipt").GetRawText());
        }
        await Replays();                                      // the daily limit is used up
        await AssertRefusedAsync(await player.GrantAsync(csrf, "Cera", 999, id), HttpStatusCode.Conflict, "request_conflict");
        await AssertRefusedAsync(await player.GrantAsync(csrf, "TokenCera", 1000, id), HttpStatusCode.Conflict, "request_conflict");
        await SetPortalAsync(host, cera: false);
        await Replays();                                      // the cera switch is closed
        await AssertRefusedAsync(await player.GrantAsync(csrf, "Cera", 1), HttpStatusCode.Forbidden, "feature_disabled");
        Assert.Equal("1010:20", await BalanceAsync(host, 1));

        // The id is the account's own: another account may use the same one.
        await SetPortalAsync(host);
        using var bob = new PlayerClient(host);
        using (var other = await bob.GrantAsync(await bob.SignInAsync("bob"), "Cera", 5, id))
        {
            Assert.Equal(HttpStatusCode.OK, other.StatusCode);
            Assert.False((await JsonAsync(other)).GetProperty("replayed").GetBoolean());
        }
        Assert.Equal("105:200", await BalanceAsync(host, 2));
        Assert.Equal(["2"], await OperationCountAsync(host));
        Assert.Equal(["Succeeded:1", "Rejected:1", "Rejected:1", "Rejected:1", "Succeeded:2"],
            await host.ReadAsync("SELECT outcome||':'||account_id FROM gm_audit WHERE action='PlayerGrantCera' ORDER BY rowid;"));
    }

    /// <summary>AC-021: the cera switch refuses only the grant; a closed portal answers portal_closed, never 500.</summary>
    [Fact]
    public async Task Closing_the_cera_switch_refuses_only_grants_and_a_closed_portal_answers_portal_closed()
    {
        var (host, player, csrf) = await StartAsync();
        await using var _ = host; using var __ = player;
        await SetPortalAsync(host, cera: false);
        await AssertRefusedAsync(await player.GrantAsync(csrf, "Cera", 1), HttpStatusCode.Forbidden, "feature_disabled");
        using (var me = await player.Client.GetAsync("/api/player/v1/me"))
        {
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
            Assert.False((await JsonAsync(me)).GetProperty("portal").GetProperty("ceraEnabled").GetBoolean());
        }
        Assert.Equal(HttpStatusCode.OK, (await player.Client.GetAsync("/api/player/v1/operations")).StatusCode);
        await SetPortalAsync(host, enabled: false);
        using (var closed = await player.GrantAsync(csrf, "Cera", 1))
        {
            Assert.Equal(HttpStatusCode.Forbidden, closed.StatusCode);
            Assert.Equal("portal_closed", await CodeAsync(closed));
        }
        Assert.Equal("10:20", await BalanceAsync(host, 1));
        Assert.Equal(["Rejected:player:1:alice:-"], await AuditAsync(host));
    }

    /// <summary>AC-008(a): the portal closed after a grant passed the boundary refuses it inside the CMD, with nothing written.</summary>
    [Fact]
    public async Task Closing_the_portal_after_a_grant_passed_the_boundary_refuses_it_inside_the_cmd()
    {
        var gate = new BoundaryGate();
        var (host, player, csrf) = await StartAsync(configureServices: gate.Wrap);
        await using var _ = host; using var __ = player;
        var gmCsrf = await GmCeraHttpTests.Login(host);
        gate.Arm(1);
        var pending = player.GrantAsync(csrf, "Cera", 1000);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using (var close = await host.PostAsync("/api/gm/v1/player-portal/settings", gmCsrf, GmPlayerPortalHttpTests.Settings(enabled: false, revision: 1)))
            Assert.Equal(HttpStatusCode.OK, close.StatusCode);
        gate.Release.SetResult();
        // An outcome is only answered by the CMD: the boundary's own refusal has none.
        await AssertRefusedAsync(await pending, HttpStatusCode.Forbidden, "portal_closed");
        Assert.Equal("10:20", await BalanceAsync(host, 1));
        Assert.Equal(["0"], await OperationCountAsync(host));
        Assert.Empty(await AuditAsync(host));
    }

    /// <summary>AC-008(c): with 1000 of a 1500 daily limit used, two concurrent 500s: exactly one succeeds.</summary>
    [Fact]
    public async Task Two_concurrent_grants_never_pass_the_daily_limit_together()
    {
        var gate = new BoundaryGate();
        var (host, player, csrf) = await StartAsync(configureServices: gate.Wrap);
        await using var _ = host; using var __ = player;
        await SetCeraLimitsAsync(host, ceraPerDay: 1500);
        Assert.Equal(HttpStatusCode.OK, (await player.GrantAsync(csrf, "Cera", 1000)).StatusCode);
        gate.Arm(2);
        var both = new[] { player.GrantAsync(csrf, "Cera", 500), player.GrantAsync(csrf, "Cera", 500) };
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        gate.Release.SetResult();
        var responses = await Task.WhenAll(both);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        var refused = Assert.Single(responses, response => response.StatusCode != HttpStatusCode.OK);
        await AssertRefusedAsync(refused, HttpStatusCode.Conflict, "limit_exceeded");
        foreach (var response in responses) response.Dispose();
        Assert.Equal("1510:20", await BalanceAsync(host, 1));
        Assert.Equal(["2"], await OperationCountAsync(host));
        Assert.Single(await AuditAsync(host), row => row.StartsWith("Rejected", StringComparison.Ordinal));
    }

    /// <summary>AC-022: per-grant and daily limits, refusals not counted, and a new game day starts again.</summary>
    [Fact]
    public async Task Limits_count_only_succeeded_grants_and_a_new_game_day_starts_again()
    {
        var clock = new GmTestClock();
        var (host, player, csrf) = await StartAsync(clock);
        await using var _ = host; using var __ = player;
        await SetCeraLimitsAsync(host, ceraPerGrant: 1000, ceraPerDay: 1500);
        await AssertRefusedAsync(await player.GrantAsync(csrf, "Cera", 1001), HttpStatusCode.Conflict, "limit_exceeded");
        Assert.Equal(HttpStatusCode.OK, (await player.GrantAsync(csrf, "Cera", 1000)).StatusCode);   // the refused 1001 counted nothing
        Assert.Equal(HttpStatusCode.OK, (await player.GrantAsync(csrf, "Cera", 500)).StatusCode);    // exactly the daily limit
        await AssertRefusedAsync(await player.GrantAsync(csrf, "Cera", 1), HttpStatusCode.Conflict, "limit_exceeded");

        var days = host.Services.GetRequiredService<IDailyResetRuleService>();
        var yesterday = days.PeriodDay(clock.GetUtcNow());
        clock.Advance(days.PeriodStart(clock.GetUtcNow()).AddDays(1) - clock.GetUtcNow() + TimeSpan.FromSeconds(1));
        var today = days.PeriodDay(clock.GetUtcNow());
        Assert.NotEqual(yesterday, today);
        csrf = await player.SignInAsync("alice");
        Assert.Equal(HttpStatusCode.OK, (await player.GrantAsync(csrf, "Cera", 1)).StatusCode);
        Assert.Equal([$"{yesterday:yyyy-MM-dd}:1500", $"{today:yyyy-MM-dd}:1"],
            await host.ReadAsync("SELECT period_day||':'||SUM(amount) FROM player_portal_operations GROUP BY period_day ORDER BY period_day;"));
        Assert.Equal(1, (await JsonAsync(await player.Client.GetAsync("/api/player/v1/me"))).GetProperty("portal").GetProperty("today").GetProperty("cera").GetInt64());
        Assert.Equal("1511:20", await BalanceAsync(host, 1));
    }

    /// <summary>TASK-01 review: cera and token cera have separate daily limits; a wallet refusal is not counted either.</summary>
    [Fact]
    public async Task Each_currency_has_its_own_daily_limit_and_a_wallet_refusal_counts_nothing()
    {
        var (host, player, csrf) = await StartAsync();
        await using var _ = host; using var __ = player;
        await SetCeraLimitsAsync(host, ceraPerDay: 1000, tokenCeraPerDay: 1000);
        Assert.Equal(HttpStatusCode.OK, (await player.GrantAsync(csrf, "Cera", 1000)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await player.GrantAsync(csrf, "TokenCera", 1000)).StatusCode);
        await AssertRefusedAsync(await player.GrantAsync(csrf, "Cera", 1), HttpStatusCode.Conflict, "limit_exceeded");
        await AssertRefusedAsync(await player.GrantAsync(csrf, "TokenCera", 1), HttpStatusCode.Conflict, "limit_exceeded");
        var today = (await JsonAsync(await player.Client.GetAsync("/api/player/v1/me"))).GetProperty("portal").GetProperty("today");
        Assert.Equal(1000, today.GetProperty("cera").GetInt64());
        Assert.Equal(1000, today.GetProperty("tokenCera").GetInt64());

        using var bob = new PlayerClient(host);
        var bobCsrf = await bob.SignInAsync("bob");
        await host.ExecuteAsync("UPDATE accounts SET cera=2147483000 WHERE account_id=2;");
        await AssertRefusedAsync(await bob.GrantAsync(bobCsrf, "Cera", 1000), HttpStatusCode.BadRequest, "invalid");
        await host.ExecuteAsync("UPDATE accounts SET cera=0 WHERE account_id=2;");
        Assert.Equal(HttpStatusCode.OK, (await bob.GrantAsync(bobCsrf, "Cera", 1000)).StatusCode);
        Assert.Equal("1000:200", await BalanceAsync(host, 2));
    }

    /// <summary>AC-024: success and refusal are the player's records with the account; the GM audit filters them.</summary>
    [Fact]
    public async Task Player_grants_are_audited_as_the_player_and_filtered_by_actor()
    {
        var (host, player, csrf) = await StartAsync();
        await using var _ = host; using var __ = player;
        await SetCeraLimitsAsync(host, ceraPerGrant: 10);
        Assert.Equal(HttpStatusCode.OK, (await player.GrantAsync(csrf, "Cera", 10)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await player.GrantAsync(csrf, "Cera", 11)).StatusCode);
        using (var anonymous = new PlayerClient(host))
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GrantAsync(await anonymous.CsrfAsync(), "Cera", 1)).StatusCode);
        Assert.Equal(["Succeeded:player:1:alice:-", "Rejected:player:1:alice:-"], await AuditAsync(host));
        Assert.Equal(["点券 +10", "未提交：单次最多 10。"],
            (await host.ReadAsync("SELECT reason FROM gm_audit WHERE action='PlayerGrantCera' ORDER BY rowid;")).Select(reason => reason[..reason.IndexOf('，')]));

        await GmCeraHttpTests.Login(host);
        var page = await JsonAsync(await host.Client.GetAsync("/api/gm/v1/audit?actor=player&action=PlayerGrantCera"));
        Assert.Equal(2, page.GetProperty("total").GetInt64());
        Assert.All(page.GetProperty("items").EnumerateArray(), item =>
        {
            Assert.Equal("player", item.GetProperty("actorKind").GetString());
            Assert.Equal(1, item.GetProperty("accountId").GetInt32());
        });
    }

    /// <summary>A commit that cannot be confirmed is audited as unknown under the player; the same id then replays, credited once.</summary>
    [Fact]
    public async Task An_unconfirmed_commit_is_audited_under_the_player_and_the_same_id_replays()
    {
        var poison = new PoisonCommit();
        var (host, player, csrf) = await StartAsync(configureServices: services => services.AddSingleton<IGmAuditDomainService>(provider =>
            new CommitFaultAudit(provider.GetRequiredService<IGmAdministrationPersistence>(), provider.GetRequiredService<IDomainTransactionAccessor>(), poison)));
        await using var _ = host; using var __ = player;
        var id = Guid.NewGuid();
        using (var unconfirmed = await player.GrantAsync(csrf, "Cera", 50, id))
        {
            Assert.Equal(HttpStatusCode.InternalServerError, unconfirmed.StatusCode);
            var body = await JsonAsync(unconfirmed);
            Assert.Equal("unknown", body.GetProperty("code").GetString());
            Assert.Equal("unknown", body.GetProperty("outcome").GetString());
        }
        Assert.Equal(["Succeeded:player:1:alice:-", "Unknown:player:1:alice:-"], await AuditAsync(host));
        using (var replay = await player.GrantAsync(csrf, "Cera", 50, id))
        {
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.True((await JsonAsync(replay)).GetProperty("replayed").GetBoolean());
        }
        Assert.Equal("60:20", await BalanceAsync(host, 1));
    }

    /// <summary>When no audit can be written at all, the answer is unknown and nothing is credited.</summary>
    [Fact]
    public async Task An_unwritable_audit_answers_unknown_and_credits_nothing()
    {
        var (host, player, csrf) = await StartAsync();
        await using var _ = host; using var __ = player;
        await host.ExecuteAsync("CREATE TRIGGER bad_audit BEFORE INSERT ON gm_audit WHEN NEW.action='PlayerGrantCera' BEGIN SELECT RAISE(ABORT,'all audits fail'); END;");
        using var result = await player.GrantAsync(csrf, "Cera", 50);
        Assert.Equal(HttpStatusCode.InternalServerError, result.StatusCode);
        Assert.Equal("unknown", (await JsonAsync(result)).GetProperty("outcome").GetString());
        Assert.Equal("10:20", await BalanceAsync(host, 1));
        Assert.Equal(["0"], await OperationCountAsync(host));
    }

    /// <summary>
    /// Holds player requests after the boundary identified them (the CMD's own checks do not pass here), until the armed
    /// number of requests has arrived and the test releases them.
    /// </summary>
    internal sealed class BoundaryGate
    {
        private readonly object sync = new();
        private int expected;
        private int entered;
        internal TaskCompletionSource Entered { get; private set; } = New();
        internal TaskCompletionSource Release { get; private set; } = New();
        private static TaskCompletionSource New() => new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal void Arm(int requests) { lock (sync) { expected = requests; entered = 0; Entered = New(); Release = New(); } }

        private Task PassAsync()
        {
            lock (sync)
            {
                if (entered >= expected) return Task.CompletedTask;
                if (++entered == expected) Entered.SetResult();
                return Release.Task;
            }
        }

        internal void Wrap(IServiceCollection services)
        {
            var original = services.Last(descriptor => descriptor.ServiceType == typeof(IPlayerPortalAccessDomainService));
            services.AddSingleton<IPlayerPortalAccessDomainService>(provider =>
                new Gated((IPlayerPortalAccessDomainService)ActivatorUtilities.CreateInstance(provider, original.ImplementationType!), this));
        }

        private sealed class Gated(IPlayerPortalAccessDomainService inner, BoundaryGate gate) : IPlayerPortalAccessDomainService
        {
            public ValueTask<bool> IsOpenAsync(CancellationToken token) => inner.IsOpenAsync(token);
            public async ValueTask<PlayerIdentity?> IdentifyAsync(string? secret, CancellationToken token)
            {
                var identity = await inner.IdentifyAsync(secret, token);
                await gate.PassAsync();
                return identity;
            }
            public ValueTask<PlayerIdentity> AuthorizeAsync(string? secret, CancellationToken token) => inner.AuthorizeAsync(secret, token);
            public ValueTask<PlayerLoginResult> LoginAsync(PlayerCredentialCheck check, CancellationToken token) => inner.LoginAsync(check, token);
            public ValueTask LogoutAsync(string secret, CancellationToken token) => inner.LogoutAsync(secret, token);
        }
    }

    private sealed class RecordingReminder : IGmCeraReminderDomainService
    {
        internal ConcurrentQueue<int[]> Calls { get; } = new();
        internal bool Fail { get; set; }
        public ValueTask<string> RemindAsync(IReadOnlyList<int> accounts, CancellationToken token)
        {
            Calls.Enqueue([.. accounts]);
            return Fail ? throw new IOException("reminder injected") : ValueTask.FromResult("attempted");
        }
    }

    private sealed class PoisonCommit : ITransactionParticipant
    {
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask CommitAsync(CancellationToken token) => throw new IOException("post-SQLite commit fault");
        public ValueTask RollbackAsync(CancellationToken token) => ValueTask.CompletedTask;
    }

    private sealed class CommitFaultAudit(IGmAdministrationPersistence persistence, IDomainTransactionAccessor transactions, PoisonCommit poison) : IGmAuditDomainService
    {
        public async ValueTask AppendAsync(GmAuditRecord record, CancellationToken token)
        {
            await persistence.AppendAuditAsync(record, token);
            if (record is { Action: "PlayerGrantCera", Outcome: "Succeeded" }) transactions.Current!.Enlist(poison, () => poison);
        }
        public ValueTask<GmAuditPage> QueryAsync(int page, int pageSize, GmAuditFilter filter, CancellationToken token) =>
            persistence.QueryAuditAsync(page, pageSize, filter, token);
    }
}
