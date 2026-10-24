using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.CrystalContract;

public sealed record GameCrystalContractCommand(NetworkDispatchContext Dispatch, CrystalContractRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed class GameCrystalContractCommandHandler(ISelectedSessionDomainService sessions,
    IOnlineUserQueryDomainService users, ICrystalContractDomainService crystals, ICommandWorkUnits units,
    ICommandProjections notifications, IGameCommandOutbox outbox, ILogger<GameCrystalContractCommandHandler> logger)
    : ICommandHandler<GameCrystalContractCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameCrystalContractCommand command, CancellationToken ct)
    {
        var context = command.Dispatch;
        var message = command.Input;
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null || !users.TryGetByConnection(context.Connection.ConnectionId, out var user) || user is null) return false;
        var saved = await units.WriteAsync(execution, new("SelectCrystalContract", []),
            (_, token) => crystals.SelectAsync(session, message.Mode, message.Selection, token), ct).ConfigureAwait(false);
        logger.LogInformation("Crystal contract selection character={CharacterId} mode={Mode} selection={Selection} saved={Saved}",
            session.CharacterId.Value, message.Mode, message.Selection, saved);
        if (!sessions.IsInWorld(session)) return saved;
        if (saved)
            await notifications.ProjectAsync(new NotificationAudience.TheseCharacters([session.CharacterId]),
                new CrystalContractSelected(session, message.Mode, message.Selection), ct).ConfigureAwait(false);
        else
            await outbox.RegisterCmdAsync(context.Connection.ConnectionId, new CrystalContractResponse(false, 0, 255), ct).ConfigureAwait(false);
        return saved;
    }
}
