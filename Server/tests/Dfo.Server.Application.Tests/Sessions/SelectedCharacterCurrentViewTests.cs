using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Testing;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Tests.Sessions;

public sealed class SelectedCharacterCurrentViewTests
{
    [Fact]
    public async Task ReplacementCannotReadOrRemoveItsPredecessorsPublication()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var sessions = Dfo.Server.Testing.TestSessions.Directory(out var state);
            var query = new FixedQuery();
            var views = new SelectedCharacterCurrentViewDomainService(sessions, query);
            var old = sessions.InWorld(state, new AccountId(7), new CharacterId(70), "old");

            Assert.NotNull(await views.TryLoadAsync(old, CancellationToken.None));
            var replacement = sessions.InWorld(state, new AccountId(7), new CharacterId(70), "replacement");

            Assert.Null(await views.TryLoadAsync(old, CancellationToken.None));
            Assert.False(sessions.ReleaseExact(old));
            Assert.True(sessions.IsInWorld(replacement));
            Assert.NotNull(await views.TryLoadAsync(replacement, CancellationToken.None));
            Assert.Equal(2, query.Loads);
        });
    }

    [Fact]
    public async Task DelayedReadCannotProjectIntoReplacementConnection()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var sessions = Dfo.Server.Testing.TestSessions.Directory(out var state);
            var query = new FixedQuery { HoldNextRead = true };
            var views = new SelectedCharacterCurrentViewDomainService(sessions, query);
            var old = sessions.InWorld(state, new AccountId(7), new CharacterId(70), "old");

            var delayed = views.TryLoadAsync(old, CancellationToken.None).AsTask();
            await query.Started.Task;
            var replacement = sessions.InWorld(state, new AccountId(7), new CharacterId(70), "replacement");
            query.Release();

            Assert.Null(await delayed);
            Assert.True(sessions.IsInWorld(replacement));
        });
    }

    [Fact]
    public void LateVitalCleanupDoesNotEraseTheReplacement()
    {
        TestGameplay.Run(() =>
        {
            var old = Session("old", 1);
            var replacement = Session("replacement", 2);
            var vitals = new OnlineCharacterRuntimeDomainService(TimeProvider.System);
            vitals.Begin(old, new CharacterPositionState(1, 0, 0, 0, 0), 70);
            vitals.Begin(replacement, new CharacterPositionState(1, 0, 0, 0, 0), 85);

            Assert.False(vitals.EndExact(old));
            Assert.Equal(85, vitals.HpPercentOf(old.CharacterId));
            Assert.True(vitals.EndExact(replacement));
            Assert.Equal(0, vitals.HpPercentOf(old.CharacterId));
        });
    }

    private static SelectedSessionIdentity Session(string connection, long generation) =>
        SelectedSessionIdentity.Create(
            new AccountId(7), new CharacterId(70), new NetworkConnectionId(connection),
            generation, 10011);

    private sealed class FixedQuery : ICharacterCurrentStateQueryDomainService
    {
        private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started => started;
        public bool HoldNextRead { get; set; }
        public int Loads { get; private set; }

        public async ValueTask<SelectCharacterResult> ReadAsync(
            SelectedSessionIdentity session, CancellationToken cancellationToken)
        {
            Loads++;
            if (HoldNextRead)
            {
                started.SetResult();
                await released.Task.WaitAsync(cancellationToken);
            }
            return new SelectCharacterResult(null!, null!, null!, null!, null!, null!, null!);
        }

        public void Release() => released.SetResult();
    }
}
