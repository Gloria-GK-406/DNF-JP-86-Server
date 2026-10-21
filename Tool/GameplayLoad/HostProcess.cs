using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GameplayLoad;

/// <summary>One CommandLoggingBehavior line, stamped with the tool's clock when the line was read.</summary>
internal sealed record CommandRecord(long ReadTimestamp, string CommandType, string HandlerType, string Kind, double ElapsedMs, bool Failed);

/// <summary>
/// A Release Host started as a child process, the way Server/tests/Dfo.Server.Host.Tests/EndToEndHostHarness.cs
/// starts it: private temp directory and database, networking on, settings through environment variables.
/// </summary>
/// <remarks>
/// Logging: appsettings.json already sets Default=Information, which is what CommandLoggingBehavior logs at.
/// The only logging override is Console:FormatterOptions:SingleLine=true so each command line can be parsed
/// on its own; nothing in Server/src is changed.
/// </remarks>
internal sealed partial class HostProcess : IAsyncDisposable
{
    private readonly Process process;
    private readonly StreamWriter log;
    private readonly HttpClient http;
    private readonly ConcurrentQueue<CommandRecord> commands = new();
    private long logLines;

    private HostProcess(Process process, StreamWriter log, string directory, int gamePort, int httpPort, long started)
    {
        this.process = process;
        this.log = log;
        Directory = directory;
        GamePort = gamePort;
        StartedTimestamp = started;
        http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{httpPort}"), Timeout = TimeSpan.FromSeconds(60) };
    }

    public string Directory { get; }

    public string DatabasePath => Path.Combine(Directory, "load.db");

    public int GamePort { get; }

    public long StartedTimestamp { get; }

    public long ReadyTimestamp { get; private set; }

    public int ProcessId => process.Id;

    public IReadOnlyCollection<CommandRecord> Commands => commands;

    public static async Task<HostProcess> StartAsync(string hostDll, string pvf, string logPath, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Path.GetTempPath(), "dfo-gameplay-load-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        var ports = AvailablePorts(5);
        var (channelPort, gamePortA, gamePortB, gamePortC, httpPort) = (ports[0], ports[1], ports[2], ports[3], ports[4]);
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(hostDll);
        var environment = new Dictionary<string, string>
        {
            ["Pvf__ArchivePath"] = pvf,
            ["Database__Path"] = Path.Combine(directory, "load.db"),
            ["Networking__Enabled"] = "true",
            ["Networking__UdpDiscoveryEnabled"] = "false",
            ["Networking__ChannelPort"] = channelPort.ToString(CultureInfo.InvariantCulture),
            ["Networking__GamePorts__0"] = gamePortA.ToString(CultureInfo.InvariantCulture),
            ["Networking__GamePorts__1"] = gamePortB.ToString(CultureInfo.InvariantCulture),
            // appsettings.json lists three game listeners; all three move to free ports.
            ["Networking__GamePorts__2"] = gamePortC.ToString(CultureInfo.InvariantCulture),
            ["Channel__UseScriptCatalog"] = "false",
            ["Channel__PublicServerIp"] = "127.0.0.1",
            ["Channel__Channels__0__Host"] = "127.0.0.1",
            ["Channel__Channels__0__Port"] = gamePortA.ToString(CultureInfo.InvariantCulture),
            ["Channel__Channels__1__Host"] = "127.0.0.1",
            ["Channel__Channels__1__Port"] = gamePortB.ToString(CultureInfo.InvariantCulture),
            ["Channel__Channels__2__Host"] = "127.0.0.1",
            ["Channel__Channels__2__Port"] = gamePortC.ToString(CultureInfo.InvariantCulture),
            ["LauncherAuth__Enabled"] = "true",
            ["LauncherAuth__ListenUrl"] = $"http://127.0.0.1:{httpPort}",
            ["LauncherAuth__PublicChannel__Host"] = "127.0.0.1",
            ["LauncherAuth__PublicChannel__Port"] = channelPort.ToString(CultureInfo.InvariantCulture),
            ["GmWeb__Enabled"] = "false",
            ["Logging__Console__FormatterOptions__SingleLine"] = "true",
        };
        foreach (var (key, value) in environment) startInfo.Environment[key] = value;

        var writer = new StreamWriter(logPath) { AutoFlush = false };
        var started = Stopwatch.GetTimestamp();
        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("The Host process could not be started.");
        // The channel the load plays on is #ch.11 (appsettings Channels[1]), as WireSmoke does.
        var host = new HostProcess(process, writer, directory, gamePortB, httpPort, started);
        process.OutputDataReceived += (_, line) => host.OnLine(line.Data);
        process.ErrorDataReceived += (_, line) => host.OnLine(line.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await host.WaitForBootstrapAsync(cancellationToken);
            return host;
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    public long LogLines => Interlocked.Read(ref logLines);

    private void OnLine(string? line)
    {
        if (line is null) return;
        var stamp = Stopwatch.GetTimestamp();
        Interlocked.Increment(ref logLines);
        lock (log) log.WriteLine(line);
        var match = CommandLine().Match(line);
        if (!match.Success) return;
        commands.Enqueue(new CommandRecord(
            stamp,
            match.Groups["command"].Value,
            match.Groups["handler"].Value,
            match.Groups["kind"].Value,
            double.Parse(match.Groups["ms"].Value, CultureInfo.InvariantCulture),
            match.Groups["verb"].Value == "failed in handler"));
    }

    /// <summary>CommandLoggingBehavior's success and failure templates (Server/src/Dfo.Server.Application/Commands/Pipeline).</summary>
    [GeneratedRegex(@"Command (?<command>\S+) (?<verb>handled by|failed in handler) (?<handler>\S+) as (?<kind>\S+) with execution \S+ (?:in|after) (?<ms>[0-9.]+) ms")]
    private static partial Regex CommandLine();

    private async Task WaitForBootstrapAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(120));
        while (true)
        {
            if (process.HasExited) throw new InvalidOperationException($"Host exited with code {process.ExitCode}; see the host log.");
            try
            {
                using var response = await http.GetAsync("/api/launcher/v1/bootstrap", timeout.Token);
                if (response.StatusCode == HttpStatusCode.OK) break;
            }
            catch (HttpRequestException)
            {
                // Kestrel is still binding.
            }

            await Task.Delay(10, timeout.Token);
        }

        // The launcher endpoint and the game listener start in the same host; also wait for the game port.
        while (true)
        {
            try
            {
                using var probe = new TcpClient();
                await probe.ConnectAsync(IPAddress.Loopback, GamePort, timeout.Token);
                break;
            }
            catch (SocketException)
            {
                await Task.Delay(10, timeout.Token);
            }
        }

        ReadyTimestamp = Stopwatch.GetTimestamp();
    }

