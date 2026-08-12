using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Sessions;

internal sealed class SelectedSessionDirectory : ISelectedSessionDirectory
{
    private readonly object sync = new();
    private readonly Dictionary<AccountId, SelectedSessionIdentity> sessions = [];

    public SelectedSessionIdentity? RegisterReplacing(SelectedSessionIdentity session)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (sync)
        {
            if (sessions.Values.Any(value => value.AccountId != session.AccountId
                    && (value.CharacterId == session.CharacterId
                        || value.WireUserId == session.WireUserId)))
            {
                throw new InvalidOperationException(
                    $"Character/wire identity {session.CharacterId}/{session.WireUserId} is already current.");
            }
            sessions.TryGetValue(session.AccountId, out var displaced);
            sessions[session.AccountId] = session;
            return displaced;
        }
    }

    public bool TryRemoveExact(SelectedSessionIdentity session)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (sync)
        {
            return sessions.TryGetValue(session.AccountId, out var current)
                && current == session
                && sessions.Remove(session.AccountId);
        }
    }

    public SelectedSessionIdentity? Find(AccountId accountId)
    {
        lock (sync) return sessions.GetValueOrDefault(accountId);
    }

    public SelectedSessionIdentity? FindByCharacter(CharacterId characterId)
    {
        lock (sync) return sessions.Values.SingleOrDefault(value => value.CharacterId == characterId);
    }

    public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId)
    {
        lock (sync) return sessions.Values.SingleOrDefault(value => value.WireUserId == wireUserId);
    }

    public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId)
    {
        lock (sync) return sessions.Values.SingleOrDefault(value => value.ConnectionId == connectionId);
    }

    public IReadOnlyList<SelectedSessionIdentity> Current()
    {
        lock (sync) return sessions.Values.OrderBy(value => value.WireUserId).ToArray();
    }
}
