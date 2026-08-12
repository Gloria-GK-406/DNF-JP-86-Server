using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Sessions;

internal sealed class SelectedSessionRuntimeDirectory : ISelectedSessionRuntimeDirectory
{
    private readonly object sync = new();
    private readonly Dictionary<AccountId, Entry> entries = [];

    public void Publish(
        SelectedSessionIdentity session, SelectCharacterResult result, bool shouldPersistPosition)
    {
        if (result.Character.Identity.CharacterId != session.CharacterId)
            throw new InvalidDataException("Selected runtime does not match the session character.");
        lock (sync) entries[session.AccountId] = new(session, result, shouldPersistPosition);
    }

    public void ReplaceExact(SelectedSessionIdentity session, SelectCharacterResult result)
    {
        lock (sync)
        {
            if (!entries.TryGetValue(session.AccountId, out var entry) || entry.Session != session)
                throw new InvalidOperationException("Selected runtime generation is no longer current.");
            entries[session.AccountId] = entry with { Result = result };
        }
    }

    public SelectCharacterResult GetRequired(SelectedSessionIdentity session)
    {
        lock (sync)
            return entries.TryGetValue(session.AccountId, out var entry) && entry.Session == session
                ? entry.Result
                : throw new KeyNotFoundException("Selected runtime generation is unavailable.");
    }

    public bool TryGet(SelectedSessionIdentity session, out SelectCharacterResult? result,
        out bool shouldPersistPosition)
    {
        lock (sync)
        {
            if (entries.TryGetValue(session.AccountId, out var entry) && entry.Session == session)
            {
                result = entry.Result;
                shouldPersistPosition = entry.ShouldPersistPosition;
                return true;
            }
            result = null;
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
        SelectedSessionIdentity Session, SelectCharacterResult Result, bool ShouldPersistPosition);
}
