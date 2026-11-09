using System.Net;
using System.Text;
using Dfo.Server.Application.Authentication;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Authentication;
using Dfo.Server.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.HttpApi.Tests;

/// <summary>
/// The launcher's credential routes, now the gate's register and login, through the production pipeline.
/// Every harness here holds this run's gate token and every request presents it unless a case says otherwise.
/// </summary>
public sealed class HttpApiPipelineTests
{
    private const string Register = "/api/gate/v1/register";
    private const string Login = "/api/gate/v1/login";
    private const string Credentials = "{\"loginId\":\"player\",\"password\":\"password-1\"}";
    private static readonly string Token = GateTestToken.New();

    /// <summary>
    /// The retired launcher v1 prefix, assembled from its segments so that a search for the old path
    /// finds callers only; this is the one place that still names it, to prove it is gone (AC-010).
    /// </summary>
    private static readonly string RetiredPrefix = string.Join('/', "", "api", "launcher", "v1");

    /// <summary>RQ-011: the launcher v1 routes are gone, with or without the gate token, and nothing behind them runs.</summary>
    [Fact]
    public async Task RetiredLauncherV1RoutesAreNotFound()
    {
        await using var harness = Harness(_ => throw new InvalidOperationException("Unexpected command."));

        foreach (var bearer in new[] { null, Token })
        {
            foreach (var (method, path) in new[] { ("GET", "/bootstrap"), ("POST", "/register"), ("POST", "/login") })
            {
                var body = new ReadTrackingStream(Encoding.UTF8.GetBytes(Credentials));
                using var response = await harness.SendAsync(method, RetiredPrefix + path, Credentials,
                    scheme: "http", remoteAddress: IPAddress.Loopback, requestBody: body, bearer: bearer);
                Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
                Assert.Equal(0, body.ReadCount);
            }
        }

        Assert.Equal(0, harness.Commands.CallCount);
        Assert.Equal(0, harness.Credentials.Registrations);
        Assert.Equal(0, harness.Credentials.LoginChecks);
    }

    [Theory]
    [InlineData(AuthenticationStatus.Success, StatusCodes.Status201Created, null)]
    [InlineData(AuthenticationStatus.InvalidRequest, StatusCodes.Status400BadRequest, "invalid_request")]
    [InlineData(AuthenticationStatus.AccountExists, StatusCodes.Status409Conflict, "account_exists")]
    public async Task RegisterMapsApplicationResultsToStableHttpContract(
        AuthenticationStatus commandStatus,
        int expectedStatus,
        string? expectedCode)
    {
        var fieldErrors = commandStatus == AuthenticationStatus.InvalidRequest
            ? new Dictionary<string, string> { ["loginId"] = "bad login" }
            : null;
        await using var harness = Harness(command => command switch
        {
            RegisterAccountCommand => new RegistrationResult(
                commandStatus,
                commandStatus == AuthenticationStatus.Success ? "player" : null,
                fieldErrors),
            _ => throw new InvalidOperationException("Unexpected command."),
        });

        var response = await harness.SendAsync(
            "POST",
            Register,
            "{\"loginId\":\"player\",\"password\":\"password-1\"}",
            bearer: Token);

        Assert.Equal(expectedStatus, response.StatusCode);
        if (expectedCode is null)
        {
            Assert.Equal("player", response.Json.RootElement.GetProperty("loginId").GetString());
        }
        else
        {
            Assert.Equal(expectedCode, response.Json.RootElement.GetProperty("code").GetString());
            if (commandStatus == AuthenticationStatus.InvalidRequest)
            {
                Assert.Equal(
                    "bad login",
                    response.Json.RootElement.GetProperty("fieldErrors").GetProperty("loginId").GetString());
            }
            else
            {
                Assert.False(response.Json.RootElement.TryGetProperty("fieldErrors", out _));
            }
        }
    }

