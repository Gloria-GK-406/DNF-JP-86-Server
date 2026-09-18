using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Characters;

public sealed class OtherCharacterDetailQueryDomainServiceTests
{
    [Fact]
    public void ProductionGraphRegistersOneSharedDetailQueryService()
    {
        var services = new ServiceCollection();
        new ApplicationModule().ConfigureServices(
            services, new ConfigurationBuilder().Build());
        services.AddSingleton<ISelectedCharacterCurrentViewDomainService, FixedCurrentViews>();
        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IOtherCharacterDetailQueryDomainService>();
        var second = provider.GetRequiredService<IOtherCharacterDetailQueryDomainService>();

        Assert.Same(first, second);
    }

    [Fact]
    public async Task CurrentSameListenerTargetReturnsAnExactTwoGenerationPlan()
    {
        var fixture = new DetailFixture();
        var requester = fixture.Admit(101, 1, "requester", 1, 10011, publishRuntime: false);
        var target = fixture.Admit(202, 2, "target", 1, 10011, publishRuntime: true);

        var result = await fixture.Details.QueryAsync(requester, target.WireUserId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(CharacterDetailQueryFailure.None, result.Failure);
        var plan = Assert.IsType<OtherCharacterDetailProjection>(result.Projection);
        Assert.Equal(requester, plan.Requester);
        Assert.Equal(target, plan.Target);
        Assert.Equal(target.CharacterId, plan.TargetState.Character.Identity.CharacterId);
    }

    [Fact]
    public async Task CrossListenerAndMissingRuntimeTargetsAreRejectedWithoutAProjection()
    {
        var fixture = new DetailFixture();
        var requester = fixture.Admit(101, 1, "requester", 1, 10011, publishRuntime: false);
        var crossListener = fixture.Admit(202, 2, "cross", 1, 10010, publishRuntime: true);

        var cross = await fixture.Details.QueryAsync(requester, crossListener.WireUserId, CancellationToken.None);

        Assert.False(cross.Succeeded);
        Assert.Equal(CharacterDetailQueryFailure.DifferentListener, cross.Failure);
        Assert.Null(cross.Projection);

        var noRuntime = fixture.Admit(303, 3, "no-runtime", 1, 10011, publishRuntime: false);
        var missing = await fixture.Details.QueryAsync(requester, noRuntime.WireUserId, CancellationToken.None);

        Assert.False(missing.Succeeded);
        Assert.Equal(CharacterDetailQueryFailure.TargetRuntimeUnavailable, missing.Failure);
        Assert.Null(missing.Projection);
    }

    [Fact]
    public async Task ReplacedRequesterOrTargetCannotReadAnOldGeneration()
    {
        var fixture = new DetailFixture();
        var requester = fixture.Admit(101, 1, "requester-old", 1, 10011, publishRuntime: false);
        var target = fixture.Admit(202, 2, "target-old", 1, 10011, publishRuntime: true);

        _ = fixture.Admit(101, 1, "requester-new", 2, 10011, publishRuntime: false);
        var staleRequester = await fixture.Details.QueryAsync(requester, target.WireUserId, CancellationToken.None);
        Assert.Equal(CharacterDetailQueryFailure.RequesterNotCurrent, staleRequester.Failure);

        var currentRequester = fixture.Sessions.Find(new AccountId(101))!;
        _ = fixture.Admit(202, 2, "target-new", 2, 10011, publishRuntime: true);
        var staleTargetId = await fixture.Details.QueryAsync(currentRequester, target.WireUserId, CancellationToken.None);

        Assert.True(staleTargetId.Succeeded);
        Assert.Equal(2, staleTargetId.Projection!.Target.ConnectionGeneration);
        Assert.NotEqual(target, staleTargetId.Projection.Target);
    }

    [Fact]
    public async Task UnknownAndSentinelTargetsAreRejected()
    {
        var fixture = new DetailFixture();
        var requester = fixture.Admit(101, 1, "requester", 1, 10011, publishRuntime: false);

        Assert.Equal(
            CharacterDetailQueryFailure.InvalidTarget,
            (await fixture.Details.QueryAsync(requester, ushort.MaxValue, CancellationToken.None)).Failure);
        Assert.Equal(
            CharacterDetailQueryFailure.TargetNotCurrent,
            (await fixture.Details.QueryAsync(requester, 42, CancellationToken.None)).Failure);
    }

    private sealed class DetailFixture
    {
        public DetailFixture() =>
            Details = new OtherCharacterDetailQueryDomainService(
                Sessions, Views, TimeProvider.System);

        public SelectedSessionDomainService Sessions { get; } = new();
        public SelectedSessionRuntimeDirectory Runtime { get; } = new();
        public FixedCurrentViews Views { get; } = new();
        public OtherCharacterDetailQueryDomainService Details { get; }

        public SelectedSessionIdentity Admit(
            int accountId,
            int characterId,
            string connectionId,
            long generation,
            int listenerPort,
            bool publishRuntime)
        {
            var identity = SelectedSessionIdentity.Create(
                new AccountId(accountId),
                new CharacterId(characterId),
                new NetworkConnectionId(connectionId),
                generation,
                listenerPort);
            Sessions.Select(identity);
            if (publishRuntime)
            {
                Runtime.Publish(identity, shouldPersistPosition: false);
                Views.Publish(identity, RuntimeState(identity.CharacterId));
            }
            return identity;
        }

        private static SelectCharacterResult RuntimeState(CharacterId characterId) => new(
            new ActiveCharacterState(
                new CharacterIdentityState(characterId, 0, [], 0, 0),
                new CharacterProgressionState(1, 0),
                new CharacterFatigueState(0, 0),
                new CharacterPositionState(0, 0, 0, 0, 0),
                DateTimeOffset.UnixEpoch),
            null!, null!, null!, null!, null!, null!);
    }

    private sealed class FixedCurrentViews : ISelectedCharacterCurrentViewDomainService
    {
        private readonly Dictionary<SelectedSessionIdentity, SelectCharacterResult> entries = [];

        public void Publish(SelectedSessionIdentity session, SelectCharacterResult value) =>
            entries[session] = value;

        public ValueTask<SelectCharacterResult?> TryLoadAsync(
            SelectedSessionIdentity session, CancellationToken cancellationToken) =>
            ValueTask.FromResult(entries.GetValueOrDefault(session));
    }
}
