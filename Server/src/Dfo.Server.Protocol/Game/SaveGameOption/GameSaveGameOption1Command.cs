using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.SaveGameOption;

public sealed record GameSaveGameOption1Command(NetworkDispatchContext Dispatch, SaveGameOption1Request Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed class GameSaveGameOption1CommandHandler(IOnlineUserQueryDomainService users, ISelectedSessionDomainService sessions,
    IClientSettingsDomainService settings, ICommandWorkUnits units, ILogger<GameSaveGameOption1CommandHandler> logger)
    : ICommandHandler<GameSaveGameOption1Command, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameSaveGameOption1Command command, CancellationToken ct)
    {
        var context = command.Dispatch; var message = command.Input;
        if (!users.TryGetByConnection(context.Connection.ConnectionId, out var user) || user?.AccountId is not { } account) return false;
        var origin = new ClientSettingsOrigin(user.SessionIdentity, account, user.Generation, sessions.FindByConnection(context.Connection.ConnectionId));
        var saved = await units.WriteAsync(execution, new("SaveGameOption1",
            origin.SelectedSession is { } selected ? [new(selected, context.Connection, message.Metadata)] : []),
            (_, token) => settings.SaveMainAsync(origin, message.Options, token), ct).ConfigureAwait(false);
        logger.LogInformation("Client settings main account={AccountId} connection={ConnectionId} bytes={Bytes} saved={Saved}",
            account.Value, context.Connection.ConnectionId, message.Options.Length, saved);
        // Report-only: A21 registers no CMD response handler for this upload.
        return saved;
    }
}
