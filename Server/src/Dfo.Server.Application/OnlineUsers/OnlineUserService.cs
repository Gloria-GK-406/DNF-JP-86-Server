using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;

namespace Dfo.Server.Application.OnlineUsers;

public sealed class OnlineUserService : IOnlineUserService
{
    private readonly object sync = new();
    private readonly Dictionary<OnlineUserId, OnlineUser> byId = [];
    private readonly Dictionary<NetworkConnectionId, OnlineUserId> byConnection = [];

    public OnlineUserSnapshot RegisterConnection(NetworkConnectionContext connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        lock (sync)
        {
            if (byConnection.TryGetValue(connection.ConnectionId, out var existingId))
            {
                return byId[existingId].Snapshot;
            }

            var onlineUser = new OnlineUser(OnlineUserId.New(), connection);
            byId.Add(onlineUser.Id, onlineUser);
            byConnection.Add(connection.ConnectionId, onlineUser.Id);
            return onlineUser.Snapshot;
        }
    }

    public OnlineUserSnapshot Authenticate(OnlineUserId onlineUserId, AccountId accountId)
    {
        lock (sync)
        {
            var onlineUser = GetRequiredAggregate(onlineUserId);
            onlineUser.Authenticate(accountId);
            return onlineUser.Snapshot;
        }
    }

    public bool TryDisconnect(
        OnlineUserId onlineUserId,
        NetworkConnectionId connectionId,
        long expectedGeneration)
    {
        lock (sync)
        {
            if (!byId.TryGetValue(onlineUserId, out var onlineUser)
                || onlineUser.Connection.ConnectionId != connectionId
                || onlineUser.Generation != expectedGeneration
                || !byConnection.TryGetValue(connectionId, out var currentId)
                || currentId != onlineUserId)
            {
                return false;
            }

            byConnection.Remove(connectionId);
            byId.Remove(onlineUserId);
            return true;
        }
    }

    public OnlineUserSnapshot GetRequired(OnlineUserId onlineUserId)
    {
        lock (sync)
        {
            return GetRequiredAggregate(onlineUserId).Snapshot;
        }
    }

    public bool TryGetByConnection(
        NetworkConnectionId connectionId,
        out OnlineUserSnapshot? onlineUser)
    {
        lock (sync)
        {
            if (byConnection.TryGetValue(connectionId, out var onlineUserId)
                && byId.TryGetValue(onlineUserId, out var aggregate))
            {
                onlineUser = aggregate.Snapshot;
                return true;
            }

            onlineUser = null;
            return false;
        }
    }

    private OnlineUser GetRequiredAggregate(OnlineUserId onlineUserId) =>
        byId.TryGetValue(onlineUserId, out var onlineUser)
            ? onlineUser
            : throw new KeyNotFoundException($"Online user '{onlineUserId}' is not registered.");

    private sealed class OnlineUser(OnlineUserId id, NetworkConnectionContext connection)
    {
        public OnlineUserId Id { get; } = id;

        public NetworkConnectionContext Connection { get; } = connection;

        public AccountId? AccountId { get; private set; }

        public OnlineUserState State { get; private set; } = OnlineUserState.Connected;

        public long Generation { get; private set; } = 1;

        public OnlineUserSnapshot Snapshot => new(Id, Connection, AccountId, State, Generation);

        public void Authenticate(AccountId accountId)
        {
            if (AccountId is { } existing && existing != accountId)
            {
                throw new InvalidOperationException(
                    $"Online user '{Id}' is already authenticated as account '{existing}'.");
            }

            if (AccountId == accountId)
            {
                return;
            }

            AccountId = accountId;
            State = OnlineUserState.Authenticated;
            Generation++;
        }
    }
}
