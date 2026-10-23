using System.Collections.Concurrent;
using System.Diagnostics;
using Dfo.Server.Abstractions.Execution;

namespace Dfo.Server.Application.Tests.Execution;

/// <summary>
/// Collects the violations reported on the current flow. A job does not inherit the test's flow, so a
/// job body attaches the queue itself.
/// </summary>
internal static class ViolationCapture
{
    private static readonly AsyncLocal<ConcurrentQueue<GameplayAssertionViolation>?> Current = new();
    private static readonly IDisposable Subscription = DiagnosticListener.AllListeners.Subscribe(new Sources());

    public static ConcurrentQueue<GameplayAssertionViolation> Attach(ConcurrentQueue<GameplayAssertionViolation>? into = null)
    {
        _ = Subscription;
        into ??= new ConcurrentQueue<GameplayAssertionViolation>();
        Current.Value = into;
        return into;
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
            if (value.Value is GameplayAssertionViolation violation) Current.Value?.Enqueue(violation);
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }
    }
}
