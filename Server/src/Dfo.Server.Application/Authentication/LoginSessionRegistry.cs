using Dfo.Server.Abstractions.Execution;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Data.Networking;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Application.Authentication;

/// <summary>
/// Holds login-session credentials for this server process.
/// </summary>
/// <remarks>
/// A launcher credential is deliberately runtime state: restarting the server invalidates every
/// outstanding pair without a database cleanup pass. The stable account id is the only identity
/// that leaves this registry and reaches account or character state.
/// </remarks>
internal sealed class LoginSessionRegistry(
    IDomainTransactionAccessor transactions,
    TimeProvider clock,
    LoginSessionOptions options)
{
    private const int MinimumMId = 100_000;
    private const int MaximumMIdExclusive = 1_000_000;
    private const int TicketEntropyBytes = 16;

    private readonly object sync = new();
    private readonly Dictionary<NetworkConnectionId, Session> byConnection = [];
    private readonly Dictionary<NetworkConnectionId, LoginSessionParticipant> pendingBindings = [];
    private readonly HashSet<NetworkConnectionId> superseded = [];
    private readonly Channel<NetworkConnectionId> closeRequests = System.Threading.Channels.Channel.CreateUnbounded<NetworkConnectionId>();

    internal ChannelReader<NetworkConnectionId> CloseRequests => closeRequests.Reader;

    public Func<bool>? CaptureAuthority(NetworkConnectionId connectionId)
    {
        lock (sync)
        {
            if (superseded.Contains(connectionId)) return static () => false;
            if (!byConnection.TryGetValue(connectionId, out var captured)) return null;
            var generation = captured.Generation;
            return () =>
            {
                lock (sync) return byConnection.GetValueOrDefault(connectionId) == captured
                    && captured.Generation == generation;
            };
        }
    }

    public bool IsSuperseded(NetworkConnectionId connectionId)
    {
        lock (sync) return superseded.Contains(connectionId);
    }

    public void Disconnected(NetworkConnectionId connectionId)
    {
        GameplayWriteAssertion.Writing(nameof(LoginSessionRegistry) + "." + nameof(Disconnected));
        lock (sync)
        {
            superseded.Remove(connectionId);
            if (pendingBindings.TryGetValue(connectionId, out var pending))
                pending.TargetDisconnected = true;
            if (!byConnection.Remove(connectionId, out var session)) return;
            session.ConnectionId = null;
            session.ExpiresAt = clock.GetUtcNow().AddSeconds(options.ReconnectGraceSeconds);
        }
    }

    public void SweepExpired()
    {
        GameplayWriteAssertion.Writing(nameof(LoginSessionRegistry) + "." + nameof(SweepExpired));
        lock (sync) RemoveExpired(clock.GetUtcNow());
    }

    private void Supersede(Session session)
    {
        if (session.ConnectionId is not { } connection) return;
        byConnection.Remove(connection);
        session.ConnectionId = null;
        superseded.Add(connection);
        closeRequests.Writer.TryWrite(connection);
    }
    private readonly Dictionary<string, Session> byMId = new(StringComparer.Ordinal);
    private readonly Dictionary<AccountId, Session> byAccount = [];
    private readonly HashSet<string> reservedMIds = new(StringComparer.Ordinal);

    public IssuedLoginSession Issue(
        AccountId accountId,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt)
    {
        GameplayWriteAssertion.Writing(nameof(LoginSessionRegistry) + "." + nameof(Issue));
        if (accountId.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId));
        }
        if (expiresAt <= issuedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt));
        }

        return Participant().StageIssue(accountId, issuedAt, expiresAt);
    }

    public AccountId? TryBind(NetworkConnectionId connectionId, string mId, string ticket)
    {
        GameplayWriteAssertion.Writing(nameof(LoginSessionRegistry) + "." + nameof(TryBind));
        ArgumentNullException.ThrowIfNull(mId);
        ArgumentNullException.ThrowIfNull(ticket);
        return Participant().TryReserveBinding(connectionId, mId, Digest(ticket));
    }

    private LoginSessionParticipant Participant()
    {
        var transaction = transactions.Current
            ?? throw new InvalidOperationException(
                "Login session changes require an active domain transaction.");
        return transaction.Enlist(
            this,
            () => new LoginSessionParticipant(this));
    }

    private IssuedLoginSession StageIssue(
        LoginSessionParticipant participant,
        AccountId accountId,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt)
    {
        lock (sync)
        {
            RemoveExpired(clock.GetUtcNow());
            string mId;
            do
            {
                mId = RandomNumberGenerator.GetInt32(MinimumMId, MaximumMIdExclusive)
                    .ToString(CultureInfo.InvariantCulture);
            }
            while (byMId.ContainsKey(mId) || !reservedMIds.Add(mId));

            var ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(TicketEntropyBytes));
            participant.SetIssue(new PendingIssue(
                mId,
                Digest(ticket),
                accountId,
                issuedAt,
                expiresAt));
            return new IssuedLoginSession(mId, ticket, expiresAt);
        }
    }

    private AccountId? TryReserveBinding(
        LoginSessionParticipant participant,
        NetworkConnectionId connectionId,
        string mId,
        byte[] ticketDigest)
    {
        lock (sync)
        {
            RemoveExpired(clock.GetUtcNow());
            if (!byMId.TryGetValue(mId, out var session)
                || session.ReservedBy is not null
                || byConnection.ContainsKey(connectionId)
                || pendingBindings.ContainsKey(connectionId)
                || superseded.Contains(connectionId)
                || !CryptographicOperations.FixedTimeEquals(
                    session.TicketDigest,
                    ticketDigest))
            {
                return null;
            }

            session.ReservedBy = participant;
            participant.SetBinding(session, connectionId);
            pendingBindings[connectionId] = participant;
            return session.AccountId;
        }
    }

    private void Prepare(LoginSessionParticipant participant)
    {
        lock (sync)
        {
            if (participant.Binding is not { } session)
            {
                return;
            }

            if (!byMId.TryGetValue(session.MId, out var current)
                || !ReferenceEquals(current, session)
                || !ReferenceEquals(session.ReservedBy, participant)
                || (session.ExpiresAt is { } deadline && clock.GetUtcNow() >= deadline)
                || participant.TargetDisconnected)
            {
                throw new InvalidOperationException(
                    "The login session can no longer bind this connection.");
            }
        }
    }

    private void Commit(LoginSessionParticipant participant)
    {
        lock (sync)
        {
            if (participant.Issue is { } issue)
            {
                if (byAccount.Remove(issue.AccountId, out var previous))
                {
                    Supersede(previous);
                    byMId.Remove(previous.MId);
                }

                var session = new Session(
                    issue.MId,
                    issue.TicketDigest,
                    issue.AccountId,
                    issue.IssuedAt,
                    issue.ExpiresAt);
                byMId[session.MId] = session;
                byAccount[session.AccountId] = session;
                reservedMIds.Remove(issue.MId);
            }

            if (participant.Binding is { } bound
                && byMId.TryGetValue(bound.MId, out var current)
                && ReferenceEquals(current, bound)
                && ReferenceEquals(bound.ReservedBy, participant))
            {
                Supersede(bound);
                bound.ReservedBy = null;
                bound.Generation++;
                bound.ConnectionId = participant.TargetDisconnected ? null : participant.ConnectionId;
                bound.ExpiresAt = participant.TargetDisconnected
                    ? clock.GetUtcNow().AddSeconds(options.ReconnectGraceSeconds) : null;
                if (bound.ConnectionId is { } connection) byConnection[connection] = bound;
            }
            if (participant.ConnectionId is { } target) pendingBindings.Remove(target);
        }
    }

    private void Rollback(LoginSessionParticipant participant)
    {
        lock (sync)
        {
            if (participant.Issue is { } issue)
            {
                reservedMIds.Remove(issue.MId);
            }

            if (participant.Binding is { } bound
                && ReferenceEquals(bound.ReservedBy, participant))
            {
                bound.ReservedBy = null;
            }
            if (participant.ConnectionId is { } target) pendingBindings.Remove(target);
        }
    }

    private void RemoveExpired(DateTimeOffset now)
    {
        foreach (var session in byMId.Values
                     .Where(value => value.ReservedBy is null && value.ExpiresAt is { } deadline && now >= deadline)
                     .ToArray())
        {
            byMId.Remove(session.MId);
            if (byAccount.TryGetValue(session.AccountId, out var accountSession)
                && ReferenceEquals(accountSession, session))
            {
                byAccount.Remove(session.AccountId);
            }
        }
    }

    private static byte[] Digest(string ticket) =>
        SHA256.HashData(Encoding.ASCII.GetBytes(ticket));

    internal sealed record IssuedLoginSession(
        string MId,
        string Ticket,
        DateTimeOffset ExpiresAtUtc);

    private sealed record PendingIssue(
        string MId,
        byte[] TicketDigest,
        AccountId AccountId,
        DateTimeOffset IssuedAt,
        DateTimeOffset ExpiresAt);

    private sealed class Session(
        string mId,
        byte[] ticketDigest,
        AccountId accountId,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt)
    {
        public string MId { get; } = mId;
        public byte[] TicketDigest { get; } = ticketDigest;
        public AccountId AccountId { get; } = accountId;
        public DateTimeOffset IssuedAt { get; } = issuedAt;
        public DateTimeOffset? ExpiresAt { get; set; } = expiresAt;
        public NetworkConnectionId? ConnectionId { get; set; }
        public long Generation { get; set; }
        public LoginSessionParticipant? ReservedBy { get; set; }
    }

    private sealed class LoginSessionParticipant(LoginSessionRegistry owner)
        : ITransactionParticipant
    {
        public PendingIssue? Issue { get; private set; }
        public Session? Binding { get; private set; }
        public NetworkConnectionId? ConnectionId { get; private set; }
        public bool TargetDisconnected { get; set; }
        public bool CommitCanFail => false;

        public IssuedLoginSession StageIssue(
            AccountId accountId,
            DateTimeOffset issuedAt,
            DateTimeOffset expiresAt)
        {
            RequireUnused();
            return owner.StageIssue(this, accountId, issuedAt, expiresAt);
        }

        public AccountId? TryReserveBinding(NetworkConnectionId connectionId, string mId, byte[] ticketDigest)
        {
            RequireUnused();
            return owner.TryReserveBinding(this, connectionId, mId, ticketDigest);
        }

        public void SetIssue(PendingIssue issue) => this.Issue = issue;

        public void SetBinding(Session session, NetworkConnectionId connectionId)
        {
            Binding = session;
            ConnectionId = connectionId;
        }

        public ValueTask PrepareAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            owner.Prepare(this);
            return ValueTask.CompletedTask;
        }

        public ValueTask CommitAsync(CancellationToken cancellationToken)
        {
            owner.Commit(this);
            return ValueTask.CompletedTask;
        }

        public ValueTask RollbackAsync(CancellationToken cancellationToken)
        {
            owner.Rollback(this);
            return ValueTask.CompletedTask;
        }

        private void RequireUnused()
        {
            if (this.Issue is not null || this.Binding is not null)
            {
                throw new InvalidOperationException(
                    "One authentication transaction can change only one login session.");
            }
        }
    }
}
