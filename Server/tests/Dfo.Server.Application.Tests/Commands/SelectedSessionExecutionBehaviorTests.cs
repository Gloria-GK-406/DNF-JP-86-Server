using System.Diagnostics.CodeAnalysis;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Commands.Pipeline;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Commands;

/// <summary>
/// Spec appendix H row 45: the selected-session behaviour's checks that are not locks, through the real bus
/// on the executor. Each refusal is checked against a command that runs when the facts are right.
/// </summary>
public sealed class SelectedSessionExecutionBehaviorTests
{
    private static readonly SelectedSessionIdentity First = SelectedSessionIdentity.Create(
        new AccountId(1), new CharacterId(11), new NetworkConnectionId("Game:1"), 1, 10011);
    private static readonly SelectedSessionIdentity Second = SelectedSessionIdentity.Create(
        new AccountId(2), new CharacterId(12), new NetworkConnectionId("Game:2"), 1, 10011);

    [Fact]
    public async Task A_command_whose_account_differs_from_its_session_never_reaches_its_handler()
    {
        await using var f = new Fixture(First, Second);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => f.SendAsync(new Probe(First, Second.AccountId, [First])));

        Assert.Contains("account differs", error.Message, StringComparison.Ordinal);
        Assert.Empty(f.Handled);
        Assert.Single(await f.SendAsync(new Probe(First, First.AccountId, [First])));
    }

    [Fact]
    public async Task A_participant_set_without_the_reporting_session_never_reaches_its_handler()
    {
        await using var f = new Fixture(First, Second);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => f.SendAsync(new Probe(First, First.AccountId, [Second])));

        Assert.Contains("frozen participant set", error.Message, StringComparison.Ordinal);
        Assert.Empty(f.Handled);
        Assert.Single(await f.SendAsync(new Probe(First, First.AccountId, [First, Second])));
    }

    [Fact]
    public async Task A_participant_that_is_no_longer_current_stops_the_command_before_its_handler()
    {
        await using var f = new Fixture(First);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => f.SendAsync(new Probe(First, First.AccountId, [First, Second])));

        Assert.Contains("stale", error.Message, StringComparison.Ordinal);
        Assert.Empty(f.Handled);
        f.Sessions.Current.Add(Second);
        Assert.Single(await f.SendAsync(new Probe(First, First.AccountId, [First, Second])));
    }

    [Fact]
    public async Task The_handler_runs_with_the_captured_recipient_as_its_selected_session_scope()
    {
        await using var f = new Fixture(First, Second);

        var seen = await f.SendAsync(new Probe(First, First.AccountId, [First, Second]));

        Assert.Equal(First, Assert.Single(seen)?.Session);
        Assert.Null(f.Services.GetRequiredService<SelectedSessionCommandScope>().Current);
    }

    [Fact]
    public async Task An_uncertain_outcome_fences_every_participant_not_only_the_reporter()
    {
        await using var f = new Fixture(First, Second);

        await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(
            () => f.SendAsync(new Probe(First, First.AccountId, [First, Second], Uncertain: true)));

        Assert.Equal([First, Second], f.Sessions.Fenced.OrderBy(x => x.AccountId.Value));
    }

    private sealed record Probe(SelectedSessionIdentity Owner, AccountId AccountId,
        IReadOnlyList<SelectedSessionIdentity> Sessions, bool Uncertain = false)
        : ICommand<IReadOnlyList<SelectedSessionRecipient?>>, IAccountCommand, IMultiSelectedSessionCommand
    {
        public SelectedSessionIdentity? Session => Owner;
    }

    private sealed class ProbeHandler(SelectedSessionCommandScope scope, Fixture.Record record)
        : ICommandHandler<Probe, IReadOnlyList<SelectedSessionRecipient?>>
    {
        public ValueTask<IReadOnlyList<SelectedSessionRecipient?>> HandleAsync(
            CommandExecutionContext context, Probe command, CancellationToken cancellationToken)
        {
            record.Handled.Add(scope.Current);
            if (command.Uncertain)
                throw new TransactionOutcomeUncertainException(TransactionUncertaintyReason.PostCommitPublicationFailure,
                    "Injected uncertain settlement.", new IOException("lost"));
            return ValueTask.FromResult<IReadOnlyList<SelectedSessionRecipient?>>([scope.Current]);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public sealed class Record
        {
            public List<SelectedSessionRecipient?> Handled { get; } = [];
        }

        public Fixture(params SelectedSessionIdentity[] current)
        {
            Sessions = new CurrentSessions(current);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddCommandDispatching();
            services.AddSingleton<ISelectedSessionDomainService>(Sessions);
            services.AddSingleton(Log);
            services.AddCommandHandler<Probe, IReadOnlyList<SelectedSessionRecipient?>, ProbeHandler>(
                CommandExecutionKind.Direct);
            Services = services.BuildServiceProvider();
        }

        public ServiceProvider Services { get; }
        public CurrentSessions Sessions { get; }
        public Record Log { get; } = new();
        public List<SelectedSessionRecipient?> Handled => Log.Handled;

        public Task<IReadOnlyList<SelectedSessionRecipient?>> SendAsync(Probe command) =>
            TestGameplay.RunAsync(Services, async () =>
                await Services.GetRequiredService<ICommandBus>().SendAsync(command));

        public ValueTask DisposeAsync() => Services.DisposeAsync();
    }

    private sealed class CurrentSessions(params SelectedSessionIdentity[] current) : ISelectedSessionDomainService
    {
        public HashSet<SelectedSessionIdentity> Current { get; } = [.. current];
        public HashSet<SelectedSessionIdentity> Fenced { get; } = [];
        public SelectedSessionStanding? StandingOf(SelectedSessionIdentity session) =>
            Current.Contains(session) ? new(SelectedSessionPhase.InWorld, Fenced.Contains(session), ConnectionRevoked: false) : null;
        public bool IsSelected(SelectedSessionIdentity session) => Current.Contains(session);
        public bool IsInWorld(SelectedSessionIdentity session) => Current.Contains(session);
        public SelectedSessionIdentity? Select(SelectedSessionIdentity session) => throw new NotSupportedException();
        public void EnterWorld(SelectedSessionIdentity session) => throw new NotSupportedException();
        public bool Fence(SelectedSessionIdentity session) => Current.Contains(session) && Fenced.Add(session);
        public bool ReleaseExact(SelectedSessionIdentity session) => throw new NotSupportedException();
        public SelectedSessionIdentity? Find(AccountId accountId) => Current.FirstOrDefault(x => x.AccountId == accountId);
        public SelectedSessionIdentity? FindByCharacter(CharacterId characterId) => Current.FirstOrDefault(x => x.CharacterId == characterId);
        public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId) => Current.FirstOrDefault(x => x.WireUserId == wireUserId);
        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId) => Current.FirstOrDefault(x => x.ConnectionId == connectionId);
        public bool TryGetInWorld(CharacterId characterId, [NotNullWhen(true)] out SelectedSessionIdentity? session) =>
            (session = FindByCharacter(characterId)) is not null;
        public bool TryGetInWorld(ushort wireUserId, [NotNullWhen(true)] out SelectedSessionIdentity? session) =>
            (session = FindByWireUserId(wireUserId)) is not null;
        public bool TryGetInWorld(NetworkConnectionId connectionId, [NotNullWhen(true)] out SelectedSessionIdentity? session) =>
            (session = FindByConnection(connectionId)) is not null;
    }
}
