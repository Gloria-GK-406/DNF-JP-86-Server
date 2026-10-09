using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Sessions;
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
        var old = Session("old", 1);
        var replacement = Session("replacement", 2);
        var sessions = new SelectedSessionDomainService();
        var published = new SelectedSessionRuntimeDirectory();
        var query = new FixedQuery();
        var views = new SelectedCharacterCurrentViewDomainService(sessions, published, query);
        sessions.Select(old);
        published.Publish(old, true);

        Assert.NotNull(await views.TryLoadAsync(old, CancellationToken.None));
        sessions.Select(replacement);
        published.Publish(replacement, false);

        Assert.Null(await views.TryLoadAsync(old, CancellationToken.None));
        Assert.False(published.RemoveExact(old));
        Assert.True(published.IsPublishedExact(replacement));
        Assert.NotNull(await views.TryLoadAsync(replacement, CancellationToken.None));
        Assert.Equal(2, query.Loads);
    }

    [Fact]
    public async Task DelayedReadCannotProjectIntoReplacementConnection()
    {
        var old = Session("old", 1);
        var replacement = Session("replacement", 2);
        var sessions = new SelectedSessionDomainService();
        var published = new SelectedSessionRuntimeDirectory();
        var query = new FixedQuery { HoldNextRead = true };
        var views = new SelectedCharacterCurrentViewDomainService(sessions, published, query);
        sessions.Select(old);
        published.Publish(old, true);

        var delayed = views.TryLoadAsync(old, CancellationToken.None).AsTask();
        await query.Started.Task;
        sessions.Select(replacement);
        published.Publish(replacement, false);
        query.Release();

        Assert.Null(await delayed);
        Assert.True(published.IsPublishedExact(replacement));
    }

    [Fact]
    public void LateVitalCleanupDoesNotEraseTheReplacement()
    {
        var old = Session("old", 1);
        var replacement = Session("replacement", 2);
        var vitals = new CharacterVitalRuntimeDomainService();
        vitals.Publish(old, 70);
        vitals.Publish(replacement, 85);

        Assert.False(vitals.RemoveExact(old));
        Assert.Equal(85, vitals.HpPercentOf(old.CharacterId));
        Assert.True(vitals.RemoveExact(replacement));
        Assert.Equal(0, vitals.HpPercentOf(old.CharacterId));
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
