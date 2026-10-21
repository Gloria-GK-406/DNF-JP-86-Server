using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.MarriageProposal;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Marriage;

public sealed class MarriageCeremonyHandlerTests
{
    [Fact]
    public async Task Ordinary_reservation_request_acknowledges_and_notifies_the_invited_partner()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var sender = new RecordingSender();
            var (f, peer) = await MarriageProposalTests.Fixture(configure: s =>
            {
                s.AddSingleton<IGameMessageSender>(sender);
                s.AddSingleton<IMarriageCeremonyContentSource, Content>();
                s.AddSingleton<Dfo.Server.Abstractions.Content.ITownDefinitionSource, ChurchTestTowns>();
                s.AddCommandHandler<PrepareMarriageChurchCommand, MarriageChurchPreparationResult, PrepareMarriageChurchCommandHandler>(
                    CommandExecutionKind.Transactional, accountAccessMode: UserAccessMode.Exclusive);
                s.AddCommandHandler<MarriageCeremonyCommand, MarriageCeremonyResult, MarriageCeremonyCommandHandler>(
                    CommandExecutionKind.Transactional, accountAccessMode: UserAccessMode.Exclusive);
            });
            await using var owned = f;
            foreach (var actor in new[] { f.Session, peer })
            {
                var moved = f.Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>().CrossInto(actor, 2, 1, 100, 200);
                f.Services.GetRequiredService<Dfo.Server.Abstractions.World.ITownAreaDomainService>().TransferExact(actor.CharacterId,
                    actor.TownAreaLease(), new(1, 2, 1), moved, true);
            }
            await MarriageProposalTests.Run(f, x => x.ProposeAsync(f.Session, peer, default));
            var engaged = await MarriageProposalTests.Run(f, x => x.RespondProposalAsync(peer, f.Session, 0, default));
            f.Sql("INSERT INTO character_stats(character_id,carried_gold) VALUES(71,1000000),(72,1000000);");
            await using (var tx = f.Scope.Begin())
            {
                var bindings = f.Services.GetRequiredService<IMarriageQuestDomainStore>();
                foreach (var character in new[] { f.Session.CharacterId, peer.CharacterId })
                    foreach (var quest in new[] { 20737, 20738 })
                        await bindings.BindAsync(character, quest, engaged.Proposal!.Id, DateTimeOffset.UtcNow, default);
                await tx.CommitAsync(default);
            }
            f.Sql("INSERT INTO character_invisible_flags(character_id,slot_index,flag_value) VALUES(71,20737,1),(71,20738,1),(72,20737,1),(72,20738,1);");
            var handler = (IClientMessageHandler)ActivatorUtilities.CreateInstance(f.Services, typeof(WeddingProposalRequest).Assembly.GetType("Dfo.Server.Protocol.Game.MarriageProposal.WeddingProposalHandler")!);
            await handler.HandleAsync(new NetworkDispatchContext(new(f.Session.ConnectionId, NetworkEndpointKind.Game, 10011)),
                new WeddingProposalRequest(GameMessageMetadata.Empty, peer.WireUserId, 2, 0, 0), default);
            var delivery = Assert.Single(sender.Deliveries);
            Assert.Equal(0, Assert.IsType<WeddingProposalAck>(Assert.Single(delivery.RequiredDirectMessages)).Status);
            var target = Assert.Single(delivery.BestEffortTargets);
            Assert.Equal(peer.ConnectionId, target.ConnectionId);
            Assert.IsType<WeddingProposalNotification>(Assert.Single(target.OrderedMessages));
            Assert.Equal(2000000, f.Scalar("SELECT SUM(carried_gold) FROM character_stats"));
            Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM marriage_proposals"));
            Assert.Equal(4, f.Scalar("SELECT COUNT(*) FROM character_invisible_flags WHERE slot_index IN(20737,20738) AND flag_value=1"));

