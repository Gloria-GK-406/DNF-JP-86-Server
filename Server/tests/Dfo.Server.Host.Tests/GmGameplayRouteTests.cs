using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.HttpApi;
using Dfo.Server.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;
using static Dfo.Server.Host.Tests.CredentialEntryHttpTests;

namespace Dfo.Server.Host.Tests;

/// <summary>Spec appendix A row 9 (RQ-015): GM routes run as GM CMDs on the executor, bodies read off it.</summary>
public sealed class GmGameplayRouteTests(ITestOutputHelper output)
{
    private const string Preview = "/api/gm/v1/mail/preview";

    /// <summary>B1: the body pre-buffer answers the three refusals exactly as the endpoint did when it read the body itself.</summary>
    [Theory]
    [InlineData("text", 400, "邮件请求超过 32 KiB 或不是 JSON。")]
    [InlineData("length", 400, "邮件请求超过 32 KiB 或不是 JSON。")]
    [InlineData("chunked", 400, "邮件请求字段无效或超过 32 KiB。")]
    public async Task Refused_bodies_keep_their_status_and_message(string shape, int status, string message)
    {
        await using var host = await GmTestHost.StartAsync();
        await GmPlayerDirectoryHttpTests.SeedAsync(host);
        var csrf = await GmCeraHttpTests.Login(host);
        var large = "{\"title\":\"" + new string('a', 40000) + "\"}";
        HttpContent content = shape switch
        {
            "text" => new StringContent("{}", Encoding.UTF8, "text/plain"),
            "length" => new StringContent(large, Encoding.UTF8, "application/json"),
            _ => new UnknownLengthContent(Encoding.UTF8.GetBytes(large)),
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, Preview) { Content = content };
        request.Headers.Add("X-GM-CSRF", csrf);
        if (shape == "chunked") request.Headers.TransferEncodingChunked = true;

        using var response = await host.Client.SendAsync(request);

        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"B1 {shape}: {(int)response.StatusCode} {body}");
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(message, JsonDocument.Parse(body).RootElement.GetProperty("message").GetString());
    }

    /// <summary>
    /// The routes that stay off the executor, each with its spec appendix B row (RQ-011). Adding one
    /// means adding it here and to appendix B.
    /// </summary>
    private static readonly string[] Exempt =
    [
        "GET /api/gm/v1/auth/csrf",
        "GET /api/gm/v1/auth/session",
        "GET /api/gm/v1/auth/status",
        "GET /api/gm/v1/health",
        "GET /api/gm/v1/items/icon-pack",
        "GET /api/gm/v1/items/{itemId:int}/icon",
        "GET /api/gm/v1/items/{itemId:int}/tooltip",
        "GET /api/gm/v1/mail/items",
        "GET /api/gm/v1/mail/items/{itemId:int}",
        "GET /api/gm/v1/server-info",
        "GET /api/launcher/v1/bootstrap",
        "GET /gm/{**path}",
    ];

    /// <summary>R1: every GM and launcher route is a CMD or an exemption with a reason, and the exemptions are exactly appendix B's.</summary>
    [Fact]
    public async Task Every_gm_and_launcher_route_is_a_cmd_or_a_listed_exemption()
    {
        var launcher = LauncherHost.Create();
        await using var host = await GmTestHost.StartAsync(directory: launcher.Directory, configuration: launcher.Settings);
        var routes = host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText is { } raw
                && (raw.StartsWith("/api/gm/", StringComparison.Ordinal) || raw.StartsWith("/gm/", StringComparison.Ordinal)
                    || raw.StartsWith("/api/launcher/", StringComparison.Ordinal)))
            .Select(Classify)
            .ToArray();

        foreach (var route in routes.OrderBy(route => route.Route, StringComparer.Ordinal))
            output.WriteLine($"R1 {route.Route} -> {route.Command ?? (route.Exempt is null ? route.Violation : "outside: " + route.Exempt)}");
        Assert.Empty(routes.Where(route => route.Violation is not null).Select(route => $"{route.Route}: {route.Violation}"));
        Assert.Equal(Exempt, routes.Where(route => route.Exempt is not null).Select(route => route.Route).Order(StringComparer.Ordinal));
        var commands = routes.Where(route => route.Command is not null).Select(route => route.Command!).ToArray();
        Assert.Equal(commands.Length, commands.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(40, commands.Count(command => command.StartsWith("gm:", StringComparison.Ordinal)));
        Assert.Equal(2, commands.Count(command => command.StartsWith("http:", StringComparison.Ordinal)));
    }

    /// <summary>R1's counter-example: a route that neither runs as a CMD nor states why not is refused, as is an empty reason.</summary>
    [Fact]
    public void A_route_that_is_neither_a_cmd_nor_an_exemption_with_a_reason_is_refused()
    {
        static RouteEndpoint Route(params object[] metadata) => new(_ => Task.CompletedTask,
            RoutePatternFactory.Parse("/api/gm/v1/rogue"), 0,
            new EndpointMetadataCollection([new HttpMethodMetadata(["POST"]), .. metadata]), "rogue");

        Assert.NotNull(Classify(Route()).Violation);
        Assert.NotNull(Classify(Route(new OutsideGameplay(" "))).Violation);
        Assert.Equal("gm:POST /api/gm/v1/rogue", Classify(Route(new GameplayCmdRoute(GameplayCallSource.Gm))).Command);
        Assert.Equal("outside", Classify(Route(new OutsideGameplay("outside"))).Exempt);
    }

    /// <summary>
    /// E1: each request is one GM CMD; everything it does downstream -- preparation, sending, the
    /// failure audit, the grant and its reminder, the directory read, the logout audit -- is that CMD.
    /// </summary>
    [Fact]
    public async Task Each_gm_request_runs_downstream_as_one_gm_cmd()
    {
        var calls = new GameplayCallLog();
        await using var host = await GmTestHost.StartAsync(configureServices: services =>
        {
            calls.Decorate<IGmOfficialMailDomainService>(services);
            calls.Decorate<IGmCeraDomainService>(services);
            calls.Decorate<IGmCeraReminderDomainService>(services);
            calls.Decorate<IGmPlayerDirectoryPersistence>(services);
            calls.Decorate<IGmAuditDomainService>(services);
        });
        await GmPlayerDirectoryHttpTests.SeedAsync(host);
        var user = await GmPlayerDirectoryHttpTests.ConnectAsync(host, 40, 10011);
        GmPlayerDirectoryHttpTests.Publish(host, user, 40, 40, 10011);
        var csrf = await GmCeraHttpTests.Login(host);
        object Mail() => new { requestId = Guid.NewGuid(), title = "官方补偿", body = "测试金币", recipientIds = new[] { 1 }, items = Array.Empty<object>(), gold = 100 };

        await AssertOneCmdAsync("gm:POST /api/gm/v1/mail/send", HttpStatusCode.OK,
            () => host.PostAsync("/api/gm/v1/mail/send", csrf, Mail()), "QueryAsync", "SendAsync");
        await host.ExecuteAsync("CREATE TRIGGER fail_receipt BEFORE INSERT ON gm_mail_operations BEGIN SELECT RAISE(ABORT,'isolated receipt fault'); END;");
        await AssertOneCmdAsync("gm:POST /api/gm/v1/mail/send", HttpStatusCode.InternalServerError,
            () => host.PostAsync("/api/gm/v1/mail/send", csrf, Mail()), "QueryAsync", "SendAsync", "RecordFailureAsync");
        await AssertOneCmdAsync("gm:POST /api/gm/v1/cera/grant", HttpStatusCode.OK,
            () => host.PostAsync("/api/gm/v1/cera/grant", csrf, GmCeraHttpTests.Payload(Guid.NewGuid(), [40])),
            "QueryAsync", "GrantAsync", "RemindAsync");
        await AssertOneCmdAsync("gm:GET /api/gm/v1/characters", HttpStatusCode.OK,
            () => host.Client.GetAsync("/api/gm/v1/characters?onlineOnly=true"), "CharactersAsync");
        await AssertOneCmdAsync("gm:POST /api/gm/v1/auth/logout", HttpStatusCode.NoContent,
            () => host.PostAsync("/api/gm/v1/auth/logout", csrf), "AppendAsync");

        async Task AssertOneCmdAsync(string commandType, HttpStatusCode status, Func<Task<HttpResponseMessage>> send, params string[] methods)
        {
            calls.Clear();
            using (var response = await send())
                Assert.True(response.StatusCode == status, $"{commandType}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
            var seen = calls.Calls;
            Assert.All(methods, method => Assert.Contains(seen, call => call.Method == method));
            Assert.All(seen, call => AssertCmd(call, GameplayCallSource.Gm, commandType));
            Assert.Single(seen.Select(call => call.JobId).Distinct());
        }
    }

    /// <summary>B2: a client that sends half its body holds no executor time; the CMD is queued only once the body is in memory.</summary>
    [Fact]
    public async Task A_slowly_sent_body_is_read_off_the_executor_before_its_cmd_is_queued()
    {
        var calls = new GameplayCallLog();
        var cookies = new CookieContainer();
        await using var host = await GmTestHost.StartAsync(cookies: cookies, configureServices: services =>
            calls.Decorate<IGmOfficialMailDomainService>(services));
        await GmPlayerDirectoryHttpTests.SeedAsync(host);
        var csrf = await GmCeraHttpTests.Login(host);
        var body = JsonSerializer.SerializeToUtf8Bytes(new { requestId = Guid.NewGuid(), title = "标题", body = "正文", recipientIds = new[] { 1 }, items = Array.Empty<object>(), gold = 0 });
        using var client = new TcpClient();
        await client.ConnectAsync(host.Address.Host, host.Address.Port);
        var stream = client.GetStream();
        var head = $"POST {Preview} HTTP/1.1\r\nHost: {host.Address.Authority}\r\nContent-Type: application/json\r\n" +
            $"Content-Length: {body.Length}\r\nX-GM-CSRF: {csrf}\r\nCookie: {cookies.GetCookieHeader(host.Address)}\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
        await stream.WriteAsync(body.AsMemory(0, body.Length / 2));
        await stream.FlushAsync();
        await Task.Delay(300);

        await host.Services.GetRequiredService<IGameplayQueue>().RunAsync(new GameplayWork(GameplayShardKey.World,
                GameplayCallChain.NewRoot(GameplayCallSource.Gm), "test:marker", _ => ValueTask.CompletedTask), default)
            .AsTask().WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Empty(calls.Calls);

        await stream.WriteAsync(body.AsMemory(body.Length / 2));
        using var reader = new StreamReader(stream, Encoding.ASCII);
        var status = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("HTTP/1.1 200 OK", status);
        AssertCmd(Assert.Single(calls.Of("QueryAsync")), GameplayCallSource.Gm, "gm:POST " + Preview);
    }

    /// <summary>S1: the startup writes and the GM bootstrap run as startup jobs; the bootstrap hash does not.</summary>
    [PvfFact]
    public async Task Startup_writes_and_the_gm_bootstrap_run_as_startup_jobs()
    {
        var calls = new GameplayCallLog();
        var passwords = new GameplayCallLog();
        await using var host = await GmTestHost.StartAsync(realContent: true,
            configuration: new Dictionary<string, string?> { ["ContentWarmup:Enabled"] = "true" }, configureServices: services =>
        {
            calls.Decorate<IEpicBookDomainService>(services);
            calls.Decorate<ICharacterExperienceStoragePolicy>(services);
            calls.Decorate<IGmAccessDomainService>(services);
            passwords.Decorate<IPasswordCredentialRuleService>(services);
        });

        AssertCmd(Assert.Single(calls.Of(nameof(IEpicBookDomainService.InitializeAsync))), GameplayCallSource.StartupRecovery,
            "startup:GameServerStartupService/epic-book");
        AssertCmd(Assert.Single(calls.Of(nameof(ICharacterExperienceStoragePolicy.InstallAsync))), GameplayCallSource.StartupRecovery,
            "startup:GameServerStartupService/experience-storage");
        AssertCmd(Assert.Single(calls.Of(nameof(IGmAccessDomainService.BootstrapAsync))), GameplayCallSource.StartupRecovery,
            "startup:GmBootstrapService");
        Assert.Single(passwords.Of(nameof(IPasswordCredentialRuleService.Hash)));
        Assert.All(passwords.Calls, call => Assert.False(call.OnExecutor));
        await ContentWarmupTests.AssertWarmedAsync(host.Services, host.Logs.Messages);
    }

    private static (string Route, string? Command, string? Exempt, string? Violation) Classify(RouteEndpoint endpoint)
    {
        var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [];
        var route = $"{string.Join(",", methods)} {endpoint.RoutePattern.RawText}";
        if (endpoint.Metadata.GetMetadata<OutsideGameplay>() is { } outside)
            return string.IsNullOrWhiteSpace(outside.Reason) ? (route, null, null, "exempt without a reason") : (route, null, outside.Reason, null);
        var source = endpoint.Metadata.GetMetadata<EnqueuesItself>()?.Source ?? endpoint.Metadata.GetMetadata<GameplayCmdRoute>()?.Source;
        return source is { } cmd ? (route, HttpGameplay.CommandTypeOf(cmd, endpoint), null, null) : (route, null, null, "neither a CMD nor exempt");
    }

    private sealed class UnknownLengthContent : HttpContent
    {
        private readonly byte[] bytes;

        public UnknownLengthContent(byte[] bytes)
        {
            this.bytes = bytes;
            Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override void SerializeToStream(Stream stream, TransportContext? context, CancellationToken cancellationToken) =>
            stream.Write(bytes);
    }
}
