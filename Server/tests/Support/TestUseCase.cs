using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Mail;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Testing;

/// <summary>Settles a test's direct call to a domain service the way the command it stands in for would.</summary>
/// <remarks>
/// Domain services join the caller's transaction and never open one of their own; called without one they
/// refuse. A test that calls such a service directly, instead of through a command, opens the command's
/// transaction here: a write commits when the call returns, a read-only one is only rolled back, and a throw
/// leaves the transaction to roll back (a service that refuses aborts it first). When the test has already
/// opened a transaction, the call joins it and the test settles it as before.
/// </remarks>
internal static class TestUseCase
{
    public static Task<T> RunAsync<T>(IServiceProvider services, Func<ValueTask<T>> call, bool readOnly = false) =>
        RunAsync(Scope(services), call, readOnly);

    public static Task RunAsync(IServiceProvider services, Func<ValueTask> call, bool readOnly = false) =>
        RunAsync(Scope(services), call, readOnly);

    /// <summary>For a test that builds its services by hand around its own scope.</summary>
    public static async Task<T> RunAsync<T>(DomainTransactionScope scope, Func<ValueTask<T>> call, bool readOnly = false)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(call);
        if (scope.Current is not null) return await call();
        await using var lease = scope.Begin(readOnly);
        var result = await call();
        if (!readOnly) await lease.CommitAsync(default);
        return result;
    }

    /// <inheritdoc cref="RunAsync{T}(DomainTransactionScope, Func{ValueTask{T}}, bool)"/>
    public static async Task RunAsync(DomainTransactionScope scope, Func<ValueTask> call, bool readOnly = false) =>
        _ = await RunAsync(scope, async () =>
        {
            await call();
            return true;
        }, readOnly);

    /// <summary>A read on a read-only transaction, which is never committed.</summary>
    public static Task<T> ReadAsync<T>(IServiceProvider services, Func<ValueTask<T>> call) =>
        RunAsync(services, call, readOnly: true);

    /// <summary>The registered mailbox, each call of which is settled by <see cref="RunAsync{T}"/>.</summary>
    /// <remarks>Reading the inbox uses a read-only transaction; every other call writes.</remarks>
    public static IMailboxDomainService Mailbox(IServiceProvider services) =>
        new UseCaseMailbox(services.GetRequiredService<IMailboxDomainService>(), services);

    private static DomainTransactionScope Scope(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.GetService<DomainTransactionScope>()
            ?? (DomainTransactionScope)services.GetRequiredService<IDomainTransactionAccessor>();
    }

    private sealed class UseCaseMailbox(IMailboxDomainService inner, IServiceProvider services) : IMailboxDomainService
    {
        public ValueTask<MailSendReceipt> SendAsync(Dfo.Server.Data.Mail.Mail mail, CancellationToken cancellationToken = default) =>
            new(RunAsync(services, () => inner.SendAsync(mail, cancellationToken)));

        public ValueTask<MailSendReceipt> SendPreparedAsync(Dfo.Server.Data.Mail.Mail mail,
            IReadOnlyList<PreparedMailAttachment> attachments, CancellationToken cancellationToken = default) =>
            new(RunAsync(services, () => inner.SendPreparedAsync(mail, attachments, cancellationToken)));

        public ValueTask<IReadOnlyList<MailView>> InboxAsync(CharacterId recipient, CancellationToken cancellationToken = default) =>
            new(TestUseCase.ReadAsync(services, () => inner.InboxAsync(recipient, cancellationToken)));

        public ValueTask<MailView> ReadAsync(CharacterId recipient, long mailId, CancellationToken cancellationToken = default) =>
            new(RunAsync(services, () => inner.ReadAsync(recipient, mailId, cancellationToken)));

        public ValueTask<MailClaimReceipt> ClaimAsync(CharacterId recipient, long attachmentId, CancellationToken cancellationToken = default) =>
            new(RunAsync(services, () => inner.ClaimAsync(recipient, attachmentId, cancellationToken)));

        public ValueTask DeleteAsync(CharacterId recipient, long mailId, CancellationToken cancellationToken = default) =>
            new(RunAsync(services, () => inner.DeleteAsync(recipient, mailId, cancellationToken)));

        public ValueTask RevokeSystemDeliveryAsync(CharacterId recipient, long mailId, string expectedDeliveryKey,
            CancellationToken cancellationToken = default) =>
            new(RunAsync(services, () => inner.RevokeSystemDeliveryAsync(recipient, mailId, expectedDeliveryKey, cancellationToken)));
    }
}
