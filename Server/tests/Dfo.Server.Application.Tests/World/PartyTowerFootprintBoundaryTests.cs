using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.World;

public sealed partial class BossTowerDomainServiceTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Task39_actual_host_encoder_failure_keeps_old_and_whole_exact_close_scope(bool legacy)
    {
        // This domain fixture has no complete selected-entry profile. No available current view
        // keeps this oracle about the real host encoder rather than an unrelated return-profile failure.
        using var f = Fixture(configure: services => services.AddSingleton<Dfo.Server.Abstractions.Sessions.ISelectedCharacterCurrentViewDomainService, Task39NoReturnView>());
        SelectedSessionIdentity peer = null!;
        BossTowerSnapshot run = null!;
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            f.SetLevel(65); await f.Seed(65, 2749110, 3); peer = await AddMember(f, 1);
            Assert.True(f.Services.GetRequiredService<IOnlineUserQueryDomainService>().TryGetByConnection(peer.ConnectionId, out var user));
            f.Sender.Outbound.Connections.Add(user!.Connection);
            var parties = f.Services.GetRequiredService<IPartyService>();
            PartyCharacterSnapshot Character(SelectedSessionIdentity session) => new(session.AccountId, session.CharacterId,
                session.WireUserId, new PartyDirectoryScope(1, 0, 0));
            Assert.True(parties.RequestInvite(Character(f.Session), Character(peer), new(0, 0, 0, 0)).Succeeded);
            Assert.True(parties.AcceptInvite(Character(peer), Character(f.Session)).Succeeded);
            run = (await Tower(f).EnterAsync([new(f.Session, 0), new(peer, 1)], f.Session, 42, default)).Run!;
        });
        var attempts = 0;
        f.Sender.Outbound.Fail(peer.ConnectionId, message => message.MessageType == ServerMessageType.GameUdpHost,
            () => { attempts++; return new IOException("TASK39 real host encoder failure"); });
        if (legacy)
            await TestGameplay.RunAsync(f.Services, async () =>
                await TestEffectFootprints.NonEffect(f.Services, GameMessageMetadata.Empty, async () =>
                {
                    var result = await TowerTestAlgorithms.BossParty(f).TryLeaveAsync(
                        f.Session, false, null, default);
                    Assert.NotNull(result); Assert.True(result.Result.Succeeded);
                }));
        else
            await f.Services.GetRequiredService<LeavePartyHandler>().HandleAsync(new(f.Connection), new LeavePartyRequest(GameMessageMetadata.Empty), default);
        Assert.Equal(1, attempts);
        TestGameplay.Run(f.Services, () =>
        {
            Assert.Null(Tower(f).Find(f.Session));
            Assert.Equal(peer, Assert.Single(Tower(f).Find(peer)!.Members).Session);
            Assert.Equal(peer.CharacterId, Assert.Single(f.Services.GetRequiredService<IPartyService>()
                .FindParty(new(peer.AccountId, peer.CharacterId, peer.WireUserId, null)).Members).CharacterId);
        });
        Assert.Equal([peer.ConnectionId], f.Sender.Outbound.Closes);
        Assert.DoesNotContain(f.Sender.Messages, message => message.MessageType == ServerMessageType.GameUdpHost);
    }

    [Fact]
    public async Task Task39_cancellation_after_the_real_tower_commit_still_mutates_the_party_and_keeps_the_ready_phase_rule()
    {
        using var request = new CancellationTokenSource();
        var writes = new List<string>();
        using var f = Fixture(configure: services => Task39UnitObserver.Install(services, writes, request, "party-leave-boss-tower"));
        SelectedSessionIdentity peer = null!;
        BossTowerSnapshot run = null!;
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            f.SetLevel(65); await f.Seed(65, 2749110, 3); peer = await AddMember(f, 1);
            var parties = f.Services.GetRequiredService<IPartyService>();
            PartyCharacterSnapshot Character(SelectedSessionIdentity session) => new(session.AccountId, session.CharacterId,
                session.WireUserId, new PartyDirectoryScope(1, 0, 0));
            Assert.True(parties.RequestInvite(Character(f.Session), Character(peer), new(0, 0, 0, 0)).Succeeded);
            Assert.True(parties.AcceptInvite(Character(peer), Character(f.Session)).Succeeded);
            run = (await Tower(f).EnterAsync([new(f.Session, 0), new(peer, 1)], f.Session, 42, default)).Run!;
            await Tower(f).LoadAsync(Context(run, f.Session), default);
            await Tower(f).LoadAsync(Context(run, peer), default);
            Assert.Equal(TowerRoomPhase.Ready, Tower(f).Find(f.Session)!.Room.Phase);
        });
        await f.Services.GetRequiredService<LeavePartyHandler>().HandleAsync(new(f.Connection), new LeavePartyRequest(GameMessageMetadata.Empty), request.Token);
        Assert.True(request.IsCancellationRequested);
        Assert.Equal(["party-leave-boss-tower", "party-start-boss-tower"], writes);
        TestGameplay.Run(f.Services, () =>
        {
            Assert.Null(Tower(f).Find(f.Session));
            var survivingRun = Tower(f).Find(peer)!;
            Assert.Equal(TowerRoomPhase.Fighting, survivingRun.Room.Phase);
            Assert.Equal(peer, Assert.Single(survivingRun.Members).Session);
            Assert.Equal(peer.CharacterId, Assert.Single(f.Services.GetRequiredService<IPartyService>()
                .FindParty(new(peer.AccountId, peer.CharacterId, peer.WireUserId, null)).Members).CharacterId);
        });
    }

    [Fact]
    public async Task Task39_whole_leave_settles_the_remaining_dead_member_with_the_declared_non_actor_owner()
    {
        using var f = Fixture();
        SelectedSessionIdentity peer = null!;
        BossTowerSnapshot run = null!;
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            f.SetLevel(65);
            await f.Seed(65, 2749110, 3);
            peer = await AddMember(f, 1);
            var parties = f.Services.GetRequiredService<IPartyService>();
            PartyCharacterSnapshot Character(SelectedSessionIdentity session) => new(session.AccountId, session.CharacterId,
                session.WireUserId, new PartyDirectoryScope(1, 0, 0));
            Assert.True(parties.RequestInvite(Character(f.Session), Character(peer), new(0, 0, 0, 0)).Succeeded);
            Assert.True(parties.AcceptInvite(Character(peer), Character(f.Session)).Succeeded);
            run = (await Tower(f).EnterAsync([new(f.Session, 0), new(peer, 1)], f.Session, 42, default)).Run!;
            await Tower(f).LoadAsync(Context(run, f.Session), default);
            await Tower(f).LoadAsync(Context(run, peer), default);
            await Tower(f).StartAsync(Context(run, f.Session), default);
            await TestEffectFootprints.NonEffect(f.Services, GameMessageMetadata.Empty, async () =>
            {
                var dead = await TestEffectFootprints.Run(new[]{f.Session,peer},cap=>Tower(f).DieAsync(Context(run,peer),cap,default));
                Assert.Null(dead.Run!.Completion);
                Assert.True(dead.Run.Members.Single(member => member.Session == peer).Dead);
            });
        });
        var before = f.Scalar("SELECT exp FROM characters WHERE character_id=3");
        await f.Services.GetRequiredService<LeavePartyHandler>().HandleAsync(new(f.Connection), new LeavePartyRequest(GameMessageMetadata.Empty), default);
        TestGameplay.Run(f.Services, () =>
        {
            Assert.Null(Tower(f).Find(f.Session));
            var settled = Tower(f).Find(peer)!;
            Assert.True(settled.Completion!.Settled);
            var award = Assert.Single(settled.Completion.Receipt!.Members);
            Assert.Equal(peer, award.Session);
            Assert.Equal(peer.CharacterId, Assert.Single(f.Services.GetRequiredService<IPartyService>()
                .FindParty(new(peer.AccountId, peer.CharacterId, peer.WireUserId, null)).Members).CharacterId);
            Assert.True(f.Scalar("SELECT exp FROM characters WHERE character_id=3") > before);
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Task39_whole_and_legacy_tower_departure_keep_the_same_membership_boundary(bool remove, bool legacy)
    {
        using var f = Fixture();
        SelectedSessionIdentity peer = null!;
        BossTowerSnapshot run = null!;
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            f.SetLevel(65);
            await f.Seed(65, 2749110, 3);
            peer = await AddMember(f, 1);
            var parties = f.Services.GetRequiredService<IPartyService>();
            PartyCharacterSnapshot Character(SelectedSessionIdentity session) => new(session.AccountId, session.CharacterId, session.WireUserId, new PartyDirectoryScope(1, 0, 0));
            Assert.True(parties.RequestInvite(Character(f.Session), Character(peer), new(0, 0, 0, 0)).Succeeded);
            Assert.True(parties.AcceptInvite(Character(peer), Character(f.Session)).Succeeded);
            run = (await Tower(f).EnterAsync([new(f.Session, 0), new(peer, 1)], f.Session, 42, default)).Run!;
            Assert.Equal(2, run.Members.Length);
        });

        if (legacy)
        {
            await TestGameplay.RunAsync(f.Services, async () =>
                await TestEffectFootprints.NonEffect(f.Services, GameMessageMetadata.Empty, async () =>
                {
                    var departing = remove ? peer : f.Session;
                    var old = await new TowerTestAlgorithms.Mutations(f).BossLeave(Context(run, departing));
                    Assert.True(old.Changed);
                }));
        }
        else if (remove)
            await f.Services.GetRequiredService<RemovePartyMemberHandler>().HandleAsync(new(f.Connection), new RemovePartyMemberRequest(GameMessageMetadata.Empty, 1), default);
        else
            await f.Services.GetRequiredService<LeavePartyHandler>().HandleAsync(new(f.Connection), new LeavePartyRequest(GameMessageMetadata.Empty), default);

        TestGameplay.Run(f.Services, () =>
        {
            var departing = remove ? peer : f.Session;
            var survivor = remove ? f.Session : peer;
            Assert.Null(Tower(f).Find(departing));
            Assert.Equal(survivor, Assert.Single(Tower(f).Find(survivor)!.Members).Session);
            var parties = f.Services.GetRequiredService<IPartyService>();
            var roster = parties.FindParty(new(survivor.AccountId, survivor.CharacterId, survivor.WireUserId, null));
            Assert.Equal(legacy ? 2 : 1, roster.Members.Length);
            if (!legacy) Assert.Equal(survivor.CharacterId, Assert.Single(roster.Members).CharacterId);
        });
    }
}

public sealed partial class IllusionTowerDomainServiceTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Task39_actual_host_encoder_failure_keeps_old_and_whole_exact_close_scope(bool legacy)
    {
        // This domain fixture has no complete selected-entry profile. No available current view
        // keeps this oracle about the real host encoder rather than an unrelated return-profile failure.
        using var f = Fixture(configure: services => services.AddSingleton<Dfo.Server.Abstractions.Sessions.ISelectedCharacterCurrentViewDomainService, Task39NoReturnView>());
        SelectedSessionIdentity peer = null!;
        IllusionTowerSnapshot run = null!;
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            f.SetLevel(50); await f.Seed(65, 4183, 3); peer = await AddMember(f, 1);
            Assert.True(f.Services.GetRequiredService<IOnlineUserQueryDomainService>().TryGetByConnection(peer.ConnectionId, out var user));
            f.Sender.Outbound.Connections.Add(user!.Connection);
            var parties = f.Services.GetRequiredService<IPartyService>();
            PartyCharacterSnapshot Character(SelectedSessionIdentity session) => new(session.AccountId, session.CharacterId,
                session.WireUserId, new PartyDirectoryScope(1, 0, 0));
            Assert.True(parties.RequestInvite(Character(f.Session), Character(peer), new(0, 0, 0, 0)).Succeeded);
            Assert.True(parties.AcceptInvite(Character(peer), Character(f.Session)).Succeeded);
            run = (await Tower(f).EnterAsync([new(f.Session, 0), new(peer, 1)], f.Session, 42, default)).Run!;
        });
        var attempts = 0;
        f.Sender.Outbound.Fail(peer.ConnectionId, message => message.MessageType == ServerMessageType.GameUdpHost,
            () => { attempts++; return new IOException("TASK39 real host encoder failure"); });
        if (legacy)
            await TestGameplay.RunAsync(f.Services, async () =>
                await TestEffectFootprints.NonEffect(f.Services, GameMessageMetadata.Empty, async () =>
                {
                    var result = await TowerTestAlgorithms.IllusionParty(f).TryLeaveAsync(
                        f.Session, false, null, default);
                    Assert.NotNull(result); Assert.True(result.Result.Succeeded);
                }));
        else
            await f.Services.GetRequiredService<LeavePartyHandler>().HandleAsync(new(f.Connection), new LeavePartyRequest(GameMessageMetadata.Empty), default);
        Assert.Equal(1, attempts);
        TestGameplay.Run(f.Services, () =>
        {
            Assert.Null(Tower(f).Find(f.Session));
            Assert.Equal(peer, Assert.Single(Tower(f).Find(peer)!.Members).Session);
            Assert.Equal(peer.CharacterId, Assert.Single(f.Services.GetRequiredService<IPartyService>()
                .FindParty(new(peer.AccountId, peer.CharacterId, peer.WireUserId, null)).Members).CharacterId);
        });
        Assert.Equal([peer.ConnectionId], f.Sender.Outbound.Closes);
        Assert.DoesNotContain(f.Sender.Messages, message => message.MessageType == ServerMessageType.GameUdpHost);
    }

    [Fact]
    public async Task Task39_cancellation_after_the_real_tower_commit_still_mutates_the_party_and_keeps_the_ready_phase_rule()
    {
        using var request = new CancellationTokenSource();
        var writes = new List<string>();
        using var f = Fixture(configure: services => Task39UnitObserver.Install(services, writes, request, "party-leave-illusion-tower"));
        SelectedSessionIdentity peer = null!;
        IllusionTowerSnapshot run = null!;
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            f.SetLevel(50); await f.Seed(65, 4183, 3); peer = await AddMember(f, 1);
            var parties = f.Services.GetRequiredService<IPartyService>();
            PartyCharacterSnapshot Character(SelectedSessionIdentity session) => new(session.AccountId, session.CharacterId,
                session.WireUserId, new PartyDirectoryScope(1, 0, 0));
            Assert.True(parties.RequestInvite(Character(f.Session), Character(peer), new(0, 0, 0, 0)).Succeeded);
            Assert.True(parties.AcceptInvite(Character(peer), Character(f.Session)).Succeeded);
            run = (await Tower(f).EnterAsync([new(f.Session, 0), new(peer, 1)], f.Session, 42, default)).Run!;
            await Tower(f).LoadAsync(Context(run, f.Session), default);
            await Tower(f).LoadAsync(Context(run, peer), default);
            Assert.Equal(TowerRoomPhase.Ready, Tower(f).Find(f.Session)!.Room.Phase);
        });
        await f.Services.GetRequiredService<LeavePartyHandler>().HandleAsync(new(f.Connection), new LeavePartyRequest(GameMessageMetadata.Empty), request.Token);
        Assert.True(request.IsCancellationRequested);
        Assert.Equal(["party-leave-illusion-tower"], writes);
        TestGameplay.Run(f.Services, () =>
        {
            Assert.Null(Tower(f).Find(f.Session));
            var survivingRun = Tower(f).Find(peer)!;
            Assert.Equal(TowerRoomPhase.Ready, survivingRun.Room.Phase);
            Assert.Equal(peer, Assert.Single(survivingRun.Members).Session);
            Assert.Equal(peer.CharacterId, Assert.Single(f.Services.GetRequiredService<IPartyService>()
                .FindParty(new(peer.AccountId, peer.CharacterId, peer.WireUserId, null)).Members).CharacterId);
        });
    }

    [Fact]
    public async Task Task39_whole_leave_settles_the_remaining_dead_member_with_the_declared_non_actor_owner()
    {
        using var f = Fixture(new Rules { Probabilities = [0, 0, 100] }, rewards: true);
        SelectedSessionIdentity peer = null!;
        IllusionTowerSnapshot run = null!;
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            f.SetLevel(50);
            await f.Seed(65, 4183, 3);
            peer = await AddMember(f, 1);
            var parties = f.Services.GetRequiredService<IPartyService>();
            PartyCharacterSnapshot Character(SelectedSessionIdentity session) => new(session.AccountId, session.CharacterId,
                session.WireUserId, new PartyDirectoryScope(1, 0, 0));
            Assert.True(parties.RequestInvite(Character(f.Session), Character(peer), new(0, 0, 0, 0)).Succeeded);
            Assert.True(parties.AcceptInvite(Character(peer), Character(f.Session)).Succeeded);
            run = (await Tower(f).EnterAsync([new(f.Session, 0), new(peer, 1)], f.Session, 42, default)).Run!;
            await Tower(f).LoadAsync(Context(run, f.Session), default);
            await Tower(f).LoadAsync(Context(run, peer), default);
            await Tower(f).StartAsync(Context(run, f.Session), default);
            await TestEffectFootprints.NonEffect(f.Services, GameMessageMetadata.Empty, async () =>
            {
                var dead = await TestEffectFootprints.Run(new[]{f.Session,peer},cap=>Tower(f).DieAsync(Context(run,peer),cap,default));
                Assert.Null(dead.Run!.Completion);
                Assert.True(dead.Run.Members.Single(member => member.Session == peer).Dead);
            });
        });
        var before = f.Scalar("SELECT exp FROM characters WHERE character_id=3");
        await f.Services.GetRequiredService<LeavePartyHandler>().HandleAsync(new(f.Connection), new LeavePartyRequest(GameMessageMetadata.Empty), default);
        TestGameplay.Run(f.Services, () =>
        {
            Assert.Null(Tower(f).Find(f.Session));
            var settled = Tower(f).Find(peer)!;
            Assert.True(settled.Completion!.Settled);
            var award = Assert.Single(settled.Completion.Receipt!.Members);
            Assert.Equal(peer, award.Session);
            Assert.Equal(peer.CharacterId, Assert.Single(f.Services.GetRequiredService<IPartyService>()
                .FindParty(new(peer.AccountId, peer.CharacterId, peer.WireUserId, null)).Members).CharacterId);
            Assert.True(f.Scalar("SELECT exp FROM characters WHERE character_id=3") > before);
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Task39_whole_and_legacy_tower_departure_keep_the_same_membership_boundary(bool remove, bool legacy)
    {
        using var f = Fixture();
        SelectedSessionIdentity peer = null!;
        IllusionTowerSnapshot run = null!;
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await f.Seed(65, 4183, 3);
            peer = await AddMember(f, 1);
            var parties = f.Services.GetRequiredService<IPartyService>();
            Assert.True(parties.RequestInvite(PartyCharacter(f.Session), PartyCharacter(peer), new(0, 0, 0, 0)).Succeeded);
            Assert.True(parties.AcceptInvite(PartyCharacter(peer), PartyCharacter(f.Session)).Succeeded);
            run = (await Tower(f).EnterAsync([new(f.Session, 0), new(peer, 1)], f.Session, 42, default)).Run!;
            Assert.Equal(2, run.Members.Length);
        });

        if (legacy)
        {
            await TestGameplay.RunAsync(f.Services, async () =>
                await TestEffectFootprints.NonEffect(f.Services, GameMessageMetadata.Empty, async () =>
                {
                    var departing = remove ? peer : f.Session;
                    var old = await new TowerTestAlgorithms.Mutations(f).IllusionLeave(Context(run, departing));
                    Assert.True(old.Changed);
                }));
        }
        else if (remove)
            await f.Services.GetRequiredService<RemovePartyMemberHandler>().HandleAsync(new(f.Connection), new RemovePartyMemberRequest(GameMessageMetadata.Empty, 1), default);
        else
            await f.Services.GetRequiredService<LeavePartyHandler>().HandleAsync(new(f.Connection), new LeavePartyRequest(GameMessageMetadata.Empty), default);

        TestGameplay.Run(f.Services, () =>
        {
            var departing = remove ? peer : f.Session;
            var survivor = remove ? f.Session : peer;
            Assert.Null(Tower(f).Find(departing));
            Assert.Equal(survivor, Assert.Single(Tower(f).Find(survivor)!.Members).Session);
            var parties = f.Services.GetRequiredService<IPartyService>();
            var roster = parties.FindParty(new(survivor.AccountId, survivor.CharacterId, survivor.WireUserId, null));
            Assert.Equal(legacy ? 2 : 1, roster.Members.Length);
            if (!legacy) Assert.Equal(survivor.CharacterId, Assert.Single(roster.Members).CharacterId);
        });
    }
}

