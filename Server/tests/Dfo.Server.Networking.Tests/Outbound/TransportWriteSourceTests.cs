using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace Dfo.Server.Networking.Tests.Outbound;

/// <summary>
/// F-63 / RQ-003: the production sources write to a transport in exactly two places, each listed with how
/// many lines do it and why. Every other write goes through the outbox and the outbound sender's loops.
/// </summary>
/// <remarks>
/// The scan reads raw text and looks for the shapes a transport write takes here: a cast to TouchSocket's
/// <c>ISender</c>, a send on something named session, a <c>ReadOnlySequence</c> send, and a UDP
/// <c>SendToAsync</c>. A send through the gameplay sender interfaces (<c>sender.SendAsync</c>) is a
/// registration, not a write, and does not match.
/// </remarks>
public sealed class TransportWriteSourceTests(ITestOutputHelper output)
{
    private static readonly (string Name, Regex Pattern)[] Writes =
    [
        ("cast to ISender", new Regex(@"\(\s*ISender\s*\)|\bas ISender\b|\bis ISender\b", RegexOptions.CultureInvariant)),
        ("send on a session", new Regex(@"\b[Ss]ession\)?\s*\.SendAsync\(", RegexOptions.CultureInvariant)),
        ("ReadOnlySequence send", new Regex(@"\.SendAsync\(\s*new ReadOnlySequence", RegexOptions.CultureInvariant)),
        ("UDP SendToAsync", new Regex(@"\.SendToAsync\(", RegexOptions.CultureInvariant)),
    ];

    /// <summary>Each file that may write to a transport, how many lines do, and why.</summary>
    private static readonly IReadOnlyDictionary<string, (int Count, string Reason)> Allowed =
        new Dictionary<string, (int Count, string Reason)>(StringComparer.Ordinal)
    {
        ["Dfo.Server.Networking/Outbound/OutboundTransport.cs"] =
            (1, "the one TCP write point, called only from the outbound sender's detached delivery loop; refuses a job (RQ-003)"),
        ["Dfo.Server.Networking/Runtime/A21UdpDiscoveryService.cs"] =
            (1, "UDP discovery reply from its BackgroundService receive loop, off the executor, not a client connection (appendix B)"),
    };

    [Fact]
    public void Only_the_listed_files_write_to_a_transport()
    {
        var files = ProductionSources();
        output.WriteLine($"scanned source files: {files.Count}");
        foreach (var hit in Hits(files))
            output.WriteLine($"write point {hit.Path}:{hit.Line} ({hit.Shape}): "
                + (Allowed.TryGetValue(hit.Path, out var allowed) ? allowed.Reason : "NOT LISTED"));

        var violations = Violations(files);

        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    [Fact]
    public void A_session_send_in_any_other_file_is_named()
    {
        var files = Listed();
        files["Dfo.Server.Networking/Runtime/Somewhere.cs"] = "await connection.Session.SendAsync(frame, token);";
        files["Dfo.Server.Protocol/Game/Elsewhere.cs"] = "await ((ISender)client).SendAsync(bytes);";
        files["Dfo.Server.Protocol/Game/Fine.cs"] = "await sender.SendAsync(connectionId, message, token);";

        var violations = Violations(files);

        Assert.Equal(2, violations.Count);
        Assert.Contains(violations, violation => violation.StartsWith("Dfo.Server.Networking/Runtime/Somewhere.cs:", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.StartsWith("Dfo.Server.Protocol/Game/Elsewhere.cs:", StringComparison.Ordinal));
    }

    /// <summary>TASK-30 section 7.3: the two shapes the first counterexample does not feed outside the list.</summary>
    [Fact]
    public void A_sequence_send_and_a_udp_send_in_any_other_file_are_named()
    {
        var files = Listed();
        files["Dfo.Server.GmWeb/Somewhere.cs"] = "await client.SendAsync(new ReadOnlySequence<byte>(frame));";
        files["Dfo.Server.Host/Elsewhere.cs"] = "await socket.SendToAsync(reply, remote);";

        var violations = Violations(files);

        Assert.Equal(2, violations.Count);
        Assert.Contains(violations, violation => violation.StartsWith("Dfo.Server.GmWeb/Somewhere.cs:1 writes to a transport (ReadOnlySequence send)", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.StartsWith("Dfo.Server.Host/Elsewhere.cs:1 writes to a transport (UDP SendToAsync)", StringComparison.Ordinal));
    }

    [Fact]
    public void A_second_write_in_a_listed_file_and_a_listed_file_without_one_are_named()
    {
        var files = Listed();
        files["Dfo.Server.Networking/Outbound/OutboundTransport.cs"] += "\nawait responseSocket.SendToAsync(reply, remote);";
        files["Dfo.Server.Networking/Runtime/A21UdpDiscoveryService.cs"] = "class A21UdpDiscoveryService { }";

        var violations = Violations(files);

        Assert.Equal(2, violations.Count);
        Assert.Contains(violations, violation => violation.Contains("OutboundTransport.cs writes 2 times, pinned 1", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.Contains("A21UdpDiscoveryService.cs writes 0 times, pinned 1", StringComparison.Ordinal));
    }

    private static IReadOnlyList<(string Path, int Line, string Shape)> Hits(IReadOnlyDictionary<string, string> files)
    {
        var hits = new List<(string, int, string)>();
        foreach (var (path, text) in files.OrderBy(file => file.Key, StringComparer.Ordinal))
        {
            var lines = text.Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                var shape = Writes.FirstOrDefault(write => write.Pattern.IsMatch(lines[index]));
                if (shape.Pattern is not null) hits.Add((path, index + 1, shape.Name));
            }
        }
        return hits;
    }

    private static IReadOnlyList<string> Violations(IReadOnlyDictionary<string, string> files)
    {
        var hits = Hits(files);
        return hits.Where(hit => !Allowed.ContainsKey(hit.Path))
            .Select(hit => $"{hit.Path}:{hit.Line} writes to a transport ({hit.Shape}) and is not on the list")
            .Concat(Allowed.OrderBy(file => file.Key, StringComparer.Ordinal)
                .Select(file => (file.Key, Found: hits.Count(hit => hit.Path == file.Key), Pinned: file.Value.Count))
                .Where(file => file.Found != file.Pinned)
                .Select(file => $"{file.Key} writes {file.Found} times, pinned {file.Pinned}"))
            .ToList();
    }

    /// <summary>Every listed file, holding exactly its pinned number of writes.</summary>
    private static Dictionary<string, string> Listed() =>
        Allowed.ToDictionary(file => file.Key,
            file => string.Join("\n", Enumerable.Repeat("await ((ISender)session).SendAsync(frames, token);", file.Value.Count)),
            StringComparer.Ordinal);

    private static Dictionary<string, string> ProductionSources()
    {
        var source = Path.Combine(Repository(), "Server", "src");
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories))
        {
            var path = Path.GetRelativePath(source, file).Replace('\\', '/');
            if (path.Contains("/bin/", StringComparison.Ordinal) || path.Contains("/obj/", StringComparison.Ordinal))
                continue;
            files[path] = File.ReadAllText(file);
        }
        return files;
    }

    private static string Repository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, "Server", "src"))) return directory.FullName;
        throw new DirectoryNotFoundException("The transport write source test requires the repository checkout.");
    }
}
