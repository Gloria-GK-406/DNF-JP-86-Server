using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.MarriageHousing;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Sessions;

/// <summary>
/// A phase of the selection workflow runs before the session has entered the world, so it asks
/// whether the session is still valid rather than whether it is in the world -- and a session
/// whose connection has been revoked under it is not.
/// </summary>
public sealed class SelectionWorkflowCurrencyTests
{
    private static readonly AccountId Account = new(61);
    private static readonly CharacterId Character = new(6101);

    [Fact]
    public async Task A_phase_runs_for_a_selection_that_has_not_entered_the_world_yet()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var sessions = TestSessions.Directory(out var state);
            var session = sessions.Selected(state, Account, Character, "Game:workflow");
            var housing = new RecordingHousing();

            Assert.True(await Run(new InitializeHousingCommandHandler(housing, sessions), session));
            Assert.Equal([Character], housing.Initialized);
        });
    }

    [Fact]
    public async Task A_phase_is_refused_once_the_connection_is_revoked()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var sessions = TestSessions.Directory(out var state);
            var session = sessions.Selected(state, Account, Character, "Game:workflow-revoked");
            Assert.True(state.TryDisconnectConnection(session.ConnectionId));
            var housing = new RecordingHousing();

            Assert.False(await Run(new InitializeHousingCommandHandler(housing, sessions), session));
            Assert.Empty(housing.Initialized);
        });
    }

    private static ValueTask<bool> Run(InitializeHousingCommandHandler handler, SelectedSessionIdentity session) =>
        handler.HandleAsync(
            new CommandExecutionContext(Guid.NewGuid(), typeof(InitializeHousingCommand),
                typeof(InitializeHousingCommandHandler), CommandExecutionKind.Transactional),
            new InitializeHousingCommand(session), CancellationToken.None);

    private sealed class RecordingHousing : IMarriageHousingDomainService
    {
        public List<CharacterId> Initialized { get; } = [];

        public ValueTask InitializeAsync(AccountId account, CharacterId character, CancellationToken ct)
        {
            Initialized.Add(character);
            return ValueTask.CompletedTask;
        }

        public ValueTask<HousingResult> ExecuteAsync(SelectedSessionIdentity actor, HousingRequest request,
            IReadOnlyList<AccountId> accounts, CancellationToken ct) => throw new NotSupportedException();

        public ValueTask<NpcShopPurchaseResult> PurchaseAsync(SelectedSessionIdentity actor, int itemId, int count,
            int shopId, IReadOnlyList<AccountId> accounts, CancellationToken ct) => throw new NotSupportedException();
    }
}
