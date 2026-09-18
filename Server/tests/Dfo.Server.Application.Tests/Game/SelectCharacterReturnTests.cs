using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Parties;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.World;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.ReturnToSelection;

namespace Dfo.Server.Application.Tests.Game;

public sealed partial class SelectCharacterLifecycleTests
{
    [Fact]
    public async Task Return_releases_production_state_preserves_login_and_allows_selection_again()
    {
        var f = await Fixture.CreateAsync();
        await f.Handler.HandleAsync(f.Context, f.Command, CancellationToken.None);
        var selected = f.Directory.Find(f.AccountId)!;
        var online = f.Users.GetRequired(f.OnlineUserId);
        var roster = new RosterCharacters(f.Result);
        var prepared = await new PrepareReturnToSelectionCommandHandler(f.Users, f.Directory, roster)
            .HandleAsync(ReturnContext(), new(f.AccountId, f.ConnectionId, online.Generation), CancellationToken.None);
        Assert.Equal(0, f.Inventory.Saves);
        Assert.Same(roster.Latest, prepared.Roster);
        Assert.Equal(413500016, Assert.Single(prepared.Roster.Characters[0].Appearances!).DisplayItemId);
        Assert.True(prepared.ResetKnightDeck);

        var runtimes = new SelectedSessionRuntimeDirectory();
        runtimes.Publish(selected, shouldPersistPosition: true);
        var positions = new CharacterPositionDomainService();
        positions.Place(selected, f.Result.Character.Position);
        var presence = new SessionPresenceDomainService();
        presence.Admit(new SessionPresence(selected.CharacterId, 10011, 1, 2, true, selected));
        var stores = new ExpertStoreDomainService();
        stores.Open(selected);
        var runs = new DungeonRunDomainService();
        var departure = new RecordingDungeonDeparture();
        var memberSession = SelectedSessionIdentity.Create(
            new AccountId(43), new CharacterId(502), new NetworkConnectionId("Game:return-member"), 1, 10011);
        f.Directory.Select(memberSession);
        var started = runs.BeginParty(
            [
                new(selected.AccountId, selected.CharacterId, 0, true),
                new(memberSession.AccountId, memberSession.CharacterId, 1, false),
            ],
            new(10000, 0, 1, 1, new MazeCell(0, 0), new MazeCell(0, 0),
                61000, 1, new Dictionary<MazeCell, int> { [new(0, 0)] = 61000 }), 0);
        var cleanup = new SelectedSessionCleanupService(f.Directory, runtimes,
            new CharacterContainerDomainService(), new CharacterItemCooldownDomainService(TimeProvider.System),
            f.Active, presence, stores, new PvpLobbyDomainService(), [f.Mutations], positions,
            new CharacterVitalRuntimeDomainService(),
            new PartyService(
                new StubPartyMemberStatusDomainService(),
                new PartyModeRuleService(),
                TimeProvider.System,
                EmptyPartyMemberSessionEndpointSource.Instance),
            runs,
            [departure]);
        var complete = new CompleteReturnToSelectionCommandHandler(f.Users, f.Directory, cleanup);

        await complete.HandleAsync(ReturnContext(), new(prepared), CancellationToken.None);

        Assert.Null(f.Directory.Find(f.AccountId));
        Assert.False(runtimes.IsPublishedExact(selected));
        Assert.Null(positions.Find(selected.CharacterId));
        Assert.Null(presence.Find(selected.CharacterId));
        Assert.False(stores.HasOpenStore(selected.CharacterId));
        Assert.Null(runs.Find(selected.CharacterId));
        Assert.Equal(started.RunId, runs.Find(memberSession.CharacterId)!.RunId);
        Assert.Equal([(started.RunId, selected.CharacterId)], departure.Calls);
        Assert.Equal(f.Result.Character.Position, f.Mutations.PersistedPosition);
        var returned = f.Users.GetRequired(f.OnlineUserId);
        Assert.Equal(f.AccountId, returned.AccountId);
        Assert.Equal(OnlineUserState.Authenticated, returned.State);
        Assert.Null(returned.CurrentCharacterId);

        await f.Handler.HandleAsync(f.Context, f.Command, CancellationToken.None);
        Assert.Equal(selected.CharacterId, f.Users.GetRequired(f.OnlineUserId).CurrentCharacterId);
        Assert.NotEqual(selected, f.Directory.Find(f.AccountId));

        await cleanup.CleanupAsync(memberSession, includeOwner: false, CancellationToken.None);
        Assert.Null(runs.Find(memberSession.CharacterId));
        Assert.Equal(
            [(started.RunId, selected.CharacterId), (started.RunId, memberSession.CharacterId)],
            departure.Calls);
    }

