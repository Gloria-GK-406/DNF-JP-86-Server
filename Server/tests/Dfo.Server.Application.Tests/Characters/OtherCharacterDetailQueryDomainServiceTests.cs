using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Abstractions.Characters;
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
        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IOtherCharacterDetailQueryDomainService>();
        var second = provider.GetRequiredService<IOtherCharacterDetailQueryDomainService>();

        Assert.Same(first, second);
    }

    [Fact]
    public void CurrentSameListenerTargetReturnsAnExactTwoGenerationPlan()
    {
        var fixture = new DetailFixture();
        var requester = fixture.Admit(101, 1, "requester", 1, 10011, publishRuntime: false);
        var target = fixture.Admit(202, 2, "target", 1, 10011, publishRuntime: true);

        var result = fixture.Details.Query(requester, target.WireUserId);

        Assert.True(result.Succeeded);
        Assert.Equal(CharacterDetailQueryFailure.None, result.Failure);
        var plan = Assert.IsType<OtherCharacterDetailProjection>(result.Projection);
        Assert.Equal(requester, plan.Requester);
        Assert.Equal(target, plan.Target);
        Assert.Equal(target.CharacterId, plan.TargetState.Character.Identity.CharacterId);
    }

    [Fact]
    public void CrossListenerAndMissingRuntimeTargetsAreRejectedWithoutAProjection()
    {
        var fixture = new DetailFixture();
        var requester = fixture.Admit(101, 1, "requester", 1, 10011, publishRuntime: false);
        var crossListener = fixture.Admit(202, 2, "cross", 1, 10010, publishRuntime: true);

        var cross = fixture.Details.Query(requester, crossListener.WireUserId);

        Assert.False(cross.Succeeded);
        Assert.Equal(CharacterDetailQueryFailure.DifferentListener, cross.Failure);
        Assert.Null(cross.Projection);

        var noRuntime = fixture.Admit(303, 3, "no-runtime", 1, 10011, publishRuntime: false);
        var missing = fixture.Details.Query(requester, noRuntime.WireUserId);

        Assert.False(missing.Succeeded);
        Assert.Equal(CharacterDetailQueryFailure.TargetRuntimeUnavailable, missing.Failure);
        Assert.Null(missing.Projection);
    }

    [Fact]
    public void ReplacedRequesterOrTargetCannotReadAnOldGeneration()
    {
        var fixture = new DetailFixture();
        var requester = fixture.Admit(101, 1, "requester-old", 1, 10011, publishRuntime: false);
        var target = fixture.Admit(202, 2, "target-old", 1, 10011, publishRuntime: true);

        _ = fixture.Admit(101, 1, "requester-new", 2, 10011, publishRuntime: false);
        var staleRequester = fixture.Details.Query(requester, target.WireUserId);
        Assert.Equal(CharacterDetailQueryFailure.RequesterNotCurrent, staleRequester.Failure);

        var currentRequester = fixture.Sessions.Find(new AccountId(101))!;
        _ = fixture.Admit(202, 2, "target-new", 2, 10011, publishRuntime: true);
        var staleTargetId = fixture.Details.Query(currentRequester, target.WireUserId);

        Assert.True(staleTargetId.Succeeded);
        Assert.Equal(2, staleTargetId.Projection!.Target.ConnectionGeneration);
        Assert.NotEqual(target, staleTargetId.Projection.Target);
    }

    [Fact]
    public void UnknownAndSentinelTargetsAreRejected()
    {
        var fixture = new DetailFixture();
        var requester = fixture.Admit(101, 1, "requester", 1, 10011, publishRuntime: false);

        Assert.Equal(
            CharacterDetailQueryFailure.InvalidTarget,
            fixture.Details.Query(requester, ushort.MaxValue).Failure);
        Assert.Equal(
            CharacterDetailQueryFailure.TargetNotCurrent,
            fixture.Details.Query(requester, 42).Failure);
    }

    private sealed class DetailFixture
    {
        public DetailFixture() =>
            Details = new OtherCharacterDetailQueryDomainService(
                Sessions, Runtime, TimeProvider.System);

        public SelectedSessionDomainService Sessions { get; } = new();
        public SelectedSessionRuntimeDirectory Runtime { get; } = new();
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
                Runtime.Publish(identity, RuntimeState(identity.CharacterId), shouldPersistPosition: false);
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
}