            var response = (IClientMessageHandler)ActivatorUtilities.CreateInstance(f.Services,
                typeof(WeddingProposalRequest).Assembly.GetType("Dfo.Server.Protocol.Game.MarriageProposal.WeddingResponseHandler")!);
            var peerContext = new NetworkDispatchContext(new(peer.ConnectionId, NetworkEndpointKind.Game, 10011));
            await response.HandleAsync(peerContext, new WeddingResponseRequest(GameMessageMetadata.Empty,
                f.Session.WireUserId + 100u, 2, 0, 0, 0), default);
            Assert.NotEqual(0, Assert.IsType<WeddingResponseAck>(Assert.Single(sender.Deliveries[^1].RequiredDirectMessages)).Status);
            Assert.Empty(sender.Deliveries[^1].BestEffortTargets);
            f.Sql("UPDATE character_stats SET carried_gold=499999 WHERE character_id=71");
            await response.HandleAsync(peerContext, new WeddingResponseRequest(GameMessageMetadata.Empty, 0, 2, 0, 0, 0), default);
            Assert.Equal(-3, Assert.IsType<WeddingResponseAck>(Assert.Single(sender.Deliveries[^1].RequiredDirectMessages)).Status);
            Assert.Equal(new WeddingResponseNotification(peer.WireUserId, 0, 2, -3),
                Assert.Single(Assert.Single(sender.Deliveries[^1].BestEffortTargets).OrderedMessages));
            f.Sql("UPDATE character_stats SET carried_gold=1000000 WHERE character_id=71");
            await handler.HandleAsync(new NetworkDispatchContext(new(f.Session.ConnectionId, NetworkEndpointKind.Game, 10011)),
                new WeddingProposalRequest(GameMessageMetadata.Empty, peer.WireUserId, 2, 0, 0), default);
            await response.HandleAsync(peerContext, new WeddingResponseRequest(GameMessageMetadata.Empty, 0, 2, 0, 0, 0), default);
            var accepted = sender.Deliveries[^1];
            Assert.Equal(0, Assert.IsType<WeddingResponseAck>(accepted.RequiredDirectMessages[0]).Status);
            Assert.Contains(accepted.RequiredDirectMessages, x => x.GetType().Name == "WeddingInfoMessage");
            Assert.Contains(Assert.Single(accepted.BestEffortTargets).OrderedMessages, x => x.GetType().Name == "WeddingInfoMessage");
            Assert.Equal(new WeddingResponseNotification(peer.WireUserId, 0, 2), accepted.BestEffortTargets[0].OrderedMessages[0]);
            Assert.True((await f.Services.GetRequiredService<IMarriageQueryDomainService>()
                .SnapshotAsync(f.Session.AccountId, f.Session.CharacterId, default)).IsCeremonyReserved);
            Assert.Equal(1500000, f.Scalar("SELECT SUM(carried_gold) FROM character_stats"));
            await response.HandleAsync(peerContext, new WeddingResponseRequest(GameMessageMetadata.Empty, 0, 2, 0, 0, 0), default);
            Assert.Equal(0, Assert.IsType<WeddingResponseAck>(sender.Deliveries[^1].RequiredDirectMessages[0]).Status);
            Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM marriage_ceremonies WHERE state=1"));
            Assert.Equal(2, f.Scalar("SELECT COUNT(*) FROM marriage_ceremonies"));
        });
    }

    private sealed class Content : IMarriageCeremonyContentSource
    {
        public MarriageCeremonyContent Load() => new([new(0, 500000, 0, 10)], [300, 600, 900]);
    }

    private sealed class RecordingSender : IGameMessageSender
    {
        public List<GameExplicitMessageDelivery> Deliveries { get; } = [];
        public ValueTask SendCmdAsync(NetworkConnectionId id, IGameCmdMessage message, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask SendNotiAsync(NetworkConnectionId id, IGameNotiMessage message, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask SendExplicitAsync(NetworkConnectionId id, GameExplicitMessageDelivery delivery, CancellationToken ct)
        { Deliveries.Add(delivery); return ValueTask.CompletedTask; }
    }
}
