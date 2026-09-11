using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Application.Authentication;

/// <summary>
/// Holds the short-lived launcher credentials for this server process.
/// </summary>
/// <remarks>
/// A launcher credential is deliberately runtime state: restarting the server invalidates every
/// outstanding pair without a database cleanup pass. The stable account id is the only identity
/// that leaves this registry and reaches account or character state.
/// </remarks>
internal sealed class LoginSessionRegistry(
    IDomainTransactionAccessor transactions,
    TimeProvider clock)
{
    private const int MinimumMId = 100_000;
    private const int MaximumMIdExclusive = 1_000_000;
    private const int TicketEntropyBytes = 16;

    private readonly object sync = new();
    private readonly Dictionary<string, Session> byMId = new(StringComparer.Ordinal);
    private readonly Dictionary<AccountId, Session> byAccount = [];
    private readonly HashSet<string> reservedMIds = new(StringComparer.Ordinal);

    public IssuedLoginSession Issue(
        AccountId accountId,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt)
    {
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

    public AccountId? TryConsume(string mId, string ticket)
    {
        ArgumentNullException.ThrowIfNull(mId);
        ArgumentNullException.ThrowIfNull(ticket);
        return Participant().TryReserveConsumption(mId, Digest(ticket));
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

    private AccountId? TryReserveConsumption(
        LoginSessionParticipant participant,
        string mId,
        byte[] ticketDigest)
    {
        lock (sync)
        {
            RemoveExpired(clock.GetUtcNow());
            if (!byMId.TryGetValue(mId, out var session)
                || session.ReservedBy is not null
                || !CryptographicOperations.FixedTimeEquals(
                    session.TicketDigest,
                    ticketDigest))
            {
                return null;
            }

            session.ReservedBy = participant;
            participant.SetConsumption(session);
            return session.AccountId;
        }
    }

    private void Prepare(LoginSessionParticipant participant)
    {
        lock (sync)
        {
            if (participant.Consumption is not { } session)
            {
                return;
            }

            if (!byMId.TryGetValue(session.MId, out var current)
                || !ReferenceEquals(current, session)
                || !ReferenceEquals(session.ReservedBy, participant)
                || clock.GetUtcNow() >= session.ExpiresAt)
            {
                throw new InvalidOperationException(
                    "The login session is no longer redeemable.");
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

            if (participant.Consumption is { } consumed
                && byMId.TryGetValue(consumed.MId, out var current)
                && ReferenceEquals(current, consumed)
                && ReferenceEquals(consumed.ReservedBy, participant))
            {
                byMId.Remove(consumed.MId);
                if (byAccount.TryGetValue(consumed.AccountId, out var accountSession)
                    && ReferenceEquals(accountSession, consumed))
                {
                    byAccount.Remove(consumed.AccountId);
                }
            }
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

            if (participant.Consumption is { } consumed
                && ReferenceEquals(consumed.ReservedBy, participant))
            {
                consumed.ReservedBy = null;
            }
        }
    }

    private void RemoveExpired(DateTimeOffset now)
    {
        foreach (var session in byMId.Values
                     .Where(value => value.ReservedBy is null && now >= value.ExpiresAt)
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
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
        public LoginSessionParticipant? ReservedBy { get; set; }
    }

    private sealed class LoginSessionParticipant(LoginSessionRegistry owner)
        : ITransactionParticipant
    {
        public PendingIssue? Issue { get; private set; }
        public Session? Consumption { get; private set; }
        public bool CommitCanFail => false;

        public IssuedLoginSession StageIssue(
            AccountId accountId,
            DateTimeOffset issuedAt,
            DateTimeOffset expiresAt)
        {
            RequireUnused();
            return owner.StageIssue(this, accountId, issuedAt, expiresAt);
        }

        public AccountId? TryReserveConsumption(string mId, byte[] ticketDigest)
        {
            RequireUnused();
            return owner.TryReserveConsumption(this, mId, ticketDigest);
        }

        public void SetIssue(PendingIssue issue) => this.Issue = issue;

        public void SetConsumption(Session session) => this.Consumption = session;

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
            if (this.Issue is not null || this.Consumption is not null)
            {
                throw new InvalidOperationException(
                    "One authentication transaction can change only one login session.");
            }
        }
    }
}
