using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Transactions;

namespace Dfo.Server.Application.Users;

/// <summary>
/// The connected users, the account each has authenticated as, and the session each account has
/// active — one state, one lock.
/// </summary>
/// <remarks>
/// The three collections are separate readings of one invariant rather than three independent
/// tables: an account has at most one active character, and that character's session belongs to a
/// live connection. Several operations only preserve it while they update more than one collection
/// together — disconnecting removes the connection and the account's active session, and
/// publishing reads the online user before writing the active entry. Giving each responsibility
/// its own lock would open a window in which an account is bound to a connection that is already
/// gone, so the responsibilities are separated into their own service types while the state they
/// share stays behind this single lock.
/// <para>
/// Which character an account is playing is answered from the online user's own snapshot, where
/// publishing binds it. This table used to keep a second copy of the character's whole standing
/// plus a character-to-account reverse index; both were written on every publication and read by
/// nothing outside the access seam that has since been retired.
/// </para>
/// </remarks>
internal sealed class OnlineStateRegistry(IDomainTransactionAccessor transactions)
{
    private readonly object sync = new();
    private readonly Dictionary<OnlineUserId, OnlineUser> byId = [];
    private readonly Dictionary<NetworkConnectionId, OnlineUserId> byConnection = [];
    private readonly Dictionary<AccountId, ActiveCharacterRegistration> activeByAccount = [];
    private readonly Dictionary<GameSessionIdentity, SessionEndpoint> endpoints = [];

    public void PrepareAuthentication(OnlineUserId onlineUserId, AccountId accountId)
    {
        var transaction = transactions.Current
            ?? throw new InvalidOperationException(
                "Online authentication requires an active domain transaction.");
        var participant = transaction.Enlist(
            new AuthenticationParticipantIdentity(this, onlineUserId),
            () => new AuthenticationParticipant(this, onlineUserId, accountId));
        participant.RequireAccount(accountId);
    }

    public void Authenticate(OnlineUserId onlineUserId, AccountId accountId)
    {
        lock (sync)
        {
            GetRequiredAggregate(onlineUserId).Authenticate(accountId);
        }
    }

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

            if (onlineUser.AccountId is { } accountId
                && activeByAccount.TryGetValue(accountId, out var active)
                && active.OnlineUserId == onlineUserId)
            {
                RemoveActive(accountId);
            }

