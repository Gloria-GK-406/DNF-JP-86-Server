using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Sessions;

internal sealed class SelectedSessionRuntimeDirectory : ISelectedSessionRuntimeDirectory
{
    private readonly object sync = new();
    private readonly Dictionary<AccountId, Entry> entries = [];

    public void Publish(SelectedSessionIdentity session, bool shouldPersistPosition)
    {
        lock (sync) entries[session.AccountId] = new(session, shouldPersistPosition);
    }

    public bool IsPublishedExact(SelectedSessionIdentity session)
    {
        lock (sync)
            return entries.TryGetValue(session.AccountId, out var entry) && entry.Session == session;
    }

    public bool TryGetPositionPersistence(
        SelectedSessionIdentity session, out bool shouldPersistPosition)
    {
        lock (sync)
        {
            if (entries.TryGetValue(session.AccountId, out var entry) && entry.Session == session)
            {
                shouldPersistPosition = entry.ShouldPersistPosition;
                return true;
            }
            shouldPersistPosition = false;
            return false;
        }
    }

    public bool RemoveExact(SelectedSessionIdentity session)
    {
        lock (sync)
            return entries.TryGetValue(session.AccountId, out var entry)
                && entry.Session == session
                && entries.Remove(session.AccountId);
    }

    private sealed record Entry(
        SelectedSessionIdentity Session, bool ShouldPersistPosition);
}
