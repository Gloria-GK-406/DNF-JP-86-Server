using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

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
internal sealed class OnlineStateStore
{
    private readonly object sync = new();
    private readonly Dictionary<OnlineUserId, OnlineUser> byId = [];
    private readonly Dictionary<NetworkConnectionId, OnlineUserId> byConnection = [];
    private readonly Dictionary<AccountId, ActiveCharacterRegistration> activeByAccount = [];

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

    private void SetActive(AccountId accountId, ActiveCharacterRegistration registration) =>
        activeByAccount[accountId] = registration;

    private bool RemoveActive(AccountId accountId) => activeByAccount.Remove(accountId);

    private OnlineUser GetRequiredAggregate(OnlineUserId onlineUserId) =>
        byId.TryGetValue(onlineUserId, out var onlineUser)
            ? onlineUser
            : throw new KeyNotFoundException($"Online user '{onlineUserId}' is not registered.");

    /// <summary>
    /// Which character an account is playing, and under which session.
    /// </summary>
    /// <remarks>
    /// The identity, not the character's standing. This record used to carry a whole
    /// <c>ActiveCharacterState</c> that nothing ever read back: the only readers wanted the id, to
    /// answer which account a character is on and to release the binding when the session ends.
    /// A character's standing is read from the session's own runtime state.
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