            endpoints.Remove(onlineUser.Snapshot.SessionIdentity);
            byConnection.Remove(connectionId);
            byId.Remove(onlineUserId);
            return true;
        }
    }

    public void Publish(SelectedSessionIdentity session)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (sync)
        {
            var owner = byId.Values.SingleOrDefault(value =>
                value.AccountId == session.AccountId
                && value.Connection.ConnectionId == session.ConnectionId
                && value.Generation == session.ConnectionGeneration)
                ?? throw new InvalidOperationException("Selected connection generation is no longer current.");
            owner.BindCharacter(session.CharacterId);
            // Binding increments the online snapshot generation, while selected-session
            // ownership remains the immutable generation validated before publication.
            SetActive(session.AccountId, new ActiveCharacterRegistration(owner.Id, session));
        }
    }

    public bool ClearExact(SelectedSessionIdentity session)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (sync)
        {
            if (!activeByAccount.TryGetValue(session.AccountId, out var active)
                || active.Session != session)
                return false;
            if (byId.TryGetValue(active.OnlineUserId, out var owner)
                && owner.Connection.ConnectionId == session.ConnectionId)
                owner.UnbindCharacter(session.CharacterId);
            return RemoveActive(session.AccountId);
        }
    }

    public bool TryDisconnectConnection(NetworkConnectionId connectionId)
    {
        lock (sync)
        {
            if (!byConnection.TryGetValue(connectionId, out var id)
                || !byId.TryGetValue(id, out var user)) return false;
            if (user.AccountId is { } accountId
                && activeByAccount.TryGetValue(accountId, out var active)
                && active.OnlineUserId == id) RemoveActive(accountId);
            endpoints.Remove(user.Snapshot.SessionIdentity);
            byConnection.Remove(connectionId);
            byId.Remove(id);
            return true;
        }
    }

    public OnlineUserSnapshot GetRequiredUser(OnlineUserId onlineUserId)
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

    public bool TryObserveEndpoint(GameSessionIdentity session, SessionEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsValid)
            return false;

        lock (sync)
        {
            if (!byId.TryGetValue(session.OnlineUserId, out var onlineUser)
                || onlineUser.Connection.ConnectionId != session.ConnectionId
                || onlineUser.AccountId is null
                || !byConnection.TryGetValue(session.ConnectionId, out var currentId)
                || currentId != session.OnlineUserId)
            {
                return false;
            }

            endpoints[session] = endpoint;
            return true;
        }
    }

    public SessionEndpoint? FindEndpoint(GameSessionIdentity session)
    {
        lock (sync)
        {
            if (!byId.TryGetValue(session.OnlineUserId, out var onlineUser)
                || onlineUser.Connection.ConnectionId != session.ConnectionId
                || !byConnection.TryGetValue(session.ConnectionId, out var currentId)
                || currentId != session.OnlineUserId)
            {
                return null;
            }

            return endpoints.GetValueOrDefault(session);
        }
    }

    private void SetActive(AccountId accountId, ActiveCharacterRegistration registration) =>
        activeByAccount[accountId] = registration;

    private bool RemoveActive(AccountId accountId) => activeByAccount.Remove(accountId);

    private OnlineUser GetRequiredAggregate(OnlineUserId onlineUserId) =>
        byId.TryGetValue(onlineUserId, out var onlineUser)
            ? onlineUser
            : throw new KeyNotFoundException($"Online user '{onlineUserId}' is not registered.");

    private sealed record AuthenticationParticipantIdentity(
        OnlineStateRegistry Registry,
        OnlineUserId OnlineUserId);

    private sealed class AuthenticationParticipant(
        OnlineStateRegistry owner,
        OnlineUserId onlineUserId,
        AccountId accountId) : ITransactionParticipant
    {
        private NetworkConnectionId? preparedConnectionId;
        private long preparedGeneration;

        public bool CommitCanFail => false;

        public void RequireAccount(AccountId expected)
        {
            if (accountId != expected)
            {
                throw new InvalidOperationException(
                    "One transaction cannot authenticate one online user as two accounts.");
            }
        }

        public ValueTask PrepareAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (owner.sync)
            {
                if (!owner.byId.TryGetValue(onlineUserId, out var user)
                    || user.AccountId is not null
                    || user.CurrentCharacterId is not null
                    || user.State != OnlineUserState.Connected
                    || !owner.byConnection.TryGetValue(
                        user.Connection.ConnectionId,
                        out var connectedId)
                    || connectedId != onlineUserId)
                {
                    throw new InvalidOperationException(
                        "Game login authentication target is unavailable.");
                }

                this.preparedConnectionId = user.Connection.ConnectionId;
                this.preparedGeneration = user.Generation;
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask CommitAsync(CancellationToken cancellationToken)
        {
            lock (owner.sync)
            {
                if (this.preparedConnectionId is { } connectionId
                    && owner.byId.TryGetValue(onlineUserId, out var user)
                    && user.Connection.ConnectionId == connectionId
                    && user.Generation == this.preparedGeneration
                    && user.AccountId is null
                    && user.CurrentCharacterId is null
                    && user.State == OnlineUserState.Connected
                    && owner.byConnection.TryGetValue(connectionId, out var connectedId)
                    && connectedId == onlineUserId)
                {
                    user.Authenticate(accountId);
                }
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask RollbackAsync(CancellationToken cancellationToken)
        {
            this.preparedConnectionId = null;
            this.preparedGeneration = 0;
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Which character an account is playing, and under which session.
    /// </summary>
    /// <remarks>
    /// The identity, not the character.s standing. The readers want the id, to answer which account
    /// a character is on and to release the binding when the session ends; a character.s standing
    /// is read from the session.s own runtime state.
    /// </remarks>
    private sealed record ActiveCharacterRegistration(
        OnlineUserId OnlineUserId,
        SelectedSessionIdentity? Session);

    private sealed class OnlineUser(OnlineUserId id, NetworkConnectionContext connection)
    {
        public OnlineUserId Id { get; } = id;

        public NetworkConnectionContext Connection { get; } = connection;

        public AccountId? AccountId { get; private set; }

        public CharacterId? CurrentCharacterId { get; private set; }

        public OnlineUserState State { get; private set; } = OnlineUserState.Connected;

        public long Generation { get; private set; } = 1;

        public OnlineUserSnapshot Snapshot => new(
            Id,
            Connection,
            AccountId,
            CurrentCharacterId,
            State,
            Generation);

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

        public void UnbindCharacter(CharacterId expected)
        {
            if (CurrentCharacterId != expected) return;
            CurrentCharacterId = null;
            State = AccountId is null ? OnlineUserState.Connected : OnlineUserState.Authenticated;
            Generation = checked(Generation + 1);
        }

        public void BindCharacter(CharacterId characterId)
        {
            if (AccountId is null)
            {
                throw new InvalidOperationException(
                    $"Online user '{Id}' must authenticate before selecting a character.");
            }

            if (CurrentCharacterId == characterId)
            {
                return;
            }

            CurrentCharacterId = characterId;
            State = OnlineUserState.CharacterActive;
            Generation++;
        }
    }
}
