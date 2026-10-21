using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Transactions;

namespace Dfo.Server.Application.Users;

/// <summary>
/// Who each connection is: the connected users, the account each has authenticated as, the session
/// each account has selected and how far it has got -- one state, one lock.
/// </summary>
/// <remarks>
/// These are readings of one identity rather than independent tables: an account has at most one
/// selected session, a session enters the world only through a live connection authenticated as
/// its account, and the character a connection is playing is the in-world session it owns. Each
/// used to be kept by its own service behind its own lock, and every caller that wanted to know
/// whether a session still counted asked three of them and compared the answers. Keeping one
/// record per selection here leaves nothing to compare: what a connection is playing, whether a
/// session has entered the world, whether it is fenced, and whether its connection has been
/// revoked under it are all read off the same record in the same moment.
/// <para>
/// A revoked connection is removed at once so it can do nothing more, while its selection stays
/// until the session's teardown releases what it holds elsewhere. That is a stage of the session,
/// not a disagreement between stores: <see cref="StandingOf"/> reports it.
/// </para>
/// </remarks>
internal sealed class OnlineStateRegistry(IDomainTransactionAccessor transactions)
{
    private readonly object sync = new();
    private readonly Dictionary<OnlineUserId, OnlineUser> byId = [];
    private readonly Dictionary<NetworkConnectionId, OnlineUserId> byConnection = [];
    private readonly Dictionary<AccountId, Selection> selections = [];
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
                return SnapshotOf(byId[existingId]);
            }

            var onlineUser = new OnlineUser(OnlineUserId.New(), connection);
            byId.Add(onlineUser.Id, onlineUser);
            byConnection.Add(connection.ConnectionId, onlineUser.Id);
            return SnapshotOf(onlineUser);
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

            Remove(onlineUser);
            return true;
        }
    }

    public bool TryDisconnectConnection(NetworkConnectionId connectionId)
    {
        lock (sync)
        {
            if (!byConnection.TryGetValue(connectionId, out var id)
                || !byId.TryGetValue(id, out var user)) return false;
            Remove(user);
            return true;
        }
    }

    public OnlineUserSnapshot GetRequiredUser(OnlineUserId onlineUserId)
    {
        lock (sync)
        {
            return SnapshotOf(GetRequiredAggregate(onlineUserId));
        }
    }

    public IReadOnlyList<AccountId> SnapshotAuthenticatedGameAccounts()
    {
        lock (sync)
            return byId.Values.Where(user => user.AccountId.HasValue
                && user.Connection.EndpointKind == NetworkEndpointKind.Game)
                .Select(user => user.AccountId!.Value).Distinct().ToArray();
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
                onlineUser = SnapshotOf(aggregate);
                return true;
            }

            onlineUser = null;
            return false;
        }
    }

    public SelectedSessionIdentity? Select(SelectedSessionIdentity session)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (sync)
        {
            if (selections.Values.Any(value => value.Session.AccountId != session.AccountId
                    && (value.Session.CharacterId == session.CharacterId
                        || value.Session.WireUserId == session.WireUserId)))
            {
                throw new InvalidOperationException(
                    $"Character/wire identity {session.CharacterId}/{session.WireUserId} is already current.");
            }
            selections.TryGetValue(session.AccountId, out var displaced);
            // Selecting the session that is already selected changes nothing, its stage included.
            if (displaced?.Session != session)
                selections[session.AccountId] = new Selection(session);
            return displaced?.Session;
        }
    }

    public void EnterWorld(SelectedSessionIdentity session)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (sync)
        {
            var selection = FindExact(session)
                ?? throw new InvalidOperationException("Cannot enter the world with a replaced selection.");
            var owner = byId.Values.SingleOrDefault(value =>
                value.AccountId == session.AccountId
                && value.Connection.ConnectionId == session.ConnectionId
                && value.Generation == session.ConnectionGeneration)
                ?? throw new InvalidOperationException("Selected connection generation is no longer current.");
            selection.EnterWorld(owner.Id);
            // Entering advances the online user's generation, while the session keeps the
            // generation it was selected under.
            owner.Advance();
        }
    }

    public bool Fence(SelectedSessionIdentity session)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (sync)
        {
            if (FindExact(session) is not { } selection) return false;
            selection.Fence();
            return true;
        }
    }

    public bool ReleaseExact(SelectedSessionIdentity session)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (sync)
        {
            if (FindExact(session) is not { } selection) return false;
            if (selection.Phase == SelectedSessionPhase.InWorld
                && selection.Owner is { } ownerId
                && byId.TryGetValue(ownerId, out var owner)
                && owner.Connection.ConnectionId == session.ConnectionId)
                owner.Advance();
            return selections.Remove(session.AccountId);
        }
    }

    public SelectedSessionIdentity? Find(AccountId accountId)
    {
        lock (sync) return selections.GetValueOrDefault(accountId)?.Session;
    }

    public SelectedSessionIdentity? FindByCharacter(CharacterId characterId)
    {
        lock (sync)
            return selections.Values.SingleOrDefault(value => value.Session.CharacterId == characterId)?.Session;
    }

    public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId)
    {
        lock (sync)
            return selections.Values.SingleOrDefault(value => value.Session.WireUserId == wireUserId)?.Session;
    }

    public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId)
    {
        lock (sync)
            return selections.Values.SingleOrDefault(value => value.Session.ConnectionId == connectionId)?.Session;
    }

    public IReadOnlyList<SelectedSessionIdentity> SnapshotSelections()
    {
        lock (sync) return selections.Values.Select(value => value.Session).ToArray();
    }

    public SelectedSessionStanding? StandingOf(SelectedSessionIdentity session)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (sync)
            return FindExact(session) is { } selection
                ? new SelectedSessionStanding(selection.Phase, selection.Fenced, IsRevoked(selection))
                : null;
    }

    public bool IsInWorld(SelectedSessionIdentity session)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (sync) return FindExact(session)?.Phase == SelectedSessionPhase.InWorld;
    }

    public bool IsActiveExact(SelectedSessionIdentity session)
    {
        lock (sync)
            return FindExact(session) is { Phase: SelectedSessionPhase.InWorld, Owner: { } ownerId }
                && byId.TryGetValue(ownerId, out var user)
                && user.Connection.ConnectionId == session.ConnectionId
                && user.Connection.EndpointKind == NetworkEndpointKind.Game
                && user.AccountId == session.AccountId
                && byConnection.TryGetValue(session.ConnectionId, out var owner) && owner == ownerId;
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

    private void Remove(OnlineUser user)
    {
        // The selection stays: its teardown still has to release what it holds elsewhere, and
        // from now on it reads as revoked.
        endpoints.Remove(SnapshotOf(user).SessionIdentity);
        byConnection.Remove(user.Connection.ConnectionId);
        byId.Remove(user.Id);
    }

    private Selection? FindExact(SelectedSessionIdentity session) =>
        selections.TryGetValue(session.AccountId, out var selection) && selection.Session == session
            ? selection
            : null;

    private bool IsRevoked(Selection selection) =>
        !byConnection.TryGetValue(selection.Session.ConnectionId, out var userId)
        || !byId.TryGetValue(userId, out var user)
        || user.AccountId != selection.Session.AccountId
        || (selection.Owner is { } owner && owner != userId);

    /// <summary>The character this online user is playing: the in-world selection it owns.</summary>
    private CharacterId? BoundCharacter(OnlineUser user) =>
        user.AccountId is { } accountId
        && selections.TryGetValue(accountId, out var selection)
        && selection.Phase == SelectedSessionPhase.InWorld
        && selection.Owner == user.Id
            ? selection.Session.CharacterId
            : null;

    private OnlineUserSnapshot SnapshotOf(OnlineUser user)
    {
        var character = BoundCharacter(user);
        return new OnlineUserSnapshot(
            user.Id,
            user.Connection,
            user.AccountId,
            character,
            character is not null ? OnlineUserState.CharacterActive : user.State,
            user.Generation);
    }

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
                    || owner.BoundCharacter(user) is not null
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
                    && owner.BoundCharacter(user) is null
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

    /// <summary>The session an account has selected, and how far it has got.</summary>
    /// <remarks>
    /// The identity, not the character's standing: what the character is doing is the online
    /// character runtime's business.
    /// </remarks>
    private sealed class Selection(SelectedSessionIdentity session)
    {
        public SelectedSessionIdentity Session { get; } = session;

        public SelectedSessionPhase Phase { get; private set; } = SelectedSessionPhase.Selected;

        /// <summary>The online user the session entered the world through.</summary>
        public OnlineUserId? Owner { get; private set; }

        public bool Fenced { get; private set; }

        public void EnterWorld(OnlineUserId owner)
        {
            Phase = SelectedSessionPhase.InWorld;
            Owner = owner;
        }

        public void Fence() => Fenced = true;
    }

    private sealed class OnlineUser(OnlineUserId id, NetworkConnectionContext connection)
    {
        public OnlineUserId Id { get; } = id;

        public NetworkConnectionContext Connection { get; } = connection;

        public AccountId? AccountId { get; private set; }

        /// <summary>Connected or authenticated; whether a character is active is read off the selection.</summary>
        public OnlineUserState State { get; private set; } = OnlineUserState.Connected;

        public long Generation { get; private set; } = 1;

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

        /// <summary>A character entered or left the world through this user.</summary>
        public void Advance() => Generation = checked(Generation + 1);
    }
}
