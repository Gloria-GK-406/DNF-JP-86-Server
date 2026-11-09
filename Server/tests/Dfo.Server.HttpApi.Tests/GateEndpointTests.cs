using System.Net;
using System.Text;
using System.Text.Json;
using Dfo.Server.Application.Authentication;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Authentication;
using Dfo.Server.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.HttpApi.Tests;

/// <summary>
/// The three gate endpoints through the production pipeline: status fields and readiness, and
/// register/login answering alike over every transport once the token passed.
/// </summary>
public sealed class GateEndpointTests
{
    private const string Status = "/api/gate/v1/status";
    private const string Register = "/api/gate/v1/register";
    private const string Login = "/api/gate/v1/login";
    private const string Credentials = "{\"loginId\":\"player\",\"password\":\"password-1\"}";
    private static readonly IPAddress Remote = IPAddress.Parse("198.51.100.20");

    [Fact]
    public async Task StatusReportsTheContractFieldsWithoutOnline()
    {
        var token = GateTestToken.New();
        await using var harness = ApiHarness.Create(gateToken: token);

        using var response = await harness.SendAsync("GET", Status, scheme: "http", bearer: token);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        var status = response.Json.RootElement;
        Assert.Equal(
            ["channel", "gamePorts", "launch", "login", "pvfSha256", "registration", "startedAt", "state", "version"],
            status.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(ServerRuntimeInfo.Version, status.GetProperty("version").GetString());
        Assert.Equal("running", status.GetProperty("state").GetString());
        Assert.Equal(ApiHarness.ArchiveDigest, status.GetProperty("pvfSha256").GetString());
        var started = harness.Services.GetRequiredService<HostStartTime>().Value;
        Assert.Equal(started, status.GetProperty("startedAt").GetDateTimeOffset());
        Assert.Equal(TimeSpan.Zero, status.GetProperty("startedAt").GetDateTimeOffset().Offset);
        Assert.Equal("a21", status.GetProperty("launch").GetString());
        Assert.Equal("203.0.113.10", status.GetProperty("channel").GetProperty("host").GetString());
        Assert.Equal(7001, status.GetProperty("channel").GetProperty("port").GetInt32());
        Assert.Equal([10011, 10100], status.GetProperty("gamePorts").EnumerateArray().Select(port => port.GetInt32()));
        Assert.Equal("/api/gate/v1", status.GetProperty("login").GetString());
        Assert.True(status.GetProperty("registration").GetBoolean());
        AssertLauncherContract(status);
    }

    [Fact]
    public async Task StatusChannelIsTheChannelALoginAnswers()
    {
        var token = GateTestToken.New();
        await using var harness = ApiHarness.Create(LoginSucceeds, gateToken: token);

        using var status = await harness.SendAsync("GET", Status, scheme: "http", bearer: token);
        using var login = await harness.SendAsync("POST", Login, Credentials, scheme: "http", bearer: token);

        Assert.Equal(StatusCodes.Status200OK, login.StatusCode);
        Assert.Equal(
            login.Json.RootElement.GetProperty("channel").GetRawText(),
            status.Json.RootElement.GetProperty("channel").GetRawText());
    }

    public enum AdminSource { Configured, Contributed, None }

    [Theory]
    [InlineData(AdminSource.Configured, "http://10.0.0.12:7080/gm/")]
    [InlineData(AdminSource.Contributed, "http://127.0.0.1:7080/gm/")]
    [InlineData(AdminSource.None, null)]
    public async Task StatusAdminIsTheConfiguredAddressElseTheContributedOneElseOmitted(AdminSource source, string? expected)
    {
        var token = GateTestToken.New();
        await using var harness = ApiHarness.Create(
            gateToken: token,
            configuration: source == AdminSource.Configured
                ? new Dictionary<string, string?> { ["LauncherAuth:AdminUrl"] = "http://10.0.0.12:7080/gm/" }
                : null,
            configureServices: source == AdminSource.None
                ? null
                : services => services.AddSingleton(new GateAdminAddress("http://127.0.0.1:7080/gm/")));

        using var response = await harness.SendAsync("GET", Status, scheme: "http", bearer: token);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        if (expected is null)
        {
            Assert.False(response.Json.RootElement.TryGetProperty("admin", out _));
        }
        else
        {
            Assert.Equal(expected, response.Json.RootElement.GetProperty("admin").GetString());
        }
    }

    public enum Unready { Http, Game, NoDigest, BlankDigest, DigestFails }

    [Theory]
    [InlineData(Unready.Http)]
    [InlineData(Unready.Game)]
    [InlineData(Unready.NoDigest)]
    [InlineData(Unready.BlankDigest)]
    [InlineData(Unready.DigestFails)]
    public async Task StatusIsUnavailableUntilHttpGameAndTheArchiveDigestAreReady(Unready unready)
    {
        var token = GateTestToken.New();
        var logs = new CaptureLoggerProvider();
        await using var harness = ApiHarness.Create(loggerProvider: logs, gateToken: token);
        switch (unready)
        {
            case Unready.Http: harness.Readiness.SetReady(false); break;
            case Unready.Game: harness.Game.SetReady(false); break;
            case Unready.NoDigest: harness.Archive.Digest = () => null; break;
            case Unready.BlankDigest: harness.Archive.Digest = () => " "; break;
            case Unready.DigestFails: harness.Archive.Digest = () => throw new IOException("archive sentinel failure"); break;
        }

        using var status = await harness.SendAsync("GET", Status, scheme: "http", bearer: token);
        harness.Readiness.SetReady(false);
        using var login = await harness.SendAsync("POST", Login, Credentials, scheme: "http", bearer: token);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, status.StatusCode);
        Assert.Equal("service_unavailable", status.Json.RootElement.GetProperty("code").GetString());
        Assert.Equal(login.Body, status.Body);
        if (unready == Unready.DigestFails)
        {
            Assert.Contains(logs.Messages, message => message.StartsWith("Warning:", StringComparison.Ordinal)
                && message.Contains(nameof(IOException), StringComparison.Ordinal));
            // The type name only: the message may carry a deployment path.
            Assert.All(logs.Messages, message => Assert.DoesNotContain("archive sentinel failure", message, StringComparison.Ordinal));
        }
    }

