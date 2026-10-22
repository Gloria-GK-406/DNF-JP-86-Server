using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Dfo.Server.Abstractions.Execution;

namespace Dfo.Server.Testing;

/// <summary>
/// Evidence for "zero gate-assertion violations in one full run": when <c>DFO_GAMEPLAY_ASSERTIONS</c>
/// names a directory, every violation reported by <see cref="GameplayGateAssertion"/> is appended to
/// <c>&lt;directory&gt;/&lt;assembly&gt;.jsonl</c> with its <c>Dfo.</c> frames. A green run alone is no
/// proof: a production <c>catch</c> or a test's <c>ThrowsAny</c> can swallow an off-executor violation.
/// </summary>
internal static class GameplayAssertionRecorder
{
    private static readonly object Sync = new();
    private static string? path;

    // CA2255: a module initializer is the only hook that runs before any test in every linked test
    // assembly without each project adding a fixture; it only subscribes and does nothing when unset.
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Install()
    {
        var directory = Environment.GetEnvironmentVariable("DFO_GAMEPLAY_ASSERTIONS");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, (Assembly.GetExecutingAssembly().GetName().Name ?? "tests") + ".jsonl");
        DiagnosticListener.AllListeners.Subscribe(new Sources());
    }

    private static void Record(GameplayAssertionViolation violation)
    {
        var frames = Environment.StackTrace
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("at Dfo.", StringComparison.Ordinal))
            .Select(line => line[3..])
            .ToArray();
        var line = JsonSerializer.Serialize(new { kind = violation.Kind, operation = violation.Operation, frames });
        lock (Sync) File.AppendAllText(path!, line + Environment.NewLine);
    }

    private sealed class Sources : IObserver<DiagnosticListener>
    {
        public void OnNext(DiagnosticListener value)
        {
            if (value.Name == GameplayGateAssertion.DiagnosticSourceName) value.Subscribe(new Events());
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }
    }

    private sealed class Events : IObserver<KeyValuePair<string, object?>>
    {
        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Value is GameplayAssertionViolation violation) Record(violation);
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }
    }
}
