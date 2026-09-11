using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Application.Authentication;
using Dfo.Server.Data.Authentication;

namespace Dfo.Server.Persistence.Tests.Accounts;

public sealed class AuthenticationConcurrencyTests
{
    [Fact]
    public async Task OverlappingPasswordReadsCannotLeaveTheSecondLoginWithAnUnwritableSqliteSnapshot()
    {
        using var passwords = new InterleavedPasswords();
        using var fixture = new AuthenticationFixture(passwords);
        await fixture.Bus.SendAsync(new RegisterAccountCommand("first", "first password"));
        await fixture.Bus.SendAsync(new RegisterAccountCommand("second", "second password"));
        passwords.Armed = true;
        var first = Task.Run(async () => await fixture.Bus.SendAsync(new AuthenticateAndIssueTicketCommand("first", "first password")));
        Task<AuthenticationResult>? second = null;
        try
        {
            await passwords.FirstRead.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            second = Task.Factory.StartNew(async () =>
            {
                secondStarted.SetResult();
                return await fixture.Bus.SendAsync(new AuthenticateAndIssueTicketCommand("second", "second password"));
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
            await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // Without write intent, force both credential snapshots to exist before first commits.
            // With write intent, second is waiting in SQLite and cannot reach its read yet.
            await Task.WhenAny(passwords.SecondRead.Task, Task.Delay(500));
            passwords.ReleaseFirst.Set();
            var firstResult = await first.WaitAsync(TimeSpan.FromSeconds(10));
            passwords.ReleaseSecond.Set();
            var secondResult = await second.WaitAsync(TimeSpan.FromSeconds(40));
            Assert.Equal(AuthenticationStatus.Success, firstResult.Status);
            Assert.Equal(AuthenticationStatus.Success, secondResult.Status);
            Assert.NotEqual(firstResult.Ticket, secondResult.Ticket);
            Assert.NotNull(await fixture.Consume(firstResult.MId!, firstResult.Ticket!));
            Assert.NotNull(await fixture.Consume(secondResult.MId!, secondResult.Ticket!));
        }
        finally
        {
            passwords.ReleaseFirst.Set();
            passwords.ReleaseSecond.Set();
            try { await first; } catch { }
            if (second is not null) { try { await second; } catch { } }
        }
    }

    private sealed class InterleavedPasswords : IPasswordCredentialRuleService, IDisposable
    {
        private readonly PasswordCredentialService inner = new(100);
        public bool Armed { get; set; }
        public TaskCompletionSource FirstRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim ReleaseFirst { get; } = new();
        public ManualResetEventSlim ReleaseSecond { get; } = new();
        public string Hash(string password) => inner.Hash(password);
        public void VerifyDummy(string password) => inner.VerifyDummy(password);
        public PasswordCheck Verify(string record, string password)
        {
            if (Armed)
            {
                var isFirst = password == "first password";
                (isFirst ? FirstRead : SecondRead).TrySetResult();
                if (!(isFirst ? ReleaseFirst : ReleaseSecond).Wait(TimeSpan.FromSeconds(15)))
                    throw new TimeoutException("Authentication interleaving did not release its verification gate.");
            }
            return inner.Verify(record, password);
        }
        public void Dispose() { ReleaseFirst.Dispose(); ReleaseSecond.Dispose(); }
    }
}
