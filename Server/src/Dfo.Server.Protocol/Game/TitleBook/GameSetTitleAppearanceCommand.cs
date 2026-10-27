using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.TitleBook;

public sealed record GameSetTitleAppearanceCommand(NetworkDispatchContext Dispatch, SetTitleAppearanceRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed class GameSetTitleAppearanceCommandHandler(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService users,
    ITitleBookDomainService titles, ICommandWorkUnits units, IGameCommandOutbox outbox)
    : ICommandHandler<GameSetTitleAppearanceCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameSetTitleAppearanceCommand command, CancellationToken ct)
    {
        var context = command.Dispatch; var message = command.Input;
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        if (!message.Complete || message.ItemId < 0 || session is null
            || !users.TryGetByConnection(context.Connection.ConnectionId, out var user) || user is null)
        { await outbox.RegisterCmdAsync(context.Connection.ConnectionId, new SetTitleAppearanceResponse(false, 0), ct).ConfigureAwait(false); return false; }
        var success = await units.WriteAsync(execution, new("SetTitleAppearance", [new(session, context.Connection, message.Metadata)]),
            (_, token) => titles.SetAppearanceAsync(session.CharacterId, message.ItemId, token), ct).ConfigureAwait(false);
        await outbox.RegisterCmdAsync(session.ConnectionId, new SetTitleAppearanceResponse(success, Math.Max(0, message.ItemId)), ct).ConfigureAwait(false);
        return success;
    }
}