    [Fact]
    public async Task Stale_return_cannot_release_a_newly_selected_session()
    {
        var f = await Fixture.CreateAsync();
        await f.Handler.HandleAsync(f.Context, f.Command, CancellationToken.None);
        var online = f.Users.GetRequired(f.OnlineUserId);
        var prepare = new PrepareReturnToSelectionCommandHandler(f.Users, f.Directory,
            new RosterCharacters(f.Result));
        var old = await prepare.HandleAsync(ReturnContext(),
            new(f.AccountId, f.ConnectionId, online.Generation), CancellationToken.None);
        await f.Handler.HandleAsync(f.Context, f.Command, CancellationToken.None);
        var replacement = f.Directory.Find(f.AccountId);
        var cleanupCalls = f.Cleanup.Calls.Count;
        var complete = new CompleteReturnToSelectionCommandHandler(f.Users, f.Directory, f.Cleanup);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await complete.HandleAsync(ReturnContext(), new(old), CancellationToken.None));

        Assert.Equal(replacement, f.Directory.Find(f.AccountId));
        Assert.Equal(cleanupCalls, f.Cleanup.Calls.Count);
    }

    [Fact]
    public async Task Return_preparation_does_not_call_obsolete_inventory_save()
    {
        var f = await Fixture.CreateAsync();
        await f.Handler.HandleAsync(f.Context, f.Command, CancellationToken.None);
        var selected = f.Directory.Find(f.AccountId);
        f.Inventory.FailSave = true;
        var handler = new PrepareReturnToSelectionCommandHandler(f.Users, f.Directory,
            new RosterCharacters(f.Result));

        _ = await handler.HandleAsync(ReturnContext(),
            new(f.AccountId, f.ConnectionId, f.Users.GetRequired(f.OnlineUserId).Generation), CancellationToken.None);

        Assert.Equal(0, f.Inventory.Saves);
        Assert.Equal(selected, f.Directory.Find(f.AccountId));
        Assert.Equal(selected!.CharacterId, f.Users.GetRequired(f.OnlineUserId).CurrentCharacterId);
    }

    private static CommandExecutionContext ReturnContext() => new(Guid.NewGuid(),
        typeof(PrepareReturnToSelectionCommand), typeof(PrepareReturnToSelectionCommandHandler),
        CommandExecutionKind.Transactional, AccountAccessMode: UserAccessMode.Exclusive);

    private sealed class RosterCharacters(SelectCharacterResult state) : ICharacterDomainService
    {
        // This fresh roster differs from the entry snapshot. The return command must query it.
        public AccountCharacterRoster Latest { get; } = new(state.Account.AccountId, 24,
            [new(state.Character.Identity.CharacterId, 3, "latest"u8.ToArray(), 12, 0, 2, 0,
                [new(3, 413500016, 4, new byte[4], 0, 0, 0, 0)])]);
        public ValueTask<AccountCharacterRoster> GetAccountRosterWithAppearanceAsync(AccountId id, CancellationToken ct) =>
            ValueTask.FromResult(Latest);
        public ValueTask<AccountCharacterRoster> GetAccountRosterAsync(AccountId id, CancellationToken ct) => throw new NotSupportedException();
        public ICharacter Get(CharacterId id) => throw new NotSupportedException();
        public ValueTask<ActiveCharacterState> LoadForSelectionAsync(AccountId id, ushort slot, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<ActiveCharacterState> LoadByRosterPositionAsync(AccountId id, ushort slot, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<CharacterNameStatus> CheckNameAsync(byte[] name, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<CharacterCreationResult> CreateCharacterAsync(AccountId id, byte job, byte[] name, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<CharacterDeletionResult> DeleteCharacterAsync(AccountId id, ushort slot, byte[] name, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RecordingDungeonDeparture : IDungeonRunParticipantDepartureSink
    {
        public List<(DungeonRunId RunId, CharacterId CharacterId)> Calls { get; } = [];

        public void ParticipantDeparted(DungeonRunId runId, CharacterId characterId) =>
            Calls.Add((runId, characterId));
    }
}
