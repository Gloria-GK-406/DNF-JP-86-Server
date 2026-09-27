using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Dfo.Server.Host.Tests;

public sealed class ChannelHostStartupTests
{
    [Fact]
    public async Task PackagedHost_FromUnrelatedWorkingDirectory_CompletesChannelBootstrap()
    {
        var workingDirectory = Path.Combine(
            Path.GetTempPath(), $"dfo-host-cwd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workingDirectory);
        var channelPort = GetAvailablePort();
        var gamePorts = GetAvailablePortsExcept(channelPort, count: 2);
        var output = new StringBuilder();
        using var process = StartHost(workingDirectory, channelPort, gamePorts, output);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await WaitForListenerAsync(process, channelPort, output, timeout.Token);
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, channelPort, timeout.Token);
            var stream = client.GetStream();

            await SendRequestAsync(stream, 0x0B, timeout.Token);
            var connect = await ReadFrameAsync(stream, timeout.Token);
            Assert.Equal(0x0C, connect.MessageNumber);
            var key = Encoding.ASCII.GetString(connect.Body, 4, 16).TrimEnd('\0');

            await SendRequestAsync(stream, 0x09, timeout.Token);
            var script = await ReadFrameAsync(stream, timeout.Token);
            Assert.Equal(0x0A, script.MessageNumber);
            var scriptPlaintext = Decrypt(script.Body, key);
            Assert.Contains((byte)'[', scriptPlaintext);

            await SendRequestAsync(stream, 0x11, timeout.Token);
            var channelInfo = await ReadFrameAsync(stream, timeout.Token);
            Assert.Equal(0x12, channelInfo.MessageNumber);
            var infoPlaintext = Decrypt(channelInfo.Body, key);
            // Both shipped channels, each on the port this test gave it. Forty-eight bytes a
            // record, the port sixteen from its end.
            Assert.Equal(
                gamePorts.Count, BinaryPrimitives.ReadInt32LittleEndian(infoPlaintext.AsSpan(2, 4)));
            for (var index = 0; index < gamePorts.Count; index++)
            {
                Assert.Equal(
                    gamePorts[index],
                    BinaryPrimitives.ReadInt32LittleEndian(
                        infoPlaintext.AsSpan(50 + (index * 48), 4)));
            }

            var startupOutput = Snapshot(output);
            Assert.Contains($"ContentRoot={Path.GetDirectoryName(typeof(Program).Assembly.Location)}", startupOutput);
            Assert.Contains("ChannelScript=", startupOutput);
            // ChannelSourceOptions resolves the configured path to an absolute path whose
            // separators follow the OS, so match the resolved tail instead of a literal forward-slash path.
            Assert.Contains(
                $"Resources{Path.DirectorySeparatorChar}Channel{Path.DirectorySeparatorChar}channel_info.etc",
                startupOutput);
            Assert.Contains("ChannelAddress=127.0.0.1", startupOutput);
            Assert.Contains("GameAddress=127.0.0.1", startupOutput);
            Assert.Contains($"ChannelPort={channelPort}", startupOutput);
            Assert.Contains($"GamePorts={string.Join(",", gamePorts)}", startupOutput);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            await TestDirectory.DeleteAsync(workingDirectory);
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("unreadable")]
    public async Task InvalidRequiredChannelScript_FailsBeforeListenersStart(string kind)
    {
        var workingDirectory = Path.Combine(
            Path.GetTempPath(), $"dfo-host-invalid-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workingDirectory);
        var scriptPath = Path.Combine(workingDirectory, "required-channel-script.etc");
        if (kind == "empty")
        {
            await File.WriteAllBytesAsync(scriptPath, []);
        }
        else if (kind == "unreadable")
        {
            Directory.CreateDirectory(scriptPath);
        }

        var channelPort = GetAvailablePort();
        var gamePorts = GetAvailablePortsExcept(channelPort, count: 2);
        var output = new StringBuilder();
        using var process = StartHost(
            workingDirectory, channelPort, gamePorts, output, scriptPath);
        try
        {
            // Generous on purpose, and the budget is not what this test is about. The refusal is
            // a hosted service, so what is being waited on is not "read a path and exit" but a
            // whole host cold-starting: the container, the 70MB archive the content sources open,
            // the database file -- and only then the refusal. Alone that is a couple of seconds;
            // under the seven assemblies this suite runs at once it went past ten and then past
            // thirty, and each time a passing baseline read as a regression.
            //
            // Nothing is masked by the larger budget. What this test proves is that the host
            // exited non-zero and that nothing is listening; a host that wrongly started would
            // never exit and would still fail here, only later. The deadline exists so that a
            // hang ends the suite instead of holding it forever, and on hitting it this reports
            // what the host had said by then rather than a bare cancellation.
            var waited = Stopwatch.StartNew();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                throw new Xunit.Sdk.XunitException(
                    $"The host was still running after {waited.Elapsed}. Its output so far:"
                        + Environment.NewLine + Snapshot(output));
            }
            Assert.NotEqual(0, process.ExitCode);
            var startupOutput = Snapshot(output);
            Assert.Contains(scriptPath, startupOutput);
            Assert.Contains("Required channel script", startupOutput);

            using var probe = new TcpClient();
            await Assert.ThrowsAnyAsync<SocketException>(async () =>
                await probe.ConnectAsync(IPAddress.Loopback, channelPort));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            await TestDirectory.DeleteAsync(workingDirectory);
        }
    }

