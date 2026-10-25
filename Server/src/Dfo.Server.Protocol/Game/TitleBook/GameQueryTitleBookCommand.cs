using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game.TitleBook;

public sealed record GameQueryTitleBookCommand(NetworkDispatchContext Dispatch, QueryTitleBookRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed class GameQueryTitleBookCommandHandler(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService users,
    ITitleBookDomainService titles, ICommandWorkUnits units, IGameCommandOutbox outbox)
    : ICommandHandler<GameQueryTitleBookCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameQueryTitleBookCommand command, CancellationToken ct)
    {
        var context = command.Dispatch; var message = command.Input;
        if (!message.Complete || message.Owner == ushort.MaxValue) return false;
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        var target = sessions.TryGetInWorld(message.Owner, out var inWorld) ? inWorld : null;
        if (session is null || target is null || target.ListenerPort != session.ListenerPort
            || !users.TryGetByConnection(context.Connection.ConnectionId, out var user) || user is null) return false;
        var rows = await units.ReadOnlyAsync<IReadOnlyList<CharacterTitleBookCategoryState>>(execution,
            new("QueryTitleBook", [new(session, context.Connection, message.Metadata)]), async token => {
                if (target.ListenerPort != session.ListenerPort || !sessions.IsInWorld(target)) return [];
                return await titles.GetAsync(target.CharacterId, token).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
        if (!sessions.IsInWorld(target) || !sessions.IsInWorld(session)) return false;
        await outbox.RegisterExplicitAsync(session.ConnectionId,
            new([.. rows.Select(x => (IGameServerMessage)new TitleBookStateMessage(x with { InfoType = 1, OwnerId16 = target.WireUserId }))], []), ct).ConfigureAwait(false);
        return true;
    }
}
