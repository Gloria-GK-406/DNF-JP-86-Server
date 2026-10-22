using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Dfo.Server.Host.Tests;

public sealed class LauncherHttpHostStartupTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task GmComponentHasItsOwnListenerAndDoesNotExposeLauncherRoutes(bool launcher, bool game)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dfo-gm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var httpPort = GetAvailablePort();
        var gmPort = GetAvailablePortExcept(httpPort);
        var channelPort = GetAvailablePortExcept(httpPort, gmPort);
        var gamePort = GetAvailablePortExcept(httpPort, gmPort, channelPort);
        var output = new StringBuilder();
        using var process = StartHost(directory, httpPort, channelPort, gamePort, output, launcher,
            new Dictionary<string, string>
            {
                ["GmWeb:Enabled"] = "true",
                ["GmWeb:ListenUrl"] = $"http://127.0.0.1:{gmPort}",
                ["Networking:Enabled"] = game.ToString(),
            });
        try
        {
            // The packaged host warms content before listening; under full-suite load that alone can
            // take tens of seconds. Startup is not a latency assertion (TASK-16, F-54).
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var gm = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{gmPort}") };
            HttpResponseMessage? health = null;
            while (health is null)
            {
                if (process.HasExited) throw new Xunit.Sdk.XunitException(Snapshot(output));
                try { health = await gm.GetAsync("/api/gm/v1/health", timeout.Token); }
                catch (HttpRequestException) { await Task.Delay(100, timeout.Token); }
            }
            using (health) Assert.Equal(HttpStatusCode.OK, health.StatusCode);
            using var wrong = await gm.GetAsync("/api/launcher/v1/bootstrap", timeout.Token);
            Assert.Equal(HttpStatusCode.NotFound, wrong.StatusCode);
            Assert.Equal(game, await CanConnectAsync(channelPort));
            if (launcher)
            {
                using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{httpPort}") };
                using var bootstrap = await client.GetAsync("/api/launcher/v1/bootstrap", timeout.Token);
                Assert.Equal(HttpStatusCode.OK, bootstrap.StatusCode);
                using var gmAtLauncher = await client.GetAsync("/api/gm/v1/health", timeout.Token);
                Assert.Equal(HttpStatusCode.NotFound, gmAtLauncher.StatusCode);
                using var mailAtLauncher = await client.GetAsync("/api/gm/v1/mail/draft", timeout.Token);
                Assert.Equal(HttpStatusCode.NotFound, mailAtLauncher.StatusCode);
                using var sendAtLauncher = await client.PostAsync("/api/gm/v1/mail/send", new StringContent("{}", Encoding.UTF8, "application/json"), timeout.Token);
                Assert.Equal(HttpStatusCode.NotFound, sendAtLauncher.StatusCode);
            }
            else Assert.False(await CanConnectAsync(httpPort));
        }
        finally { await StopAsync(process); await TestDirectory.DeleteAsync(directory); }
    }

    [Fact]
    public async Task HttpWithoutGame_AllowsBootstrapAndRegistrationButDoesNotIssueGameTickets()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dfo-http-only-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var httpPort = GetAvailablePort();
        var channelPort = GetAvailablePortExcept(httpPort);
        var gamePort = GetAvailablePortExcept(httpPort, channelPort);
        var output = new StringBuilder();
        using var process = StartHost(directory, httpPort, channelPort, gamePort, output,
            commandLineOverrides: new Dictionary<string, string> { ["Networking:Enabled"] = "false" });
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{httpPort}") };
            using var bootstrap = await WaitForBootstrapAsync(process, client, output, timeout.Token);
            Assert.Equal(HttpStatusCode.OK, bootstrap.StatusCode);
            using var content = new StringContent(
                "{\"loginId\":\"http-only-player\",\"password\":\"password-1\"}", Encoding.UTF8, "application/json");
            using var registration = await client.PostAsync("/api/launcher/v1/register", content, timeout.Token);
            Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
            using var login = await PostCredentialsAsync(client, "http-only-player", "password-1", timeout.Token);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, login.StatusCode);
            Assert.Equal("service_unavailable", JsonCode(await login.Content.ReadAsStringAsync(timeout.Token)));
            Assert.False(await CanConnectAsync(channelPort));
            Assert.False(await CanConnectAsync(gamePort));
        }
        finally
        {
            await StopAsync(process);
            await TestDirectory.DeleteAsync(directory);
        }
    }

    [Fact]
    public async Task EnabledHttpApi_ListensOnlyOnTheConfiguredUrlAndReturnsReadyBootstrap()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dfo-launcher-host-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var httpPort = GetAvailablePort();
        var channelPort = GetAvailablePortExcept(httpPort);
        var gamePort = GetAvailablePortExcept(httpPort, channelPort);
        var output = new StringBuilder();
        using var process = StartHost(directory, httpPort, channelPort, gamePort, output);

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{httpPort}") };
            var response = await WaitForBootstrapAsync(process, client, output, timeout.Token);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(timeout.Token));
            Assert.Equal("1", json.RootElement.GetProperty("apiVersion").GetString());
            Assert.Equal("ready", json.RootElement.GetProperty("status").GetString());
            Assert.True(json.RootElement.GetProperty("registrationEnabled").GetBoolean());
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }

            await TestDirectory.DeleteAsync(directory);
        }
    }

    [Fact]
    public async Task RealHttpRegistrationRaceAndCredentialFailuresKeepTheStableContract()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dfo-launcher-auth-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var httpPort = GetAvailablePort();
        var channelPort = GetAvailablePortExcept(httpPort);
        var gamePort = GetAvailablePortExcept(httpPort, channelPort);
        var output = new StringBuilder();
        using var process = StartHost(directory, httpPort, channelPort, gamePort, output);

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{httpPort}") };
            using var bootstrap = await WaitForBootstrapAsync(process, client, output, timeout.Token);
            Assert.Equal(HttpStatusCode.OK, bootstrap.StatusCode);

            const string registration = "{\"loginId\":\"race-player\",\"password\":\"password-1\"}";
            var registrations = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
            {
                using var content = new StringContent(registration, Encoding.UTF8, "application/json");
                using var response = await client.PostAsync(
                    "/api/launcher/v1/register",
                    content,
                    timeout.Token);
                return (response.StatusCode, Body: await response.Content.ReadAsStringAsync(timeout.Token));
            }));
            Assert.Single(registrations, result => result.StatusCode == HttpStatusCode.Created);
            Assert.Equal(7, registrations.Count(result => result.StatusCode == HttpStatusCode.Conflict));
            Assert.All(
                registrations.Where(result => result.StatusCode == HttpStatusCode.Conflict),
                result => Assert.Equal("account_exists", JsonCode(result.Body)));

            using var correct = await PostCredentialsAsync(
                client,
                "race-player",
                "password-1",
                timeout.Token);
            Assert.Equal(HttpStatusCode.OK, correct.StatusCode);
            using var loginJson = JsonDocument.Parse(
                await correct.Content.ReadAsStringAsync(timeout.Token));
            Assert.Equal("127.0.0.1", loginJson.RootElement.GetProperty("channel").GetProperty("host").GetString());
            Assert.Equal(channelPort, loginJson.RootElement.GetProperty("channel").GetProperty("port").GetInt32());

            using var wrong = await PostCredentialsAsync(
                client,
                "race-player",
                "wrong-password",
                timeout.Token);
            using var missing = await PostCredentialsAsync(
                client,
                "missing-player",
                "wrong-password",
                timeout.Token);
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
            Assert.Equal(
                await wrong.Content.ReadAsStringAsync(timeout.Token),
                await missing.Content.ReadAsStringAsync(timeout.Token));
        }
        finally
        {
            await StopAsync(process);
            await TestDirectory.DeleteAsync(directory);
        }
    }

    [Fact]
    public async Task DisabledHttpApi_AllowsTwoIsolatedHostsWithoutAHiddenKestrelListener()
    {
        var firstDirectory = Path.Combine(Path.GetTempPath(), $"dfo-no-http-a-{Guid.NewGuid():N}");
        var secondDirectory = Path.Combine(Path.GetTempPath(), $"dfo-no-http-b-{Guid.NewGuid():N}");
        Directory.CreateDirectory(firstDirectory);
        Directory.CreateDirectory(secondDirectory);
        var sharedUnusedHttpPort = GetAvailablePort();
        var firstChannel = GetAvailablePortExcept(sharedUnusedHttpPort);
        var secondChannel = GetAvailablePortExcept(sharedUnusedHttpPort, firstChannel);
        var firstGame = GetAvailablePortExcept(sharedUnusedHttpPort, firstChannel, secondChannel);
        var secondGame = GetAvailablePortExcept(sharedUnusedHttpPort, firstChannel, secondChannel, firstGame);
        var firstOutput = new StringBuilder();
        var secondOutput = new StringBuilder();
        using var first = StartHost(
            firstDirectory,
            sharedUnusedHttpPort,
            firstChannel,
            firstGame,
            firstOutput,
            launcherEnabled: false);
        using var second = StartHost(
            secondDirectory,
            sharedUnusedHttpPort,
            secondChannel,
            secondGame,
            secondOutput,
            launcherEnabled: false);

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await Task.WhenAll(
                WaitForTcpListenerAsync(first, firstChannel, firstOutput, timeout.Token),
                WaitForTcpListenerAsync(second, secondChannel, secondOutput, timeout.Token));

            Assert.False(first.HasExited, Snapshot(firstOutput));
            Assert.False(second.HasExited, Snapshot(secondOutput));
            Assert.DoesNotContain("Now listening on", Snapshot(firstOutput), StringComparison.Ordinal);
            Assert.DoesNotContain("Now listening on", Snapshot(secondOutput), StringComparison.Ordinal);
            Assert.False(await CanConnectAsync(sharedUnusedHttpPort));
        }
        finally
        {
            await StopAsync(first);
            await StopAsync(second);
            await TestDirectory.DeleteAsync(firstDirectory);
            await TestDirectory.DeleteAsync(secondDirectory);
        }
    }

    [Theory]
    [InlineData("LauncherAuth:PublicChannel:Host", "", "LauncherAuth:PublicChannel:Host")]
    [InlineData("LauncherAuth:PublicChannel:Port", "0", "LauncherAuth:PublicChannel:Port")]
    [InlineData("Channel:Channels:0:Host", "", "Channel:Channels:0:Host")]
    [InlineData("Channel:Channels:0:Port", "invalid", "Channel:Channels:0:Port")]
    [InlineData("Game:EndpointHost", "", "Game:EndpointHost")]
    [InlineData("GameLogin:EndpointPortA", "65536", "GameLogin:EndpointPortA")]
    [InlineData("LauncherAuth:LegacyMd5Migration:AllowedAccountIds:0", "*", "Legacy MD5 migration")]
    public async Task InvalidLauncherConfigurationFailsBeforeAnyListenerStarts(
        string key,
        string value,
        string expectedOutput)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dfo-bad-launcher-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var httpPort = GetAvailablePort();
        var channelPort = GetAvailablePortExcept(httpPort);
        var gamePort = GetAvailablePortExcept(httpPort, channelPort);
        var output = new StringBuilder();
        using var process = StartHost(
            directory,
            httpPort,
            channelPort,
            gamePort,
            output,
            commandLineOverrides: new Dictionary<string, string> { [key] = value });

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await process.WaitForExitAsync(timeout.Token);
            Assert.NotEqual(0, process.ExitCode);
            Assert.Contains(expectedOutput, Snapshot(output), StringComparison.OrdinalIgnoreCase);
            Assert.False(await CanConnectAsync(httpPort));
            Assert.False(await CanConnectAsync(channelPort));
        }
        finally
        {
            await StopAsync(process);
            await TestDirectory.DeleteAsync(directory);
        }
    }

    private static Process StartHost(
        string workingDirectory,
        int httpPort,
        int channelPort,
        int gamePort,
        StringBuilder output,
        bool launcherEnabled = true,
        IReadOnlyDictionary<string, string>? commandLineOverrides = null)
    {
        var assembly = typeof(Program).Assembly.Location;
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(assembly);
        if (commandLineOverrides is not null)
        {
            foreach (var (key, value) in commandLineOverrides)
            {
                startInfo.ArgumentList.Add($"--{key}={value}");
            }
        }

        startInfo.Environment["LauncherAuth__Enabled"] = launcherEnabled.ToString();
        startInfo.Environment["LauncherAuth__ListenUrl"] = $"http://127.0.0.1:{httpPort}";
        startInfo.Environment["LauncherAuth__PublicChannel__Host"] = "127.0.0.1";
        startInfo.Environment["LauncherAuth__PublicChannel__Port"] = channelPort.ToString();
        startInfo.Environment["Channel__UseScriptCatalog"] = "false";
        startInfo.Environment["Networking__Enabled"] = "true";
        startInfo.Environment["Networking__UdpDiscoveryEnabled"] = "false";
        startInfo.Environment["Networking__ChannelPort"] = channelPort.ToString();
        startInfo.Environment["Networking__GamePorts__0"] = gamePort.ToString();
        startInfo.Environment["Networking__GamePorts__1"] = GetAvailablePortExcept(httpPort, channelPort, gamePort).ToString();
        startInfo.Environment["Networking__GamePorts__2"] = GetAvailablePortExcept(
            httpPort, channelPort, gamePort, int.Parse(startInfo.Environment["Networking__GamePorts__1"]!)).ToString();
        startInfo.Environment["Channel__PublicServerIp"] = "127.0.0.1";
        startInfo.Environment["Channel__Channels__0__Host"] = "127.0.0.1";
        startInfo.Environment["Channel__Channels__0__Port"] = gamePort.ToString();
        startInfo.Environment["Channel__Channels__1__Host"] = "127.0.0.1";
        startInfo.Environment["Channel__Channels__1__Port"] = startInfo.Environment["Networking__GamePorts__1"];
        startInfo.Environment["Channel__Channels__2__Host"] = "127.0.0.1";
        startInfo.Environment["Channel__Channels__2__Port"] = startInfo.Environment["Networking__GamePorts__2"];
        startInfo.Environment["Game__EndpointHost"] = "127.0.0.1";
        startInfo.Environment["GameLogin__ConnectionPort"] = "10128";
        startInfo.Environment["GameLogin__EndpointPortA"] = "17200";
        startInfo.Environment["GameLogin__EndpointPortB"] = "17201";
        startInfo.Environment["Database__Path"] = Path.Combine(workingDirectory, "host.db");

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The Host process could not be started.");
        process.OutputDataReceived += (_, args) => Append(output, args.Data);
        process.ErrorDataReceived += (_, args) => Append(output, args.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static async Task<HttpResponseMessage> PostCredentialsAsync(
        HttpClient client,
        string loginId,
        string password,
        CancellationToken cancellationToken)
    {
        using var content = new StringContent(
            JsonSerializer.Serialize(new { loginId, password }),
            Encoding.UTF8,
            "application/json");
        return await client.PostAsync("/api/launcher/v1/login", content, cancellationToken);
    }

    private static string? JsonCode(string body)
    {
        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("code").GetString();
    }

    private static async Task<HttpResponseMessage> WaitForBootstrapAsync(
        Process process,
        HttpClient client,
        StringBuilder output,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            if (process.HasExited)
            {
                throw new Xunit.Sdk.XunitException(
                    $"Host exited with code {process.ExitCode}.{Environment.NewLine}{Snapshot(output)}");
            }

            try
            {
                return await client.GetAsync("/api/launcher/v1/bootstrap", cancellationToken);
            }
            catch (HttpRequestException)
            {
                await Task.Delay(25, cancellationToken);
            }
        }
    }

    private static async Task WaitForTcpListenerAsync(
        Process process,
        int port,
        StringBuilder output,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            if (process.HasExited)
            {
                throw new Xunit.Sdk.XunitException(
                    $"Host exited with code {process.ExitCode}.{Environment.NewLine}{Snapshot(output)}");
            }

            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port, cancellationToken);
                return;
            }
            catch (SocketException)
            {
                await Task.Delay(20, cancellationToken);
            }
        }
    }

    private static async Task<bool> CanConnectAsync(int port)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
            return true;
        }
        catch (Exception exception) when (exception is SocketException or OperationCanceledException)
        {
            return false;
        }
    }

    private static async Task StopAsync(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
    }

    private static void Append(StringBuilder output, string? line)
    {
        if (line is null) return;
        lock (output) output.AppendLine(line);
    }

    private static string Snapshot(StringBuilder output)
    {
        lock (output) return output.ToString();
    }

    private static int GetAvailablePortExcept(params int[] excluded)
    {
        while (true)
        {
            var port = GetAvailablePort();
            if (!excluded.Contains(port)) return port;
        }
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