internal sealed class Task39UnitObserver(ICommandWorkUnits inner, List<string> writes,
    CancellationTokenSource request, string cancelAfter) : ICommandWorkUnits
{
    internal static void Install(IServiceCollection services, List<string> writes, CancellationTokenSource request, string cancelAfter)
    {
        var descriptor = services.Last(value => value.ServiceType == typeof(ICommandWorkUnits));
        services.Remove(descriptor);
        services.AddSingleton<ICommandWorkUnits>(provider => new Task39UnitObserver(
            descriptor.ImplementationInstance as ICommandWorkUnits
                ?? descriptor.ImplementationFactory?.Invoke(provider) as ICommandWorkUnits
                ?? (ICommandWorkUnits)ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!), writes, request, cancelAfter));
    }
    public async ValueTask<T> WriteAsync<T>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
        Func<CommandWorkUnit, CancellationToken, ValueTask<T>> action, CancellationToken token = default) where T : notnull
    {
        writes.Add(declaration.Name);
        var result = await inner.WriteAsync(context, declaration, action, token);
        if (declaration.Name == cancelAfter) request.Cancel();
        return result;
    }
    public ValueTask<CommandWriteResult<T>> WriteResultAsync<T>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
        Func<CommandWorkUnit, CancellationToken, ValueTask<T>> action, CancellationToken token = default) where T : notnull =>
        inner.WriteResultAsync(context, declaration, action, token);
    public ValueTask<T> ReadOnlyAsync<T>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
        Func<CancellationToken, ValueTask<T>> action, CancellationToken token = default) where T : notnull =>
        inner.ReadOnlyAsync(context, declaration, action, token);
}

internal sealed class Task39NoReturnView : Dfo.Server.Abstractions.Sessions.ISelectedCharacterCurrentViewDomainService
{
    public ValueTask<Dfo.Server.Data.Game.SelectCharacterResult?> TryLoadAsync(SelectedSessionIdentity session, CancellationToken token) =>
        ValueTask.FromResult<Dfo.Server.Data.Game.SelectCharacterResult?>(null);
}
