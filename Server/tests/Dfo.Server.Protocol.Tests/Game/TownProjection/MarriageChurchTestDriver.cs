using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Testing;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.MarriageProposal;
using Dfo.Server.Protocol.Game.Party;
using Microsoft.Extensions.Logging.Abstractions;
namespace Dfo.Server.Protocol.Tests.Game.TownProjection;

internal static class MarriageChurchTestDriver
{
    internal static WeddingChurchEnterHandler Create(ISelectedSessionDomainService sessions, IGameMessageSender sender,
        IOnlineCharacterRuntimeDomainService positions, ITownAreaDomainService areas, ISelectedCharacterCurrentViewDomainService views,
        IEventService notifications, MarriageChurchRoom room) => new(new Driver(CreateHandler(sessions, sender, positions, areas, views, notifications, room)));
    private static GameMarriageCommandHandler CreateHandler(ISelectedSessionDomainService sessions, IGameMessageSender sender,
        IOnlineCharacterRuntimeDomainService positions, ITownAreaDomainService areas, ISelectedCharacterCurrentViewDomainService views,
        IEventService notifications, MarriageChurchRoom room)
        => new(sessions,ProtocolAlgorithmFixture.Default<IMarriageQueryDomainService>(),ProtocolAlgorithmFixture.Default<IMarriageCoupleDomainService>(),ProtocolAlgorithmFixture.Default<IMarriageVenueDomainService>(),ProtocolAlgorithmFixture.Default<IQuestOfferRefreshDomainService>(),ProtocolAlgorithmFixture.Default<ICharacterDomainService>(),views,positions,areas,            SharedTownMovementDomainService.Instance,ProtocolAlgorithmFixture.Default<IPartyService>(),new PartyProjectionStreamRegistry(),global::Dfo.Server.Testing.ProtocolAlgorithmFixture.QueryFor(sessions),new Units(room),            new global::Dfo.Server.Testing.TestCommandProjections(notifications),new Outbox(sender),TimeProvider.System,NullLogger<GameMarriageCommandHandler>.Instance,housing: ProtocolAlgorithmFixture.Default<IMarriageHousingQueryDomainService>(),housingSessions: ProtocolAlgorithmFixture.Default<IMarriageHousingSessionDomainService>(),ordering: ProtocolAlgorithmFixture.Default<IOutboundOrdering>());
    private sealed class Driver(GameMarriageCommandHandler handler) : ICommandBus
    {
        public ValueTask<TInternal> SendInternalAsync<TInternal>(ICommand<TInternal> command, InternalCommandOrigin origin, CancellationToken ct = default) where TInternal : notnull =>
            ValueTask.FromException<TInternal>(new NotSupportedException("This protocol-only test double does not accept internal commands."));

        public async ValueTask<T> SendAsync<T>(ICommand<T> value, CancellationToken ct = default) where T : notnull
        {
            var command = Assert.IsType<GameWeddingChurchEnterCommand>(value);
            var request = new CommandRequestContext.ProtocolNonEffect(command.Dispatch, command.Message, command.Frame, () => true);
            var context = DomainCommandFixture.Context(Guid.NewGuid(), value.GetType(), handler.GetType()) with { Request = request };
            return (T)(object)await handler.HandleAsync(context, command, ct);
        }
    }
    private sealed class Units(MarriageChurchRoom room) : ICommandWorkUnits
    {
        public ValueTask<T> WriteAsync<T>(CommandExecutionContext c, CommandWorkUnitDeclaration d, Func<CommandWorkUnit, CancellationToken, ValueTask<T>> action, CancellationToken ct = default) where T : notnull
        {
            Assert.Equal("EnterMarriageChurch", d.Name); Assert.Equal(CommandWorkUnitKind.Ordinary, d.Kind);
            var actor = Assert.Single(d.Participants).Session;
            var snapshot = new MarriageChurchSnapshot(room, new(actor.CharacterId.Value == 1 ? 2 : 1), "groom"u8.ToArray(), "bride"u8.ToArray(), new(2, 12, 250, 300));
            return ValueTask.FromResult((T)(object)new MarriageChurchEntryResult(0, snapshot));
        }
        public ValueTask<CommandWriteResult<T>> WriteResultAsync<T>(CommandExecutionContext c, CommandWorkUnitDeclaration d, Func<CommandWorkUnit, CancellationToken, ValueTask<T>> a, CancellationToken ct = default) where T : notnull => throw new NotSupportedException();
        public ValueTask<T> ReadOnlyAsync<T>(CommandExecutionContext c, CommandWorkUnitDeclaration d, Func<CancellationToken, ValueTask<T>> a, CancellationToken ct = default) where T : notnull => throw new NotSupportedException();
    }
    private sealed class Outbox(IGameMessageSender sender) : IGameCommandOutbox
    {
        public ValueTask RegisterCmdAsync(NetworkConnectionId id, IGameCmdMessage message, CancellationToken ct) => sender.SendCmdAsync(id, message, ct);
        public ValueTask RegisterNotiAsync(NetworkConnectionId id, IGameNotiMessage message, CancellationToken ct) => sender.SendNotiAsync(id, message, ct);
        public ValueTask RegisterBatchAsync(NetworkConnectionContext connection, IReadOnlyList<IGameServerMessage> messages, CancellationToken ct) => sender.SendBatchAsync(connection, messages, ct);
        public ValueTask RegisterExplicitAsync(NetworkConnectionId id, GameExplicitMessageDelivery delivery, CancellationToken ct) => sender.SendExplicitAsync(id, delivery, ct);
    }
}
