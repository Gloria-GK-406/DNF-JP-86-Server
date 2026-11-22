using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Raids;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Parties;
using Dfo.Server.Application.Raids;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Raids;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.AntonRaid;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Tests.Game.SelectCharacter;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.AntonRaid;

[Trait("Category", "Unit")]
public sealed class AntonRaidPeerCommandTests
{
    [Fact]
    public Task Application_projects_the_sender_without_a_command_read_only_work_unit() => TestGameplay.RunAsync(async () =>
    {
        var sessions = TestSessions.Directory(out var registry);
        var leader = sessions.InWorld(registry, new(1), new(1), "leader", 10200);
        var applicant = sessions.InWorld(registry, new(2), new(2), "applicant", 10200);
        var town = new OnlineCharacterLocationDomainService();
        var runtime = new OnlineCharacterRuntimeDomainService(TimeProvider.System, town);
        var area = new TownAreaKey(200, 19, 1);
        var position = new CharacterPositionState(19, 1, 100, 20, 0);
        runtime.Begin(leader, area, position, true, 100);
        runtime.Begin(applicant, area, position, true, 100);
        var parties = TestParties.Create();
        var content = TestRequired.Absent<IAntonRaidContentSource>((method, _) =>
            method.Name == nameof(IAntonRaidContentSource.Load)
                ? new AntonRaidRules(2, 20, default, default, default, 0, 0, 0, 0, []) : null);
        var raidRules = new AntonRaidRuleService(content, new PartyModeRuleService());
        var raids = new AntonRaidDomainService(sessions, town, parties, ChannelEntryOptions.Legacy, TimeProvider.System,
            parties, raidRules, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);
        var raid = raids.Create(new(leader, "Leader"u8.ToArray(), 0, 0, 0), "RaidLab"u8.ToArray());
        Assert.True(raid.Succeeded);

        var channels = TestRequired.Absent<IChannelEntryDomainService>((method, _) =>
            method.Name == nameof(IChannelEntryDomainService.ResolveAsync)
                ? ValueTask.FromResult(new ChannelEntryIdentity(200, 10200, ChannelEntryKind.Raid)) : null);
        var views = TestRequired.Absent<ISelectedCharacterCurrentViewDomainService>((method, args) =>
            method.Name == nameof(ISelectedCharacterCurrentViewDomainService.TryLoadAsync)
                ? ValueTask.FromResult<SelectCharacterResult?>(View((SelectedSessionIdentity)args![0]!)) : null);
        var projections = new ApprovalProjection();
        var workUnits = TestRequired.Absent<ICommandWorkUnits>((method, _) =>
            throw new InvalidOperationException($"Application unexpectedly opened a command work unit via {method.Name}."));
        var order = TestRequired.Absent<IOutboundOrdering>();
        var partyProjection = new PartyProjectionStreamRegistry();
        var squadProjection = new AntonRaidSquadProjection(new PartyClientIdRegistry(), projections, partyProjection,
            parties, order, sessions, town);
        var outbox = TestRequired.Absent<IGameCommandOutbox>();
        var returnProjection = new DungeonTownReturnProjection(sessions, TestRequired.Absent<IDungeonRunQueryDomainService>(),
            views, runtime, TestRequired.Absent<ITownTransitDomainService>(), town, outbox, TestRequired.Absent<IWeaknessDomainService>(),
            projections, parties, partyProjection);
        var rewards = new AntonRaidRewardProjection(TestRequired.Absent<IAntonRaidRewardSettlementDomainService>(),
            raids, sessions, views, outbox, TimeProvider.System);
        var handler = ProtocolAlgorithmFixture.Create<GameAntonRaidCommandHandler>(sessions, raids, channels, views, workUnits,
            projections, parties, order, partyProjection, outbox, squadProjection, returnProjection, rewards);
        var command = new GameAntonRaidCommand(new(new(applicant.ConnectionId, NetworkEndpointKind.Game, 10200)),
            new(GameMessageMetadata.Empty, ClientMessageType.GameRequestAntonRaidPeer, Target: leader.WireUserId));
        var request = new CommandRequestContext.ProtocolNonEffect(command.Dispatch, command.Message, command.Frame, () => true);
        var execution = new CommandExecutionContext(Guid.NewGuid(), typeof(GameAntonRaidCommand), handler.GetType()) { Request = request };

        Assert.True(await handler.HandleAsync(execution, command, default));

        var notice = Assert.IsType<AntonRaidApprovalIssued>(projections.Message);
        Assert.Equal(applicant.CharacterId, notice.Sender.Character.Identity.CharacterId);
        Assert.Equal(applicant.WireUserId, notice.Invite.Sender);
        Assert.True(raids.Respond(leader, applicant, accepted: true).Succeeded);

        SelectCharacterResult View(SelectedSessionIdentity session)
        {
            var template = SelectCharacterPayloadTestData.CreateComplete();
            return template with { Character = template.Character with
            {
                Identity = template.Character.Identity with { CharacterId = session.CharacterId },
                Position = position,
            } };
        }
    });

    private sealed class ApprovalProjection : ICommandProjections
    {
        public object? Message { get; private set; }

        public ValueTask<NotificationDelivery> ProjectAsync<TNotification>(NotificationAudience audience,
            TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : class, INotification<TNotification>
        {
            Message = notification;
            return ValueTask.FromResult(new NotificationDelivery([]));
        }
    }
}