    [Theory]
    [InlineData(AuthenticationStatus.InvalidRequest, StatusCodes.Status400BadRequest, "invalid_request")]
    [InlineData(AuthenticationStatus.InvalidCredentials, StatusCodes.Status401Unauthorized, "invalid_credentials")]
    public async Task LoginMapsRefusalsWithoutExposingCredentialDetails(
        AuthenticationStatus commandStatus,
        int expectedStatus,
        string expectedCode)
    {
        await using var harness = Harness(command => command switch
        {
            AuthenticateAccountCommand => new AuthenticationResult(
                commandStatus,
                FieldErrors: commandStatus == AuthenticationStatus.InvalidRequest
                    ? new Dictionary<string, string> { ["password"] = "bad password" }
                    : null),
            _ => throw new InvalidOperationException("Unexpected command."),
        });

        var response = await harness.SendAsync(
            "POST",
            Login,
            "{\"loginId\":\"unknown\",\"password\":\"secret-sentinel\"}",
            bearer: Token);

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(expectedCode, response.Json.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("secret-sentinel", response.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("unknown", response.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoginSuccessProjectsTicketExpiryAndTheConfiguredChannelBootstrap()
    {
        var expiry = new DateTimeOffset(2026, 9, 10, 12, 34, 56, TimeSpan.Zero);
        await using var harness = Harness(command => command switch
        {
            AuthenticateAccountCommand { Login.LoginId: "player" } =>
                new AuthenticationResult(
                    AuthenticationStatus.Success,
                    "123456",
                    "0123456789ABCDEF0123456789ABCDEF",
                    expiry),
            _ => throw new InvalidOperationException("Unexpected command."),
        });

        var response = await harness.SendAsync(
            "POST",
            Login,
            "{\"loginId\":\"player\",\"password\":\"password-1\"}",
            bearer: Token);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal("123456", response.Json.RootElement.GetProperty("mId").GetString());
        Assert.False(response.Json.RootElement.TryGetProperty("loginId", out _));
        Assert.Equal("0123456789ABCDEF0123456789ABCDEF", response.Json.RootElement.GetProperty("ticket").GetString());
        Assert.Equal(expiry, response.Json.RootElement.GetProperty("expiresAtUtc").GetDateTimeOffset());
        var channel = response.Json.RootElement.GetProperty("channel");
        Assert.Equal("203.0.113.10", channel.GetProperty("host").GetString());
        Assert.Equal(7001, channel.GetProperty("port").GetInt32());
    }

    /// <summary>
    /// RQ-005: remote plain HTTP is no longer refused for its transport. Without the token the request is
    /// refused before its body is read or anything is dispatched; with it, the same request is served.
    /// </summary>
    [Theory]
    [InlineData(Register, "{\"loginId\":\"player\",\"password\":\"password-1\"}", StatusCodes.Status201Created, 1)]
    [InlineData(Register, "{", StatusCodes.Status400BadRequest, 0)]
    [InlineData(Register + "/", "{\"loginId\":\"player\",\"password\":\"password-1\"}", StatusCodes.Status201Created, 1)]
    [InlineData(Register + "/", "{", StatusCodes.Status400BadRequest, 0)]
    [InlineData(Login, "{\"loginId\":\"player\",\"password\":\"password-1\"}", StatusCodes.Status200OK, 1)]
    [InlineData(Login, "{", StatusCodes.Status400BadRequest, 0)]
    [InlineData(Login + "/", "{\"loginId\":\"player\",\"password\":\"password-1\"}", StatusCodes.Status200OK, 1)]
    [InlineData(Login + "/", "{", StatusCodes.Status400BadRequest, 0)]
    public async Task RemotePlaintextCredentialRequestsAreServedWithTheTokenAndRefusedBeforeAnyBodyReadWithoutIt(
        string path,
        string body,
        int expectedWithToken,
        int expectedCommands)
    {
        await using var harness = Harness(command => command switch
        {
            RegisterAccountCommand => new RegistrationResult(AuthenticationStatus.Success, "player"),
            AuthenticateAccountCommand => new AuthenticationResult(
                AuthenticationStatus.Success,
                "player",
                "ticket_safe-123",
                new DateTimeOffset(2026, 9, 10, 12, 34, 56, TimeSpan.Zero)),
            _ => throw new InvalidOperationException("Unexpected command."),
        });
        var remote = IPAddress.Parse("198.51.100.20");

        var refusedBody = new ReadTrackingStream(Encoding.UTF8.GetBytes(body));
        var refused = await harness.SendAsync("POST", path, body, scheme: "http", remoteAddress: remote, requestBody: refusedBody);

        Assert.Equal(StatusCodes.Status401Unauthorized, refused.StatusCode);
        Assert.Equal("unauthorized", refused.Json.RootElement.GetProperty("code").GetString());
        Assert.Equal(0, refusedBody.ReadCount);
        Assert.Equal(0, harness.Commands.CallCount);

        var served = await harness.SendAsync("POST", path, body, scheme: "http", remoteAddress: remote, bearer: Token);

        Assert.Equal(expectedWithToken, served.StatusCode);
        Assert.DoesNotContain("secure_transport_required", served.Body, StringComparison.Ordinal);
        if (expectedWithToken == StatusCodes.Status400BadRequest)
            Assert.Equal("invalid_request", served.Json.RootElement.GetProperty("code").GetString());
        Assert.Equal(expectedCommands, harness.Commands.CallCount);
    }

    [Theory]
    [InlineData(Register, null)]
    [InlineData(Register, "text/plain")]
    [InlineData(Login, null)]
    [InlineData(Login, "text/plain")]
    public async Task MissingOrUnsupportedCredentialContentTypeUsesStableInvalidRequest(
        string path,
        string? contentType)
    {
        const string secret = "content-type-secret-sentinel";
        await using var harness = Harness();
        var body = $"{{\"loginId\":\"player\",\"password\":\"{secret}\"}}";
        var trackingBody = new ReadTrackingStream(Encoding.UTF8.GetBytes(body));

        var response = await harness.SendAsync(
            "POST",
            path,
            body,
            requestBody: trackingBody,
            contentType: contentType,
            bearer: Token);

        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", response.Json.RootElement.GetProperty("code").GetString());
        Assert.Equal(0, harness.Commands.CallCount);
        Assert.Equal(0, trackingBody.ReadCount);
        Assert.DoesNotContain(secret, response.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Register, "application/json")]
    [InlineData(Register, "application/problem+json")]
    [InlineData(Login, "application/json")]
    [InlineData(Login, "application/problem+json")]
    public async Task SupportedJsonContentTypesStillMapMalformedBodyToInvalidRequest(
        string path,
        string contentType)
    {
        await using var harness = Harness();

        var response = await harness.SendAsync("POST", path, "{", contentType: contentType, bearer: Token);

        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", response.Json.RootElement.GetProperty("code").GetString());
        Assert.Equal(0, harness.Commands.CallCount);
    }

    [Theory]
    [InlineData(Register + "/", "http", "127.0.0.1", StatusCodes.Status201Created)]
    [InlineData(Register + "/", "http", "198.51.100.20", StatusCodes.Status201Created)]
    [InlineData(Register + "/", "https", "198.51.100.20", StatusCodes.Status201Created)]
    [InlineData(Login + "/", "http", "127.0.0.1", StatusCodes.Status200OK)]
    [InlineData(Login + "/", "http", "198.51.100.20", StatusCodes.Status200OK)]
    [InlineData(Login + "/", "https", "198.51.100.20", StatusCodes.Status200OK)]
    public async Task RoutedCredentialEndpointsAreAvailableOverEveryTransportWithTheToken(
        string path,
        string scheme,
        string remoteAddress,
        int expectedStatus)
    {
        await using var harness = Harness(command => command switch
        {
            RegisterAccountCommand => new RegistrationResult(
                AuthenticationStatus.Success,
                "player"),
            AuthenticateAccountCommand => new AuthenticationResult(
                AuthenticationStatus.Success,
                "player",
                "ticket_safe-123",
                new DateTimeOffset(2026, 9, 10, 12, 34, 56, TimeSpan.Zero)),
            _ => throw new InvalidOperationException("Unexpected command."),
        });

        var response = await harness.SendAsync(
            "POST",
            path,
            "{\"loginId\":\"player\",\"password\":\"password-1\"}",
            scheme: scheme,
            remoteAddress: IPAddress.Parse(remoteAddress),
            bearer: Token);

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(1, harness.Commands.CallCount);
    }

    [Theory]
    [InlineData(Register, "null")]
    [InlineData(Register, "{}")]
    [InlineData(Register, "{")]
    [InlineData(Login, "null")]
    [InlineData(Login, "{}")]
    [InlineData(Login, "{")]
    public async Task NullMissingAndMalformedCredentialJsonUsesTheSameStableError(
        string path,
        string body)
    {
        await using var harness = Harness();

        var response = await harness.SendAsync("POST", path, body, bearer: Token);

        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", response.Json.RootElement.GetProperty("code").GetString());
        Assert.False(response.Json.RootElement.TryGetProperty("fieldErrors", out _));
        Assert.Equal(0, harness.Commands.CallCount);
    }

    [Theory]
    [InlineData(Register)]
    [InlineData(Login)]
    public async Task CredentialEndpointsDoNotReadOrDispatchWhileServiceIsUnavailable(string path)
    {
        await using var harness = Harness();
        harness.Readiness.SetReady(false);

        var response = await harness.SendAsync(
            "POST",
            path,
            "{\"loginId\":\"player\",\"password\":\"password-1\"}",
            bearer: Token);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, response.StatusCode);
        Assert.Equal("service_unavailable", response.Json.RootElement.GetProperty("code").GetString());
        Assert.Equal(0, harness.Commands.CallCount);
    }

    [Fact]
    public async Task MissingAccountAndWrongPasswordHaveByteIdenticalExternalSemantics()
    {
        await using var harness = Harness(command => command switch
        {
            AuthenticateAccountCommand =>
                new AuthenticationResult(AuthenticationStatus.InvalidCredentials),
            _ => throw new InvalidOperationException("Unexpected command."),
        });

        var wrong = await harness.SendAsync(
            "POST",
            Login,
            "{\"loginId\":\"known\",\"password\":\"wrong-password\"}",
            bearer: Token);
        var missing = await harness.SendAsync(
            "POST",
            Login,
            "{\"loginId\":\"missing\",\"password\":\"wrong-password\"}",
            bearer: Token);

        Assert.Equal(StatusCodes.Status401Unauthorized, wrong.StatusCode);
        Assert.Equal(wrong.Body, missing.Body);
    }

    [Fact]
    public async Task LoopbackPlaintextReachesBindingAndMalformedJsonGetsStableError()
    {
        await using var harness = Harness();

        var response = await harness.SendAsync(
            "POST",
            Register,
            "{",
            scheme: "http",
            remoteAddress: IPAddress.Loopback,
            bearer: Token);

        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", response.Json.RootElement.GetProperty("code").GetString());
        Assert.Equal(0, harness.Commands.CallCount);
    }

    [Fact]
    public async Task UnexpectedCommandFailureReturnsSafeInternalError()
    {
        var logs = new CaptureLoggerProvider();
        await using var harness = Harness(
            _ => throw new InvalidOperationException("SQL stack secret-sentinel"),
            logs);

        var response = await harness.SendAsync(
            "POST",
            Login,
            "{\"loginId\":\"player\",\"password\":\"secret-sentinel\"}",
            bearer: Token);

        Assert.Equal(StatusCodes.Status500InternalServerError, response.StatusCode);
        Assert.Equal("internal_error", response.Json.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("secret-sentinel", response.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("SQL", response.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack", response.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-sentinel", string.Join("\n", logs.Messages), StringComparison.Ordinal);
    }

    /// <summary>
    /// L1 at the endpoint: a record changed between the credential check and the login CMD is checked
    /// again once; changed again, the caller is refused as with a wrong password.
    /// </summary>
    [Theory]
    [InlineData(1, StatusCodes.Status200OK)]
    [InlineData(2, StatusCodes.Status401Unauthorized)]
    public async Task LoginChecksTheCredentialAgainOnceWhenItChangedBeforeTheCommand(int changes, int expectedStatus)
    {
        var sent = 0;
        await using var harness = Harness(command => command switch
        {
            AuthenticateAccountCommand => ++sent <= changes
                ? new AuthenticationResult(AuthenticationStatus.CredentialChanged)
                : new AuthenticationResult(
                    AuthenticationStatus.Success,
                    "123456",
                    "0123456789ABCDEF0123456789ABCDEF",
                    new DateTimeOffset(2026, 9, 10, 12, 34, 56, TimeSpan.Zero)),
            _ => throw new InvalidOperationException("Unexpected command."),
        });

        var response = await harness.SendAsync(
            "POST",
            Login,
            "{\"loginId\":\"player\",\"password\":\"password-1\"}",
            bearer: Token);

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(2, harness.Credentials.LoginChecks);
        Assert.Equal(2, harness.Commands.CallCount);
        if (expectedStatus == StatusCodes.Status401Unauthorized)
            Assert.Equal("invalid_credentials", response.Json.RootElement.GetProperty("code").GetString());
    }

    private static ApiHarness Harness(Func<ICommand, object>? execute = null, ILoggerProvider? logs = null) =>
        ApiHarness.Create(execute, logs, gateToken: Token);
}