    /// <summary>Registers an account and redeems a launcher ticket (both hash the password server-side).</summary>
    public async Task<(string MId, string Ticket, double RegisterMs, double LoginMs)> RegisterAndLoginAsync(
        string loginId,
        CancellationToken cancellationToken)
    {
        var credentials = new { loginId, password = "gameplay load password" };
        var start = Stopwatch.GetTimestamp();
        using (var registration = await http.PostAsJsonAsync("/api/launcher/v1/register", credentials, cancellationToken))
        {
            if (registration.StatusCode != HttpStatusCode.Created)
                throw new InvalidOperationException($"register {loginId}: {(int)registration.StatusCode}");
        }

        var registered = Stopwatch.GetTimestamp();
        using var login = await http.PostAsJsonAsync("/api/launcher/v1/login", credentials, cancellationToken);
        if (login.StatusCode != HttpStatusCode.OK) throw new InvalidOperationException($"launcher login {loginId}: {(int)login.StatusCode}");
        using var json = JsonDocument.Parse(await login.Content.ReadAsStreamAsync(cancellationToken));
        var done = Stopwatch.GetTimestamp();
        return (json.RootElement.GetProperty("mId").GetString()!,
            json.RootElement.GetProperty("ticket").GetString()!,
            Stopwatch.GetElapsedTime(start, registered).TotalMilliseconds,
            Stopwatch.GetElapsedTime(registered, done).TotalMilliseconds);
    }

    /// <summary>A fresh launcher ticket for an existing account (verifies the password hash server-side).</summary>
    public async Task<(string MId, string Ticket)> LoginAsync(string loginId, CancellationToken cancellationToken)
    {
        var credentials = new { loginId, password = "gameplay load password" };
        using var login = await http.PostAsJsonAsync("/api/launcher/v1/login", credentials, cancellationToken);
        if (login.StatusCode != HttpStatusCode.OK) throw new InvalidOperationException($"launcher login {loginId}: {(int)login.StatusCode}");
        using var json = JsonDocument.Parse(await login.Content.ReadAsStreamAsync(cancellationToken));
        return (json.RootElement.GetProperty("mId").GetString()!, json.RootElement.GetProperty("ticket").GetString()!);
    }

    public async ValueTask DisposeAsync()
    {
        http.Dispose();
        if (!process.HasExited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            await process.WaitForExitAsync();
        }

        process.Dispose();
        lock (log) log.Dispose();
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static int[] AvailablePorts(int count)
    {
        var ports = new List<int>(count);
        while (ports.Count < count)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            if (!ports.Contains(port)) ports.Add(port);
        }

        return [.. ports];
    }
}