    /// <summary>
    /// The packaged host, on ports this test owns.
    /// </summary>
    /// <remarks>
    /// Every configured game port is overridden, not just the first. The shipped configuration
    /// lists two channels so that the archive's channel-to-dungeon reward can be seen paying and
    /// not paying; leaving the second at its shipped port would have this test bind 10011 on a
    /// developer machine, where a real server is often already listening on it.
    /// </remarks>
    private static Process StartHost(
        string workingDirectory,
        int channelPort,
        IReadOnlyList<int> gamePorts,
        StringBuilder output,
        string? scriptPath = null)
    {
        var assembly = typeof(Program).Assembly.Location;
        var startInfo = new ProcessStartInfo("dotnet", $"\"{assembly}\"")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.Environment["Networking__Enabled"] = "true";
        startInfo.Environment["Networking__UdpDiscoveryEnabled"] = "false";
        startInfo.Environment["Networking__ChannelPort"] = channelPort.ToString();
        for (var index = 0; index < gamePorts.Count; index++)
        {
            startInfo.Environment[$"Networking__GamePorts__{index}"] =
                gamePorts[index].ToString();
            startInfo.Environment[$"Channel__Channels__{index}__Port"] =
                gamePorts[index].ToString();
        }

        startInfo.Environment["Channel__PublicServerIp"] = "127.0.0.1";
        startInfo.Environment["Game__EndpointHost"] = "127.0.0.1";
        startInfo.Environment["Database__Path"] = Path.Combine(workingDirectory, "host.db");
        if (scriptPath is not null)
        {
            startInfo.Environment["Channel__ScriptPath"] = scriptPath;
        }

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The Host process could not be started.");
        process.OutputDataReceived += (_, args) => Append(output, args.Data);
        process.ErrorDataReceived += (_, args) => Append(output, args.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
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

    private static async Task WaitForListenerAsync(
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
                    $"Host exited with code {process.ExitCode}.\n{output}");
            }

            try
            {
                using var probe = new TcpClient();
                await probe.ConnectAsync(IPAddress.Loopback, port, cancellationToken);
                return;
            }
            catch (SocketException)
            {
                await Task.Delay(20, cancellationToken);
            }
        }
    }

    private static async Task SendRequestAsync(
        NetworkStream stream,
        byte messageNumber,
        CancellationToken cancellationToken)
    {
        var frame = new byte[11];
        frame[0] = 0x7C;
        frame[1] = messageNumber;
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(2, 4), (uint)frame.Length);
        frame[10] = 1;
        await stream.WriteAsync(frame, cancellationToken);
    }

    private static async Task<(byte MessageNumber, byte[] Body)> ReadFrameAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var header = new byte[11];
        await stream.ReadExactlyAsync(header, cancellationToken);
        var totalLength = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(2, 4));
        var body = new byte[checked((int)totalLength - header.Length)];
        await stream.ReadExactlyAsync(body, cancellationToken);
        return (header[1], body);
    }

    private static byte[] Decrypt(byte[] body, string key)
    {
        using var input = new MemoryStream(body);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var compressed = new MemoryStream();
        zlib.CopyTo(compressed);
        using var aes = Aes.Create();
        aes.Key = Encoding.ASCII.GetBytes(key.PadRight(16, '\0'));
        return aes.DecryptEcb(compressed.ToArray(), PaddingMode.None);
    }

    private static IReadOnlyList<int> GetAvailablePortsExcept(int excluded, int count)
    {
        var ports = new List<int>(count);
        while (ports.Count < count)
        {
            var port = GetAvailablePort();
            if (port != excluded && !ports.Contains(port))
                ports.Add(port);
        }

        return ports;
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
