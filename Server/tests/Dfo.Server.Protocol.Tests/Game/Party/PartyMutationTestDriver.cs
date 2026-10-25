using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.Party;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Protocol.Tests.Game.Party;

internal static class PartyMutationTestDriver
{
    internal static GamePartyMutationCommandHandler Handler(ISelectedSessionDomainService sessions, IPartyService parties,
        ITownAreaDomainService? town = null, IEventService? events = null, PartyProjectionStreamRegistry? streams = null,
        IGameMessageSender? sender = null, IDungeonRunDomainService? runs = null) =>
        new(sessions, parties, town!, new LegacyCommandProjections(events!), streams ?? new(),
            NullLogger<GamePartyMutationCommandHandler>.Instance, outbox: sender is null ? null : new Outbox(sender), runs: runs);

    internal static ICommandBus Bus(GamePartyMutationCommandHandler handler, Action<ICommand>? observe = null) => new Driver(handler, observe);

    private sealed class Driver(GamePartyMutationCommandHandler handler, Action<ICommand>? observe) : ICommandBus
    {
        public async ValueTask<TResult> SendAsync<TResult>(ICommand<TResult> value, CancellationToken token = default) where TResult : notnull
        {
            observe?.Invoke(value);
            var command = (IProtocolCommand)value;
            var request = new CommandRequestContext.ProtocolNonEffect(command.Dispatch, command.Message, command.Frame, () => true);
            var context = new CommandExecutionContext(Guid.NewGuid(), value.GetType(), handler.GetType(), CommandExecutionKind.Direct) { Request = request };
            var result = value switch
            {
                GameLeavePartyCommand leave => await handler.HandleAsync(context, leave, token),
                GameRemovePartyMemberCommand remove => await handler.HandleAsync(context, remove, token),
                GameChangePartyLeaderCommand transfer => await handler.HandleAsync(context, transfer, token),
                _ => throw new NotSupportedException(value.GetType().Name),
            };
            return (TResult)(object)result;
        }
    }

    internal sealed class Outbox(IGameMessageSender sender) : IGameCommandOutbox
    {
        public ValueTask RegisterCmdAsync(NetworkConnectionId connection, IGameCmdMessage message, CancellationToken token) => sender.SendCmdAsync(connection, message, token);
        public ValueTask RegisterNotiAsync(NetworkConnectionId connection, IGameNotiMessage message, CancellationToken token) => sender.SendNotiAsync(connection, message, token);
        public ValueTask RegisterBatchAsync(NetworkConnectionContext connection, IReadOnlyList<IGameServerMessage> messages, CancellationToken token) =>
            sender.SendExplicitAsync(connection.ConnectionId, new(messages, []), token);
        public ValueTask RegisterExplicitAsync(NetworkConnectionId connection, GameExplicitMessageDelivery delivery, CancellationToken token) => sender.SendExplicitAsync(connection, delivery, token);
    }
}

internal sealed class PartyMutationProbe(IPartyService inner, Action<string, CharacterId, CharacterId?>? observe = null,
    bool throwAfterLeave = false) : IPartyService
{
    public IReadOnlyList<PartySnapshot> Query(PartyDirectoryQuery query) => inner.Query(query);
    public void UpdateDirectoryPresence(CharacterId character, PartyDirectoryScope? scope) => inner.UpdateDirectoryPresence(character, scope);
    public PartyOperationResult CreateOrUpdateSettings(PartyCharacterSnapshot character, PartySettings settings, PartyTitle title) => inner.CreateOrUpdateSettings(character, settings, title);
    public PartyOperationResult RequestInvite(PartyCharacterSnapshot inviter, PartyCharacterSnapshot target, PartyInviteDisplayState display) => inner.RequestInvite(inviter, target, display);
    public PartyOperationResult AcceptInvite(PartyCharacterSnapshot invitee, PartyCharacterSnapshot inviter) => inner.AcceptInvite(invitee, inviter);
    public PartyOperationResult TransferLeadership(CharacterId leader, CharacterId target) { observe?.Invoke("transfer", leader, target); return inner.TransferLeadership(leader, target); }
    public PartyOperationResult RemoveMember(CharacterId leader, CharacterId target) { observe?.Invoke("remove", leader, target); return inner.RemoveMember(leader, target); }
    public PartyOperationResult LeaveParty(CharacterId character)
    {
        observe?.Invoke("leave", character, null);
        var result = inner.LeaveParty(character);
        return throwAfterLeave ? throw new IOException("wrapper failed after party write") : result;
    }
    public PartyOperationResult CleanupCharacter(CharacterId character) => inner.CleanupCharacter(character);
    public PartySnapshot FindParty(PartyCharacterSnapshot character) => inner.FindParty(character);
    public PartyMembershipSnapshot? FindByKey(PartyKey key) => inner.FindByKey(key);
}

internal sealed class RecordingPartyMutations(Action<CharacterId>? observed = null) : IPartyService
{
    internal List<CharacterId> Left { get; } = [];
    public PartyOperationResult LeaveParty(CharacterId character) { Left.Add(character); observed?.Invoke(character); return PartyOperationResult.Success(); }
    public IReadOnlyList<PartySnapshot> Query(PartyDirectoryQuery query) => throw new NotSupportedException();
    public void UpdateDirectoryPresence(CharacterId character, PartyDirectoryScope? scope) => throw new NotSupportedException();
    public PartyOperationResult CreateOrUpdateSettings(PartyCharacterSnapshot character, PartySettings settings, PartyTitle title) => throw new NotSupportedException();
    public PartyOperationResult RequestInvite(PartyCharacterSnapshot inviter, PartyCharacterSnapshot target, PartyInviteDisplayState display) => throw new NotSupportedException();
    public PartyOperationResult AcceptInvite(PartyCharacterSnapshot invitee, PartyCharacterSnapshot inviter) => throw new NotSupportedException();
    public PartyOperationResult TransferLeadership(CharacterId leader, CharacterId target) => throw new NotSupportedException();
    public PartyOperationResult RemoveMember(CharacterId leader, CharacterId target) => throw new NotSupportedException();
    public PartyOperationResult CleanupCharacter(CharacterId character) => throw new NotSupportedException();
    public PartySnapshot FindParty(PartyCharacterSnapshot character) => throw new NotSupportedException();
    public PartyMembershipSnapshot? FindByKey(PartyKey key) => throw new NotSupportedException();
}