    public sealed record Exchange(string Path, string Body, Func<ICommand, object>? Execute, int Status, bool GameReady = true, bool HttpReady = true)
    {
        public override string ToString() => $"{Path} {Body} -> {Status}";
    }

    public static TheoryData<Exchange> Exchanges => new()
    {
        new("register", Credentials, command => new RegistrationResult(AuthenticationStatus.Success, "player"), 201),
        new("register", Credentials, command => new RegistrationResult(AuthenticationStatus.InvalidRequest, null,
            new Dictionary<string, string> { ["loginId"] = "bad login" }), 400),
        new("register", Credentials, command => new RegistrationResult(AuthenticationStatus.AccountExists), 409),
        new("register", "{", null, 400),
        new("register", "{}", null, 400),
        new("register", Credentials, null, 503, HttpReady: false),
        new("login", Credentials, LoginSucceeds, 200),
        new("login", Credentials, command => new AuthenticationResult(AuthenticationStatus.InvalidCredentials), 401),
        new("login", Credentials, command => new AuthenticationResult(AuthenticationStatus.InvalidRequest,
            FieldErrors: new Dictionary<string, string> { ["password"] = "bad password" }), 400),
        new("login", "null", null, 400),
        new("login", Credentials, null, 503, HttpReady: false),
        new("login", Credentials, null, 503, GameReady: false),
    };

    /// <summary>
    /// RQ-005 and RQ-009 (AC-005): the same request answers byte for byte, with the same CMDs, whether it
    /// arrives as loopback HTTPS (the one transport the retired v1 routes accepted from anywhere) or as
    /// remote plain HTTP; no <c>secure_transport_required</c> remains.
    /// </summary>
    [Theory]
    [MemberData(nameof(Exchanges))]
    public async Task GateCredentialRoutesAnswerAlikeOverEveryTransport(Exchange exchange)
    {
        var token = GateTestToken.New();
        await using var harness = ApiHarness.Create(exchange.Execute, gateToken: token);
        harness.Readiness.SetReady(exchange.HttpReady);
        harness.Game.SetReady(exchange.GameReady);

        using var local = await harness.SendAsync("POST", "/api/gate/v1/" + exchange.Path, exchange.Body,
            remoteAddress: IPAddress.Loopback, bearer: token);
        var localCommands = harness.Commands.CallCount;
        using var remote = await harness.SendAsync("POST", "/api/gate/v1/" + exchange.Path, exchange.Body,
            scheme: "http", remoteAddress: Remote, bearer: token);

        Assert.Equal(exchange.Status, local.StatusCode);
        Assert.Equal(local.StatusCode, remote.StatusCode);
        Assert.Equal(local.Body, remote.Body);
        Assert.DoesNotContain("secure_transport_required", remote.Body, StringComparison.Ordinal);
        Assert.Equal(localCommands * 2, harness.Commands.CallCount);
    }

