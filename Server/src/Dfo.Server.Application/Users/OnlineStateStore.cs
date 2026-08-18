using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Users;

/// <summary>
/// The connected users, the account each has authenticated as, and the character each account has
/// active — one state, one lock.
/// </summary>
/// <remarks>
/// The three collections are separate readings of one invariant rather than three independent
/// tables: an account has at most one active character, and that character's session belongs to a
/// live connection. Several operations only preserve it while they update more than one collection
/// together — disconnecting removes the connection and the account's active character, and
/// publishing reads the online user before writing the active-character entry. Giving each
/// responsibility its own lock would open a window in which an account is bound to a connection
/// that is already gone, so the responsibilities are separated into their own service types while
/// the state they share stays behind this single lock.
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
                activeByAccount.Remove(accountId);
            }

            byConnection.Remove(connectionId);
            byId.Remove(onlineUserId);
            return true;
        }
    }

    public void Publish(AccountId accountId, ActiveCharacterState character)
    {
        ArgumentNullException.ThrowIfNull(character);
        if (character.Identity.CharacterId.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(character),
                "An active character ID must be positive.");
        }

        lock (sync)
        {
            var matches = byId.Values
                .Where(user => user.AccountId == accountId)
                .Take(2)
                .ToArray();
            if (matches.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Expected one online user for account '{accountId}', but found '{matches.Length}'.");
            }

            var owner = matches[0];
            owner.BindCharacter(character.Identity.CharacterId);
            activeByAccount[accountId] = new ActiveCharacterRegistration(owner.Id, null, character);
        }
    }

    public void Publish(SelectedSessionIdentity session, ActiveCharacterState character)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(character);
        if (character.Identity.CharacterId != session.CharacterId)
            throw new InvalidDataException("Active character does not match the selected session.");
        lock (sync)
        {
            var owner = byId.Values.SingleOrDefault(value =>
                value.AccountId == session.AccountId
                && value.Connection.ConnectionId == session.ConnectionId
                && value.Generation == session.ConnectionGeneration)
                ?? throw new InvalidOperationException("Selected connection generation is no longer current.");
            owner.BindCharacter(character.Identity.CharacterId);
            // Binding increments the online snapshot generation, while selected-session
            // ownership remains the immutable generation validated before publication.
            activeByAccount[session.AccountId] = new ActiveCharacterRegistration(owner.Id, session, character);
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
            return activeByAccount.Remove(session.AccountId);
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
                && active.OnlineUserId == id) activeByAccount.Remove(accountId);
            byConnection.Remove(connectionId);
            byId.Remove(id);
            return true;
        }
    }

    public ActiveCharacterState GetRequiredCharacter(AccountId accountId)
    {
        lock (sync)
        {
            return activeByAccount.TryGetValue(accountId, out var active)
                ? active.Character
                : throw new KeyNotFoundException(
                    $"Account '{accountId}' does not have an active character.");
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

    private OnlineUser GetRequiredAggregate(OnlineUserId onlineUserId) =>
        byId.TryGetValue(onlineUserId, out var onlineUser)
            ? onlineUser
            : throw new KeyNotFoundException($"Online user '{onlineUserId}' is not registered.");

    private sealed record ActiveCharacterRegistration(
        OnlineUserId OnlineUserId,
        SelectedSessionIdentity? Session,
        ActiveCharacterState Character);

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