    [Fact]
    public async Task WithoutAConfiguredTokenEveryGatePathIsNotFoundWithoutReadingAnything()
    {
        await using var harness = ApiHarness.Create(LoginSucceeds, gateToken: null);

        foreach (var bearer in new[] { null, GateTestToken.New() })
        {
            using var status = await harness.SendAsync("GET", Status, scheme: "http", bearer: bearer);
            foreach (var path in new[] { Register, Login })
            {
                var body = new ReadTrackingStream(Encoding.UTF8.GetBytes(Credentials));
                using var refused = await harness.SendAsync("POST", path, Credentials, scheme: "http", requestBody: body, bearer: bearer);
                Assert.Equal(StatusCodes.Status404NotFound, refused.StatusCode);
                Assert.Equal("not_found", refused.Json.RootElement.GetProperty("code").GetString());
                Assert.Equal(0, body.ReadCount);
            }

            Assert.Equal(StatusCodes.Status404NotFound, status.StatusCode);
            Assert.Equal("not_found", status.Json.RootElement.GetProperty("code").GetString());
        }

        Assert.Equal(0, harness.Credentials.Registrations);
        Assert.Equal(0, harness.Credentials.LoginChecks);
        Assert.Equal(0, harness.Archive.Calls);
        Assert.Equal(0, harness.Commands.CallCount);
        Assert.False(harness.Services.GetRequiredService<GateAuthFailureLimiter>().IsLimited(Remote));
    }

    /// <summary>AC-004 end to end: wrong tokens throttle their source; the valid token passes all three endpoints regardless.</summary>
    [Fact]
    public async Task TheValidTokenReachesEveryGateEndpointFromAThrottledSource()
    {
        var token = GateTestToken.New();
        var wrong = GateTestToken.New();
        await using var harness = ApiHarness.Create(command => command switch
        {
            RegisterAccountCommand => new RegistrationResult(AuthenticationStatus.Success, "player"),
            _ => LoginSucceeds(command),
        }, gateToken: token);

        for (var i = 1; i <= 10; i++)
        {
            using var refused = await harness.SendAsync("GET", Status, scheme: "http", bearer: wrong);
            Assert.Equal(StatusCodes.Status401Unauthorized, refused.StatusCode);
        }

        using (var limited = await harness.SendAsync("POST", Login, Credentials, scheme: "http", bearer: wrong))
        {
            Assert.Equal(StatusCodes.Status429TooManyRequests, limited.StatusCode);
            Assert.Equal("too_many_requests", limited.Json.RootElement.GetProperty("code").GetString());
        }

        using (var status = await harness.SendAsync("GET", Status, scheme: "http", bearer: token))
            Assert.Equal(StatusCodes.Status200OK, status.StatusCode);
        using (var registered = await harness.SendAsync("POST", Register, Credentials, scheme: "http", bearer: token))
            Assert.Equal(StatusCodes.Status201Created, registered.StatusCode);
        using (var login = await harness.SendAsync("POST", Login, Credentials, scheme: "http", bearer: token))
            Assert.Equal(StatusCodes.Status200OK, login.StatusCode);
        Assert.Equal(1, harness.Credentials.Registrations);
        Assert.Equal(1, harness.Credentials.LoginChecks);
    }

    [Fact]
    public async Task WrongPasswordsBehindTheValidTokenAreNotTokenFailures()
    {
        var token = GateTestToken.New();
        await using var harness = ApiHarness.Create(
            _ => new AuthenticationResult(AuthenticationStatus.InvalidCredentials), gateToken: token);

        for (var i = 0; i < 20; i++)
        {
            using var refused = await harness.SendAsync("POST", Login, Credentials, scheme: "http", bearer: token);
            Assert.Equal(StatusCodes.Status401Unauthorized, refused.StatusCode);
            Assert.Equal("invalid_credentials", refused.Json.RootElement.GetProperty("code").GetString());
        }

        using var wrong = await harness.SendAsync("GET", Status, scheme: "http", bearer: GateTestToken.New());
        Assert.Equal(StatusCodes.Status401Unauthorized, wrong.StatusCode);
        Assert.Equal("unauthorized", wrong.Json.RootElement.GetProperty("code").GetString());
    }

    /// <summary>AC-006: the forwarded client address is logged for a valid request only, and never counted.</summary>
    [Fact]
    public async Task CredentialLogsNameTheForwardedClientOfAValidRequestOnly()
    {
        var token = GateTestToken.New();
        var logs = new CaptureLoggerProvider();
        await using var harness = ApiHarness.Create(command => command switch
        {
            RegisterAccountCommand => new RegistrationResult(AuthenticationStatus.Success, "player"),
            _ => LoginSucceeds(command),
        }, logs, gateToken: token);

        for (var i = 0; i < 10; i++)
        {
            using var refused = await harness.SendAsync("POST", Login, Credentials, scheme: "http", bearer: "wrong",
                forwardedFor: "198.51.100.7");
            Assert.Equal(StatusCodes.Status401Unauthorized, refused.StatusCode);
        }

        Assert.All(logs.Messages, message => Assert.DoesNotContain("198.51.100.7", message, StringComparison.Ordinal));
        var limiter = harness.Services.GetRequiredService<GateAuthFailureLimiter>();
        Assert.True(limiter.IsLimited(Remote));
        Assert.False(limiter.IsLimited(IPAddress.Parse("198.51.100.7")));

        using (var login = await harness.SendAsync("POST", Login, Credentials, scheme: "http", bearer: token,
            forwardedFor: "203.0.113.9, 198.51.100.7"))
            Assert.Equal(StatusCodes.Status200OK, login.StatusCode);
        Assert.Contains(logs.Messages, message => message.StartsWith("Information:", StringComparison.Ordinal)
            && message.Contains("login", StringComparison.Ordinal) && message.Contains("198.51.100.7", StringComparison.Ordinal));
        using (var registered = await harness.SendAsync("POST", Register, Credentials, scheme: "http", bearer: token,
            forwardedFor: "not-an-address"))
            Assert.Equal(StatusCodes.Status201Created, registered.StatusCode);
        Assert.Contains(logs.Messages, message => message.StartsWith("Information:", StringComparison.Ordinal)
            && message.Contains("register", StringComparison.Ordinal) && message.Contains(Remote.ToString(), StringComparison.Ordinal));
        Assert.All(logs.Messages, message =>
        {
            Assert.DoesNotContain("player", message, StringComparison.Ordinal);
            Assert.DoesNotContain(token, message, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// A method mismatch is answered by routing's 405 endpoint, which carries no gate metadata: it is
    /// neither authenticated nor counted, and reads nothing.
    /// </summary>
    [Fact]
    public async Task MethodMismatchIsMethodNotAllowedWithoutAuthenticationOrCounting()
    {
        var token = GateTestToken.New();
        await using var harness = ApiHarness.Create(LoginSucceeds, gateToken: token);

        for (var i = 0; i < 11; i++)
        {
            using var login = await harness.SendAsync("GET", Login, scheme: "http", bearer: "wrong");
            Assert.Equal(StatusCodes.Status405MethodNotAllowed, login.StatusCode);
            using var status = await harness.SendAsync("POST", Status, Credentials, scheme: "http");
            Assert.Equal(StatusCodes.Status405MethodNotAllowed, status.StatusCode);
        }

        Assert.Equal(0, harness.Credentials.LoginChecks);
        Assert.Equal(0, harness.Archive.Calls);
        Assert.False(harness.Services.GetRequiredService<GateAuthFailureLimiter>().IsLimited(Remote));
        using var refused = await harness.SendAsync("GET", Status, scheme: "http", bearer: "wrong");
        Assert.Equal(StatusCodes.Status401Unauthorized, refused.StatusCode);
    }

    private static object LoginSucceeds(ICommand command) => command switch
    {
        AuthenticateAccountCommand => new AuthenticationResult(
            AuthenticationStatus.Success,
            "123456",
            "0123456789ABCDEF0123456789ABCDEF",
            new DateTimeOffset(2026, 9, 10, 12, 34, 56, TimeSpan.Zero)),
        _ => throw new InvalidOperationException("Unexpected command."),
    };

    /// <summary>
    /// The launcher interface contract, section 3.1, rule by rule (the launcher's
    /// <c>GameServerStatusValidator</c>), without referencing the launcher repository.
    /// </summary>
    private static void AssertLauncherContract(JsonElement status)
    {
        static bool IsPort(JsonElement port) => port.ValueKind == JsonValueKind.Number && port.GetInt32() is >= 1 and <= 65535;

        Assert.False(string.IsNullOrWhiteSpace(status.GetProperty("version").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(status.GetProperty("state").GetString()));
        var digest = status.GetProperty("pvfSha256").GetString()!;
        Assert.True(digest.Length == 64 && digest.All(char.IsAsciiHexDigit), digest);
        Assert.False(string.IsNullOrWhiteSpace(status.GetProperty("launch").GetString()));
        var channel = status.GetProperty("channel");
        Assert.False(string.IsNullOrWhiteSpace(channel.GetProperty("host").GetString()));
        Assert.True(IsPort(channel.GetProperty("port")));
        var login = status.GetProperty("login").GetString()!;
        Assert.True(login.StartsWith('/') && !login.StartsWith("//", StringComparison.Ordinal) && login.IndexOfAny(['?', '#', '\\']) < 0, login);
        Assert.Equal(JsonValueKind.True, status.GetProperty("registration").ValueKind);
        Assert.False(status.TryGetProperty("online", out _));
        Assert.All(status.GetProperty("gamePorts").EnumerateArray(), port => Assert.True(IsPort(port)));
        Assert.Equal(JsonValueKind.String, status.GetProperty("startedAt").ValueKind);
    }
}
